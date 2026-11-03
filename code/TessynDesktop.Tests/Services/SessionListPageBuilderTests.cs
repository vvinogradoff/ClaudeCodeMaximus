using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.15.2, TC.15.3 and the Short Node Id test of TC.15.6 (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListPageBuilderTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

	private readonly List<SessionSnapshotModel> _sessions = new();
	private readonly Dictionary<string, DateTimeOffset?> _activity = new();
	private readonly List<string> _activityLookups = new();
	private readonly List<string> _resumabilityChecks = new();

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>Test NodeIds are the tag padded with '-' to 32 characters; tags never end in '-'.</summary>
	private string Id(string tag) => tag.PadRight(32, '-');

	private string Short(string tag) => Id(tag)[..Constants.Agent.ShortNodeIdMinLength];

	private string TagOf(SessionListRowModel row) => row.NodeId.TrimEnd('-');

	private IReadOnlyList<string> Tags(SessionListPageModel page) => page.Rows.Select(TagOf).ToList();

	private SessionListRowModel RowOf(SessionListPageModel page, string tag) =>
		page.Rows.Single(r => r.NodeId == Id(tag));

	/// <param name="ageMinutes">Minutes since the last write; null = the session file is missing.</param>
	private void Add(
		string tag, string name, string dir = "A", string? sup = null, int? ageMinutes = 10,
		bool running = false, string[]? groups = null)
	{
		_sessions.Add(new SessionSnapshotModel
		{
			NodeId             = Id(tag),
			Name               = name,
			FileName           = tag + ".txt",
			WorkingDirectory   = "C:\\" + dir,
			IsRunning          = running,
			OrchestrationDepth = sup == null ? 0 : 1,
			SupervisorNodeId   = sup == null ? null : Id(sup),
			DirectoryPath      = "C:\\" + dir,
			DirectoryLabel     = dir,
			GroupPath          = groups ?? Array.Empty<string>(),
		});
		_activity[Id(tag)] = ageMinutes == null ? null : Now.AddMinutes(-ageMinutes.Value);
	}

	private SessionListPageModel Build(string caller, SessionListQueryModel? query = null) =>
		new SessionListPageBuilder().Build(
			_sessions,
			Id(caller),
			query ?? new SessionListQueryModel(),
			s =>
			{
				_activityLookups.Add(s.NodeId);
				return _activity.GetValueOrDefault(s.NodeId);
			},
			s =>
			{
				_resumabilityChecks.Add(s.NodeId);
				return true;
			},
			Now);

	// ── TC.15.2 ───────────────────────────────────────────────────────────────

	[Fact]
	public void Project_Default_ListsTopLevelSessionsOfCallersDirectoryOnly()
	{
		Add("a1", "A1", "A", ageMinutes: 1);
		Add("a2", "A2", "A", ageMinutes: 2);
		Add("a3", "A3", "A", ageMinutes: 3);
		Add("b1", "B1", "B", ageMinutes: 4);

		var page = Build("a1");

		Assert.Equal(new[] { "a1", "a2", "a3" }, Tags(page));
		Assert.Equal(3, page.TotalRows);
		var section = Assert.Single(page.Sections);
		Assert.Equal(SessionListSectionKind.Directory, section.Kind);
		Assert.Equal("A", section.DirectoryLabel);
		Assert.Equal(3, section.TopLevelCount);
		Assert.All(page.Rows, r => Assert.Equal(0, r.SectionIndex));
	}

	[Fact]
	public void Project_Default_CallerWorkersNestedUnderCallerRow_OtherWorkersOnlyCounted()
	{
		Add("c", "Sup1", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);
		Add("w2", "W2", sup: "c", ageMinutes: 3);
		Add("o", "Sup2", ageMinutes: 4);
		Add("x1", "X1", sup: "o", ageMinutes: 5, running: true);
		Add("x2", "X2", sup: "o", ageMinutes: 6);

		var page = Build("c");

		Assert.Equal(new[] { "c", "w1", "w2", "o" }, Tags(page));
		Assert.Equal(new[] { 0, 1, 1, 0 }, page.Rows.Select(r => r.IndentLevel));
		Assert.Equal(2, RowOf(page, "o").WorkerCount);
		Assert.Equal(1, RowOf(page, "o").RunningWorkerCount);
		Assert.Equal(2, RowOf(page, "c").WorkerCount);
		Assert.True(RowOf(page, "c").IsCaller);
		Assert.Null(RowOf(page, "w1").WorkerOfShortId);
		Assert.False(RowOf(page, "w1").IsWorkerOfCaller);
		Assert.Equal(Short("o"), page.WorkersHintShortId);
	}

	[Fact]
	public void Project_CallerIsWorker_CallerRowListed()
	{
		Add("p", "Parent", ageMinutes: 5);
		Add("c", "Caller", sup: "p", ageMinutes: 1);
		Add("t", "Other", ageMinutes: 3);

		var page = Build("c");

		Assert.Equal(new[] { "t", "p", "c" }, Tags(page));
		Assert.Equal(1, RowOf(page, "c").IndentLevel);
		Assert.True(RowOf(page, "c").IsCaller);
		Assert.Null(page.WorkersHintShortId);
		Assert.NotNull(page.Caller);
		Assert.Equal(Short("c"), page.Caller!.ShortId);
		Assert.Equal(1, page.Caller.OrchestrationDepth);
		Assert.Equal(Short("p"), page.Caller.SupervisorShortId);
		Assert.Equal("Parent", page.Caller.SupervisorName);
	}

	[Fact]
	public void Project_CallerWorkerInOtherDirectory_NestedWithOtherDirectoryLabel()
	{
		Add("c", "Caller", "A", ageMinutes: 1);
		Add("w", "Remote", "B", sup: "c", ageMinutes: 2);
		Add("t", "Top", "A", ageMinutes: 3);

		var page = Build("c");

		Assert.Equal(new[] { "c", "w", "t" }, Tags(page));
		var worker = RowOf(page, "w");
		Assert.Equal(1, worker.IndentLevel);
		Assert.Equal("B", worker.OtherDirectoryLabel);
		Assert.Equal(RowOf(page, "c").SectionIndex, worker.SectionIndex);
		Assert.Null(RowOf(page, "c").OtherDirectoryLabel);
	}

	[Fact]
	public void Order_NewestLastActivityFirst_TiesByNameThenNodeId_MissingFileLast()
	{
		Add("old", "Old", ageMinutes: 100);
		Add("zed", "Zed", ageMinutes: 5);
		Add("alp", "alpha", ageMinutes: 5);
		Add("s2", "Same", ageMinutes: 50);
		Add("s1", "Same", ageMinutes: 50);
		Add("mis", "Missing", ageMinutes: null);

		var page = Build("old");

		Assert.Equal(new[] { "alp", "zed", "s1", "s2", "old", "mis" }, Tags(page));
		Assert.Null(RowOf(page, "mis").LastActivityUtc);
		Assert.Equal(Now.AddMinutes(-5), RowOf(page, "zed").LastActivityUtc);
	}

	[Fact]
	public void Nesting_SiblingsNewestFirst_IndentPerLevel()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 30);
		Add("w2", "W2", sup: "c", ageMinutes: 10);
		Add("g1", "G1", sup: "w2", ageMinutes: 5);

		var page = Build("c", new SessionListQueryModel { IncludeWorkers = true });

		Assert.Equal(new[] { "c", "w2", "g1", "w1" }, Tags(page));
		Assert.Equal(new[] { 0, 1, 2, 1 }, page.Rows.Select(r => r.IndentLevel));
	}

	[Fact]
	public void All_TopLevelRowsGroupedByDirectory_GroupsOrderedByNewestRow()
	{
		Add("a1", "A1", "A", ageMinutes: 50);
		Add("a2", "A2", "A", ageMinutes: 60);
		Add("bt", "BT", "B", ageMinutes: 5);
		Add("bw", "BW", "B", sup: "bt", ageMinutes: 1);
		Add("c1", "C1", "C", ageMinutes: 30);

		var page = Build("a1", new SessionListQueryModel { Scope = SessionListScope.All });

		Assert.Equal(new[] { "bt", "c1", "a1", "a2" }, Tags(page));
		Assert.Equal(new[] { "B", "C", "A" }, page.Sections.Select(s => s.DirectoryLabel));
		Assert.Equal(new[] { 0, 1, 2, 2 }, page.Rows.Select(r => r.SectionIndex));
		Assert.Equal(1, page.Sections[0].TopLevelCount);
		Assert.Equal(1, page.Sections[0].WorkerCount);
	}

	[Fact]
	public void All_SectionCountsCoverWholeDirectoryBeforeFilters()
	{
		Add("a1", "Alpha", "A", ageMinutes: 1);
		Add("a2", "Beta", "A", ageMinutes: 2);
		Add("aw", "Gamma", "A", sup: "a2", ageMinutes: 3);

		var page = Build("a1", new SessionListQueryModel { Scope = SessionListScope.All, Query = "beta" });

		Assert.Equal(new[] { "a2" }, Tags(page));
		Assert.Equal(2, page.Sections[0].TopLevelCount);
		Assert.Equal(1, page.Sections[0].WorkerCount);
	}

	[Fact]
	public void IncludeWorkers_EveryWorkerListedUnderItsSupervisor()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("p", "Other", ageMinutes: 2);
		Add("w1", "W1", sup: "p", ageMinutes: 3);
		Add("w2", "W2", sup: "p", ageMinutes: 4);

		var page = Build("c", new SessionListQueryModel { IncludeWorkers = true });

		Assert.Equal(new[] { "c", "p", "w1", "w2" }, Tags(page));
		Assert.Equal(new[] { 0, 0, 1, 1 }, page.Rows.Select(r => r.IndentLevel));
		Assert.Null(page.WorkersHintShortId);
	}

	[Fact]
	public void Children_ListsDirectWorkersOfRoot_RootDefaultsToCaller_NoWorkerOfFlag()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);
		Add("w2", "W2", sup: "c", ageMinutes: 3);
		Add("g1", "G1", sup: "w2", ageMinutes: 4);

		var page = Build("c", new SessionListQueryModel { Scope = SessionListScope.Children });

		Assert.Equal(new[] { "w1", "w2" }, Tags(page));
		Assert.All(page.Rows, r => Assert.Equal(0, r.IndentLevel));
		Assert.All(page.Rows, r => Assert.Null(r.WorkerOfShortId));
		Assert.All(page.Rows, r => Assert.False(r.IsWorkerOfCaller));
		Assert.Equal(1, RowOf(page, "w2").WorkerCount);
		var section = Assert.Single(page.Sections);
		Assert.Equal(SessionListSectionKind.Workers, section.Kind);
		Assert.Equal(Short("c"), section.RootShortId);
		Assert.Equal("Caller", section.RootName);
		Assert.Equal(2, section.DirectCount);
		Assert.Equal(3, section.SubtreeCount);
		Assert.Equal(Short("w2"), page.WorkersHintShortId);

		var explicitRoot = Build("c", new SessionListQueryModel { Scope = SessionListScope.Children, RootNodeId = Id("w2") });
		Assert.Equal(new[] { "g1" }, Tags(explicitRoot));
	}

	[Fact]
	public void Subtree_ListsAllDescendants_NestedByLevel()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);
		Add("w2", "W2", sup: "c", ageMinutes: 3);
		Add("g1", "G1", sup: "w2", ageMinutes: 4);
		Add("h1", "H1", sup: "g1", ageMinutes: 5);

		var page = Build("c", new SessionListQueryModel { Scope = SessionListScope.Subtree });

		Assert.Equal(new[] { "w1", "w2", "g1", "h1" }, Tags(page));
		Assert.Equal(new[] { 0, 0, 1, 2 }, page.Rows.Select(r => r.IndentLevel));
		Assert.All(page.Rows, r => Assert.Null(r.WorkerOfShortId));
		Assert.Equal(4, page.Sections[0].SubtreeCount);
	}

	[Fact]
	public void Paging_OffsetAndLimit_CountNestedRows_TotalRowsReported()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);
		Add("w2", "W2", sup: "c", ageMinutes: 3);
		Add("w3", "W3", sup: "c", ageMinutes: 4);
		Add("t1", "T1", ageMinutes: 5);
		Add("t2", "T2", ageMinutes: 6);

		var page = Build("c", new SessionListQueryModel { Offset = 1, Limit = 2 });

		Assert.Equal(new[] { "w1", "w2" }, Tags(page));
		Assert.Equal(6, page.TotalRows);
		Assert.Equal(1, page.Offset);
		Assert.Equal(2, page.Limit);
		Assert.Equal(new[] { Id("w1"), Id("w2") }, _resumabilityChecks);
	}

	[Fact]
	public void Paging_LimitClampedTo1And200_NegativeOffsetCountsAsZero()
	{
		Add("c", "Caller", ageMinutes: 1);
		for (var i = 0; i < 249; i++)
			Add($"t{i:D3}", $"T{i:D3}", ageMinutes: 2 + i);

		var tooBig = Build("c", new SessionListQueryModel { Limit = 1000, Offset = -5 });
		Assert.Equal(200, tooBig.Limit);
		Assert.Equal(200, tooBig.Rows.Count);
		Assert.Equal(250, tooBig.TotalRows);
		Assert.Equal(0, tooBig.Offset);
		Assert.Equal(Id("c"), tooBig.Rows[0].NodeId);

		Assert.Equal(1, Build("c", new SessionListQueryModel { Limit = 0 }).Limit);
		Assert.Single(Build("c", new SessionListQueryModel { Limit = -3 }).Rows);
	}

	[Fact]
	public void Paging_SectionOfFirstRowAlwaysIncluded()
	{
		Add("a1", "A1", "A", ageMinutes: 50);
		Add("a2", "A2", "A", ageMinutes: 60);
		Add("b1", "B1", "B", ageMinutes: 5);
		Add("c1", "C1", "C", ageMinutes: 30);

		var page = Build("a1", new SessionListQueryModel { Scope = SessionListScope.All, Offset = 1, Limit = 1 });

		Assert.Equal(new[] { "c1" }, Tags(page));
		var section = Assert.Single(page.Sections);
		Assert.Equal("C", section.DirectoryLabel);
		Assert.Equal(0, page.Rows[0].SectionIndex);
	}

	[Fact]
	public void Paging_EmptyPage_ReportsScopesOwnSection()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);

		var project  = Build("c", new SessionListQueryModel { Query = "nomatch" });
		var children = Build("c", new SessionListQueryModel { Scope = SessionListScope.Children, Query = "nomatch" });
		var all      = Build("c", new SessionListQueryModel { Scope = SessionListScope.All, Query = "nomatch" });
		var unknown  = Build("c", new SessionListQueryModel { Scope = SessionListScope.Children, RootNodeId = Id("zzz") });

		Assert.Empty(project.Rows);
		Assert.Equal(SessionListSectionKind.Directory, Assert.Single(project.Sections).Kind);
		Assert.Equal(SessionListSectionKind.Workers, Assert.Single(children.Sections).Kind);
		Assert.Empty(all.Sections);
		Assert.Empty(unknown.Rows);
		Assert.Empty(unknown.Sections);
	}

	[Fact]
	public void Paging_PageStartsInsideNestedBlock_OrphanedRowsFlaggedWorkerOf()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);
		Add("g1", "G1", sup: "w1", ageMinutes: 3);
		Add("w2", "W2", sup: "c", ageMinutes: 4);
		Add("g2", "G2", sup: "w2", ageMinutes: 5);

		var all  = Build("c", new SessionListQueryModel { IncludeWorkers = true });
		var page = Build("c", new SessionListQueryModel { IncludeWorkers = true, Offset = 2, Limit = 3 });

		Assert.Equal(new[] { "c", "w1", "g1", "w2", "g2" }, Tags(all));
		Assert.All(all.Rows.Skip(1), r => Assert.Null(r.WorkerOfShortId));
		Assert.Equal(new[] { "g1", "w2", "g2" }, Tags(page));
		Assert.Equal(new[] { 2, 1, 2 }, page.Rows.Select(r => r.IndentLevel));
		Assert.Equal(Short("w1"), RowOf(page, "g1").WorkerOfShortId);
		Assert.True(RowOf(page, "w2").IsWorkerOfCaller);
		Assert.Null(RowOf(page, "w2").WorkerOfShortId);
		Assert.Null(RowOf(page, "g2").WorkerOfShortId);
		Assert.False(RowOf(page, "g2").IsWorkerOfCaller);
	}

	[Fact]
	public void Paging_PageStartsInsideNestedBlock_SupervisorIsScopeRoot_NotFlagged()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("w1", "W1", sup: "c", ageMinutes: 2);
		Add("g1", "G1", sup: "w1", ageMinutes: 3);

		var page = Build("c", new SessionListQueryModel { Scope = SessionListScope.Subtree, Offset = 1 });

		Assert.Equal(new[] { "g1" }, Tags(page));
		Assert.Equal(Short("w1"), RowOf(page, "g1").WorkerOfShortId);

		var children = Build("c", new SessionListQueryModel { Scope = SessionListScope.Children, Offset = 0 });
		Assert.Null(RowOf(children, "w1").WorkerOfShortId);
	}

	[Fact]
	public void WorkersHint_SetWhenAListedRowHasUnlistedWorkers()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("o", "Other", ageMinutes: 2);
		Add("w", "W", sup: "o", ageMinutes: 3);

		Assert.Equal(Short("o"), Build("c").WorkersHintShortId);
		Assert.Null(Build("c", new SessionListQueryModel { IncludeWorkers = true }).WorkersHintShortId);
	}

	// ── TC.15.3 ───────────────────────────────────────────────────────────────

	[Fact]
	public void Query_MatchesNameCaseInsensitively_IncludesWorkers()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("d1", "Deploy tool", ageMinutes: 2);
		Add("o", "Other", ageMinutes: 3);
		Add("d2", "deploy-helper", sup: "o", ageMinutes: 4);
		Add("x", "Unrelated", ageMinutes: 5);

		var page = Build("c", new SessionListQueryModel { Query = "DEPLOY" });

		Assert.Equal(new[] { "d1", "d2" }, Tags(page));
	}

	[Fact]
	public void Query_MatchesGroupNameOnTreePath()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("g", "Plain name", ageMinutes: 2, groups: new[] { "Infra", "Prod" });
		Add("n", "Other name", ageMinutes: 3, groups: new[] { "Dev" });

		var page = Build("c", new SessionListQueryModel { Query = "prod" });

		Assert.Equal(new[] { "g" }, Tags(page));
	}

	[Fact]
	public void Query_MatchesNodeIdPrefix()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("n1", "One", ageMinutes: 2);
		Add("n2", "Two", ageMinutes: 3);

		var page = Build("c", new SessionListQueryModel { Query = Id("n2")[..6].ToUpperInvariant() });

		Assert.Equal(new[] { "n2" }, Tags(page));
	}

	[Fact]
	public void Query_WorkerWithoutListedSupervisor_FlaggedWorkerOf()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("mine", "needle mine", sup: "c", ageMinutes: 2);
		Add("o", "Other", ageMinutes: 3);
		Add("theirs", "needle theirs", sup: "o", ageMinutes: 4);

		var page = Build("c", new SessionListQueryModel { Query = "needle" });

		Assert.Equal(new[] { "mine", "theirs" }, Tags(page));
		var theirs = RowOf(page, "theirs");
		Assert.Equal(Short("o"), theirs.WorkerOfShortId);
		Assert.False(theirs.IsWorkerOfCaller);
		Assert.Equal(0, theirs.IndentLevel);
		var mine = RowOf(page, "mine");
		Assert.True(mine.IsWorkerOfCaller);
		Assert.Null(mine.WorkerOfShortId);
	}

	[Fact]
	public void Query_MatchesSupervisorAndWorker_WorkerNestedUnderSupervisor()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("boss", "needle boss", ageMinutes: 2);
		Add("work", "needle work", sup: "boss", ageMinutes: 3);

		var page = Build("c", new SessionListQueryModel { Query = "needle" });

		Assert.Equal(new[] { "boss", "work" }, Tags(page));
		Assert.Equal(new[] { 0, 1 }, page.Rows.Select(r => r.IndentLevel));
		Assert.Null(RowOf(page, "work").WorkerOfShortId);
		Assert.False(RowOf(page, "work").IsWorkerOfCaller);
	}

	[Fact]
	public void Query_SurroundingWhitespaceIgnored()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("n", "A Needle here", ageMinutes: 2);
		Add("o", "Other", ageMinutes: 3);

		var padded = Build("c", new SessionListQueryModel { Query = "  NEEDLE \t" });
		var blank  = Build("c", new SessionListQueryModel { Query = "   " });

		Assert.Equal(new[] { "n" }, Tags(padded));
		Assert.Equal(new[] { "c", "n", "o" }, Tags(blank));
	}

	[Fact]
	public void Subtree_MiddleNodeFilteredOut_GrandchildFlaggedWorkerOfMiddle()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("m", "Middle", sup: "c", ageMinutes: 2);
		Add("g", "Grand", sup: "m", ageMinutes: 3, running: true);

		var page = Build("c", new SessionListQueryModel { Scope = SessionListScope.Subtree, Running = true });

		Assert.Equal(new[] { "g" }, Tags(page));
		var row = RowOf(page, "g");
		Assert.Equal(Short("m"), row.WorkerOfShortId);
		Assert.False(row.IsWorkerOfCaller);
		Assert.Equal(0, row.IndentLevel);
	}

	[Fact]
	public void Running_TrueListsOnlyRunning_FalseListsOnlyIdle()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("r", "Runner", ageMinutes: 2, running: true);
		Add("i", "Idler", ageMinutes: 3);

		var running = Build("c", new SessionListQueryModel { Running = true });
		var idle    = Build("c", new SessionListQueryModel { Running = false });

		Assert.Equal(new[] { "r" }, Tags(running));
		Assert.Equal(new[] { "c", "i" }, Tags(idle));
	}

	[Fact]
	public void ActiveWithinHours_ExcludesOlderSessionsAndMissingFiles()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("recent", "Recent", ageMinutes: 119);
		Add("old", "Old", ageMinutes: 121);
		Add("mis", "Missing", ageMinutes: null);

		var page = Build("c", new SessionListQueryModel { ActiveWithinHours = 2 });

		Assert.Equal(new[] { "c", "recent" }, Tags(page));
	}

	// ── Laziness ──────────────────────────────────────────────────────────────

	[Fact]
	public void Build_LooksUpLastActivityOnlyForCandidates()
	{
		Add("c", "Caller", "A", ageMinutes: 1);
		Add("t", "Top", "A", ageMinutes: 2);
		Add("o", "Other", "B", ageMinutes: 3);
		Add("w", "Worker", "A", sup: "t", ageMinutes: 4);

		Build("c");

		Assert.DoesNotContain(Id("o"), _activityLookups);
		Assert.DoesNotContain(Id("w"), _activityLookups);
		Assert.Equal(_activityLookups.Distinct().Count(), _activityLookups.Count);
	}

	[Fact]
	public void SupervisorCycle_DoesNotLoopAndRowsStayListed()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("a", "A", sup: "b", ageMinutes: 2);
		Add("b", "B", sup: "a", ageMinutes: 3);

		var page = Build("c", new SessionListQueryModel { IncludeWorkers = true });

		Assert.Equal(new[] { "c", "a", "b" }, Tags(page));
		Assert.All(page.Rows, r => Assert.Equal(0, r.IndentLevel));
	}

	[Fact]
	public void Row_SelfSupervisedSession_NoWorkerOfFlag()
	{
		Add("c", "Caller", ageMinutes: 1);
		Add("s", "Self", sup: "s", ageMinutes: 2);

		var page = Build("c", new SessionListQueryModel { IncludeWorkers = true });

		var row = RowOf(page, "s");
		Assert.Null(row.WorkerOfShortId);
		Assert.False(row.IsWorkerOfCaller);
	}

	// ── TC.15.6 ───────────────────────────────────────────────────────────────

	[Fact]
	public void ShortIds_AtLeast8Characters_ExtendedUntilUnique()
	{
		Add("abcdef12aaaa", "Shared1");
		Add("abcdef12abbb", "Shared2");
		Add("abcdef12bbbb", "Shared3");
		Add("zzzzzzzz1", "Alone");

		var page = Build("zzzzzzzz1", new SessionListQueryModel { Limit = 10 });

		Assert.Equal("zzzzzzzz", page.Caller!.ShortId);
		Assert.Equal("abcdef12aa", RowOf(page, "abcdef12aaaa").ShortId);
		Assert.Equal("abcdef12ab", RowOf(page, "abcdef12abbb").ShortId);
		Assert.Equal("abcdef12b", RowOf(page, "abcdef12bbbb").ShortId);
		Assert.All(page.Rows, r => Assert.True(r.ShortId.Length >= Constants.Agent.ShortNodeIdMinLength));
	}
}
