using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// Turns a snapshot of the whole session tree into one <see cref="SessionListPageModel"/>
/// (FR.15.13): candidate selection, filters, ordering and nesting, paging, sections, flags and
/// Short Node Ids. Pure — all file-system work is injected as delegates and runs only for the
/// sessions that need it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionListPageBuilder
{
	private const string WorkersSectionKey = "workers";

	public SessionListPageModel Build(
		IReadOnlyList<SessionSnapshotModel> sessions,
		string callerNodeId,
		SessionListQueryModel query,
		Func<SessionSnapshotModel, DateTimeOffset?> getLastActivityUtc,
		Func<SessionSnapshotModel, bool> isResumable,
		DateTimeOffset nowUtc)
	{
		query = query with { Query = string.IsNullOrWhiteSpace(query.Query) ? null : query.Query.Trim() };

		var limit  = Math.Clamp(query.Limit, 1, Constants.Agent.ListSessionsMaxLimit);
		var offset = Math.Max(query.Offset, 0);

		var index     = new SessionListIndex(sessions);
		query         = query with { TagCarrierNodeIds = ExpandToWorkers(index, query.TagCarrierNodeIds) };
		var caller    = index.Find(callerNodeId);
		var isWorkers = query.Scope is SessionListScope.Children or SessionListScope.Subtree;
		var scopeRoot = isWorkers ? index.Find(query.RootNodeId ?? callerNodeId) : null;
		var context   = new SessionListBuildContext(index, caller, scopeRoot, query, getLastActivityUtc, nowUtc);

		var candidates = CollectCandidates(context);
		var filtered   = candidates.Where(s => Matches(context, s)).ToList();
		var rows       = Layout(context, filtered, out var sections);

		var pageRows = rows.Skip(offset).Take(limit).ToList();
		var page     = AssemblePage(context, pageRows, sections, isResumable);

		return new SessionListPageModel
		{
			Scope              = query.Scope,
			Caller             = BuildCaller(context),
			Sections           = page.Sections,
			Rows               = page.Rows,
			TotalRows          = rows.Count,
			Offset             = offset,
			Limit              = limit,
			WorkersHintShortId = FindWorkersHint(context, page.Rows),
		};
	}

	/// <summary>
	/// A Worker Session rolls up to its top-level supervisor (FR.24.12), so the carriers (already top-level, resolved
	/// by <see cref="SessionTagIndexBuilder"/>, which owns the supervisor-chain and cycle rule) plus all their
	/// descendants are the sessions that count as carrying the tags. Null stays null (no filter).
	/// </summary>
	private IReadOnlySet<string>? ExpandToWorkers(SessionListIndex index, IReadOnlySet<string>? carriers)
	{
		if (carriers == null)
			return null;

		var expanded = new HashSet<string>(carriers, StringComparer.OrdinalIgnoreCase);
		foreach (var carrier in carriers)
			foreach (var worker in index.Descendants(carrier))
				expanded.Add(worker.NodeId);
		return expanded;
	}

	private IReadOnlyList<SessionSnapshotModel> CollectCandidates(SessionListBuildContext ctx)
	{
		var query = ctx.Query;
		var index = ctx.Index;

		if (query.Scope == SessionListScope.Children)
			return ctx.ScopeRoot == null ? [] : index.DirectWorkers(ctx.ScopeRoot.NodeId);

		if (query.Scope == SessionListScope.Subtree)
			return ctx.ScopeRoot == null ? [] : index.Descendants(ctx.ScopeRoot.NodeId);

		if (query.Scope == SessionListScope.Project && ctx.Caller == null)
			return [];

		var includeAllWorkers = query.IncludeWorkers || !string.IsNullOrEmpty(query.Query);
		var result = new Dictionary<string, SessionSnapshotModel>(StringComparer.OrdinalIgnoreCase);

		foreach (var s in index.All)
		{
			if (query.Scope == SessionListScope.Project && !IsSameDirectory(s.DirectoryPath, ctx.Caller!.DirectoryPath))
				continue;

			if (!index.IsWorker(s) || includeAllWorkers)
				result[s.NodeId] = s;
		}

		if (ctx.Caller != null)
		{
			result[ctx.Caller.NodeId] = ctx.Caller;
			foreach (var worker in index.DirectWorkers(ctx.Caller.NodeId))
				result[worker.NodeId] = worker;
		}

		return result.Values.ToList();
	}

	private bool Matches(SessionListBuildContext ctx, SessionSnapshotModel s)
	{
		var query = ctx.Query;

		if (!string.IsNullOrEmpty(query.Query) && !MatchesQueryText(s, query.Query))
			return false;

		if (query.Running.HasValue && s.IsRunning != query.Running.Value)
			return false;

		if (query.TagCarrierNodeIds != null && !query.TagCarrierNodeIds.Contains(s.NodeId))
			return false;

		if (!query.ActiveWithinHours.HasValue)
			return true;

		var last = ctx.GetLastActivity(s);
		return last.HasValue && ctx.Now - last.Value <= TimeSpan.FromHours(query.ActiveWithinHours.Value);
	}

	private bool MatchesQueryText(SessionSnapshotModel s, string text) =>
		s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
		|| s.GroupPath.Any(g => g.Contains(text, StringComparison.OrdinalIgnoreCase))
		|| s.NodeId.StartsWith(text, StringComparison.OrdinalIgnoreCase);

	private List<SessionListRowModel> Layout(
		SessionListBuildContext ctx, List<SessionSnapshotModel> filtered, out List<SessionListSectionModel> sections)
	{
		var listed   = new HashSet<string>(filtered.Select(s => s.NodeId), StringComparer.OrdinalIgnoreCase);
		var children = new Dictionary<string, List<SessionSnapshotModel>>(StringComparer.OrdinalIgnoreCase);
		var roots    = new List<SessionSnapshotModel>();

		foreach (var s in filtered)
		{
			if (!IsNestedUnderSupervisor(ctx, s, listed))
			{
				roots.Add(s);
				continue;
			}

			if (!children.TryGetValue(s.SupervisorNodeId!, out var list))
				children[s.SupervisorNodeId!] = list = [];
			list.Add(s);
		}

		var groups = roots
			.GroupBy(r => GetSectionKey(ctx, r), StringComparer.OrdinalIgnoreCase)
			.Select(g => BuildGroup(ctx, g.Key, g.ToList(), children))
			.OrderBy(g => g, Comparer<RowGroup>.Create(CompareGroups))
			.ToList();

		sections = [];
		var rows = new List<SessionListRowModel>();
		foreach (var group in groups)
		{
			var sectionIndex = sections.Count;
			sections.Add(group.Section);
			rows.AddRange(group.Rows.Select(r => r with { SectionIndex = sectionIndex }));
		}

		return rows;
	}

	private RowGroup BuildGroup(
		SessionListBuildContext ctx, string sectionKey, List<SessionSnapshotModel> roots,
		Dictionary<string, List<SessionSnapshotModel>> children)
	{
		var section = BuildSection(ctx, sectionKey)!;
		var rows    = new List<SessionListRowModel>();

		roots.Sort(ctx.CompareNewestFirst);
		foreach (var root in roots)
			AppendRows(ctx, root, 0, section, children, rows);

		return new RowGroup(section, rows);
	}

	private void AppendRows(
		SessionListBuildContext ctx, SessionSnapshotModel s, int indent, SessionListSectionModel section,
		Dictionary<string, List<SessionSnapshotModel>> children, List<SessionListRowModel> rows)
	{
		rows.Add(BuildRow(ctx, s, indent, section));

		if (!children.TryGetValue(s.NodeId, out var kids))
			return;

		kids.Sort(ctx.CompareNewestFirst);
		foreach (var kid in kids)
			AppendRows(ctx, kid, indent + 1, section, children, rows);
	}

	private SessionListRowModel BuildRow(
		SessionListBuildContext ctx, SessionSnapshotModel s, int indent, SessionListSectionModel section)
	{
		var workers    = ctx.Index.DirectWorkers(s.NodeId);
		var supervisor = ctx.Index.FindSupervisor(s);

		// Callers only reach here for rows that are not nested under their supervisor's row when indent == 0.
		var flagSupervisor = indent == 0
			&& supervisor != null
			&& !string.Equals(supervisor.NodeId, ctx.ScopeRoot?.NodeId, StringComparison.OrdinalIgnoreCase);
		var workerOfCaller = flagSupervisor
			&& ctx.Caller != null
			&& string.Equals(supervisor!.NodeId, ctx.Caller.NodeId, StringComparison.OrdinalIgnoreCase);

		return new SessionListRowModel
		{
			NodeId              = s.NodeId,
			ShortId             = ctx.Index.GetShortId(s.NodeId),
			Name                = s.Name,
			LastActivityUtc     = ctx.GetLastActivity(s),
			IsRunning           = s.IsRunning,
			ProfileLabel        = s.ProfileLabel,
			SwitchingToLabel    = s.SwitchingToLabel,
			IsFavorite          = s.IsFavorite,
			WorkerCount         = workers.Count,
			RunningWorkerCount  = workers.Count(w => w.IsRunning),
			IndentLevel         = indent,
			WorkerOfShortId     = flagSupervisor && !workerOfCaller ? ctx.Index.GetShortId(supervisor!.NodeId) : null,
			IsWorkerOfCaller    = workerOfCaller,
			OtherDirectoryLabel = IsSameDirectory(s.DirectoryPath, section.DirectoryPath) ? null : s.DirectoryLabel,
			IsCaller            = ctx.Caller != null && string.Equals(s.NodeId, ctx.Caller.NodeId, StringComparison.OrdinalIgnoreCase),
			SectionIndex        = 0,
		};
	}

	private bool IsNestedUnderSupervisor(SessionListBuildContext ctx, SessionSnapshotModel s, HashSet<string> listed) =>
		!string.IsNullOrEmpty(s.SupervisorNodeId)
		&& listed.Contains(s.SupervisorNodeId)
		&& !ctx.Index.IsInSupervisorCycle(s);

	private string GetSectionKey(SessionListBuildContext ctx, SessionSnapshotModel root) => ctx.Query.Scope switch
	{
		SessionListScope.All     => root.DirectoryPath,
		SessionListScope.Project => ctx.Caller!.DirectoryPath,
		_                        => WorkersSectionKey,
	};

	private SessionListSectionModel? BuildSection(SessionListBuildContext ctx, string sectionKey)
	{
		if (sectionKey != WorkersSectionKey || ctx.Query.Scope is SessionListScope.All or SessionListScope.Project)
			return ctx.Index.GetDirectorySection(sectionKey);

		var root = ctx.ScopeRoot;
		if (root == null)
			return null;

		return new SessionListSectionModel
		{
			Kind           = SessionListSectionKind.Workers,
			DirectoryLabel = root.DirectoryLabel,
			DirectoryPath  = root.DirectoryPath,
			RootShortId    = ctx.Index.GetShortId(root.NodeId),
			RootName       = root.Name,
			DirectCount    = ctx.Index.DirectWorkers(root.NodeId).Count,
			SubtreeCount   = ctx.Index.Descendants(root.NodeId).Count,
		};
	}

	private int CompareGroups(RowGroup a, RowGroup b)
	{
		if (a.Newest.HasValue != b.Newest.HasValue)
			return a.Newest.HasValue ? -1 : 1;

		if (a.Newest.HasValue && a.Newest.Value != b.Newest!.Value)
			return b.Newest.Value.CompareTo(a.Newest.Value);

		var byLabel = StringComparer.OrdinalIgnoreCase.Compare(a.Section.DirectoryLabel, b.Section.DirectoryLabel);
		if (byLabel != 0)
			return byLabel;

		return string.CompareOrdinal(a.Section.DirectoryPath, b.Section.DirectoryPath);
	}

	private PageSlice AssemblePage(
		SessionListBuildContext ctx, List<SessionListRowModel> pageRows,
		List<SessionListSectionModel> allSections, Func<SessionSnapshotModel, bool> isResumable)
	{
		var remap    = new Dictionary<int, int>();
		var sections = new List<SessionListSectionModel>();
		var rows     = new List<SessionListRowModel>();
		var onPage   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var row in pageRows)
		{
			if (!remap.TryGetValue(row.SectionIndex, out var sectionIndex))
			{
				sectionIndex = sections.Count;
				remap[row.SectionIndex] = sectionIndex;
				sections.Add(allSections[row.SectionIndex]);
			}

			var snapshot = ctx.Index.Find(row.NodeId)!;
			rows.Add(FlagOrphanedRow(ctx, row, snapshot, onPage) with
			{
				SectionIndex = sectionIndex,
				IsResumable  = isResumable(snapshot),
			});
			onPage.Add(row.NodeId);
		}

		if (rows.Count == 0)
			sections.AddRange(BuildEmptyPageSections(ctx));

		return new PageSlice(sections, rows);
	}

	/// <summary>
	/// A nested row whose supervisor's row is not on this page (the page starts inside its block) is
	/// flagged <c>worker of</c> like a root row, unless the supervisor is the scope root.
	/// </summary>
	private SessionListRowModel FlagOrphanedRow(
		SessionListBuildContext ctx, SessionListRowModel row, SessionSnapshotModel snapshot, HashSet<string> onPage)
	{
		if (row.IndentLevel == 0)
			return row;

		var supervisor = ctx.Index.FindSupervisor(snapshot);
		if (supervisor == null
			|| onPage.Contains(supervisor.NodeId)
			|| string.Equals(supervisor.NodeId, ctx.ScopeRoot?.NodeId, StringComparison.OrdinalIgnoreCase))
			return row;

		var workerOfCaller = ctx.Caller != null
			&& string.Equals(supervisor.NodeId, ctx.Caller.NodeId, StringComparison.OrdinalIgnoreCase);

		return row with
		{
			IsWorkerOfCaller = workerOfCaller,
			WorkerOfShortId  = workerOfCaller ? null : ctx.Index.GetShortId(supervisor.NodeId),
		};
	}

	private IEnumerable<SessionListSectionModel> BuildEmptyPageSections(SessionListBuildContext ctx)
	{
		SessionListSectionModel? section = ctx.Query.Scope switch
		{
			SessionListScope.Project  => ctx.Caller == null ? null : ctx.Index.GetDirectorySection(ctx.Caller.DirectoryPath),
			SessionListScope.Children => BuildSection(ctx, WorkersSectionKey),
			SessionListScope.Subtree  => BuildSection(ctx, WorkersSectionKey),
			_                         => null,
		};

		return section == null ? [] : [section];
	}

	internal SessionListCallerModel? BuildCaller(SessionListBuildContext ctx)
	{
		var caller = ctx.Caller;
		if (caller == null)
			return null;

		var supervisor = ctx.Index.FindSupervisor(caller);

		return new SessionListCallerModel
		{
			ShortId            = ctx.Index.GetShortId(caller.NodeId),
			Name               = caller.Name,
			DirectoryLabel     = caller.DirectoryLabel,
			OrchestrationDepth = caller.OrchestrationDepth,
			SupervisorShortId  = supervisor == null ? null : ctx.Index.GetShortId(supervisor.NodeId),
			SupervisorName     = supervisor?.Name,
		};
	}

	private string? FindWorkersHint(SessionListBuildContext ctx, IReadOnlyList<SessionListRowModel> pageRows)
	{
		var listed = new HashSet<string>(pageRows.Select(r => r.NodeId), StringComparer.OrdinalIgnoreCase);

		foreach (var row in pageRows)
		{
			var listedWorkers = ctx.Index.DirectWorkers(row.NodeId).Count(w => listed.Contains(w.NodeId));
			if (row.WorkerCount > listedWorkers)
				return row.ShortId;
		}

		return null;
	}

	private bool IsSameDirectory(string a, string b) =>
		string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
