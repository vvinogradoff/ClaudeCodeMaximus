using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// Turns a snapshot of the session tree into one <see cref="SessionTagSearchPageModel"/> (FR.24.15): the
/// Session Tag Index of the caller's scope, FR.24.11 matching and order (<see cref="SessionTagSearch"/>), paging,
/// and the Short Node Ids of each row's newest carriers. Pure — the tag index and the file-system work are
/// injected as delegates; last-write times are read only for carriers (once each).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagSearchPageBuilder
{
	public SessionTagSearchPageModel Build(
		IReadOnlyList<SessionSnapshotModel> sessions,
		string callerNodeId,
		SessionTagSearchQueryModel query,
		Func<IReadOnlyList<SessionTagIndexSessionModel>, string?, IReadOnlyList<SessionTagUsageModel>> buildIndex,
		Func<SessionSnapshotModel, DateTimeOffset?> getLastActivityUtc)
	{
		var text      = string.IsNullOrWhiteSpace(query.Query) ? null : query.Query.Trim();
		var limit     = Math.Clamp(query.Limit, 1, Constants.Agent.ListSessionsMaxLimit);
		var offset    = Math.Max(query.Offset, 0);
		var isProject = query.Scope == SessionListScope.Project;

		var index   = new SessionListIndex(sessions);
		var caller  = index.Find(callerNodeId);
		var context = new SessionListBuildContext(index, caller, null, new SessionListQueryModel(), getLastActivityUtc, DateTimeOffset.UtcNow);

		IReadOnlyList<SessionTagUsageModel> usages = [];
		if (!isProject || caller != null)
		{
			var snapshot = sessions
				.Select(s => new SessionTagIndexSessionModel(s.NodeId, s.Name, s.SupervisorNodeId, s.DirectoryPath))
				.ToList();
			usages = buildIndex(snapshot, isProject ? caller!.DirectoryPath : null);
		}

		var matching = usages.Where(u => SessionTagSearch.Matches(u.Tag, text)).ToList();
		var ordered  = SessionTagSearch.Order(matching, id => LastActivityOf(context, id));
		var rows     = ordered.Skip(offset).Take(limit).Select(u => BuildRow(context, u)).ToList();

		return new SessionTagSearchPageModel
		{
			Scope          = query.Scope,
			Caller         = new SessionListPageBuilder().BuildCaller(context),
			DirectoryLabel = isProject ? caller?.DirectoryLabel : null,
			DirectoryPath  = isProject ? caller?.DirectoryPath : null,
			Query          = text,
			Rows           = rows,
			TotalRows      = matching.Count,
			TagsInScope    = usages.Count,
			Offset         = offset,
			Limit          = limit,
		};
	}

	private DateTimeOffset? LastActivityOf(SessionListBuildContext context, string nodeId)
	{
		var session = context.Index.Find(nodeId);
		return session == null ? null : context.GetLastActivity(session);
	}

	private SessionTagSearchRowModel BuildRow(SessionListBuildContext context, SessionTagUsageModel usage)
	{
		var carriers = usage.NodeIds
			.Select(context.Index.Find)
			.OfType<SessionSnapshotModel>()
			.ToList();
		carriers.Sort(context.CompareNewestFirst);

		return new SessionTagSearchRowModel
		{
			Label           = usage.Tag.Label,
			Url             = usage.Tag.Url,
			SessionCount    = usage.NodeIds.Count,
			CarrierShortIds = carriers
				.Take(Constants.SessionTags.SearchMaxCarriersPerRow)
				.Select(c => context.Index.GetShortId(c.NodeId))
				.ToList(),
		};
	}
}
