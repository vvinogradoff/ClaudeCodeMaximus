using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Package J4, the Core import service. The first group is the characterisation of the Avalonia <c>SessionTreeView.ExecuteImport</c> the service was
/// extracted from (each test names the line of the old code it pins); the second group is the path defect and its repair; the rest is the listing,
/// title and run logic behind the remote <c>import.*</c> methods.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportServiceTests : IDisposable
{
	private readonly ImportTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	// ── Characterisation of ExecuteImport ────────────────────────────────────────────

	[Fact]
	public void ImportSession_IntoADirectory_WritesTheSessionFileAndAddsTheNode()
	{
		var directory = _env.AddDirectory("proj");
		var path      = _env.WriteClaudeSession(Constants.ClaudeSessions.BuildProjectSlug(directory.Path), "s1", "Fix the build", messages: 3);

		var outcome = _env.Service.ImportSession(directory, null, _env.SummaryOf(path));

		Assert.Equal(ImportFailureKind.None, outcome.Failure);
		var node = Assert.Single(directory.Sessions);
		Assert.Same(node, outcome.Node);
		Assert.Equal("Fix the build", node.Name);                  // no title: the first prompt (old: TruncateForSessionName)
		Assert.Equal("s1", node.ClaudeSessionId);                  // old: vm.ImportSession(..., item.SessionId)
		Assert.Equal("s1", node.ExternalId);
		Assert.Equal(directory.Path, node.WorkingDirectory);
		Assert.Equal(3, _env.Files.ReadEntries(node.FileName).Count); // old: ParseJsonlSession -> WriteSessionFile
		Assert.Null(node.OriginalProjectPath);                     // same project: no original path
	}

	[Fact]
	public void AnImportedSession_IsAddressableAtOnce_WithANodeIdAndAnAgentToken()
	{
		var directory = _env.AddDirectory("proj");
		var group     = _env.Registry.AddGroup(directory, "G");
		var path      = _env.WriteClaudeSession("any", "s1");
		var summary   = _env.SummaryOf(path);

		var inDirectory = _env.Service.ImportSession(directory, null, summary).Node!;
		var inGroup     = _env.Service.ImportSession(null, group, summary).Node!;

		Assert.Matches("^[0-9a-f]{32}$", inDirectory.NodeId);   // the registry used to leave both empty until the next load
		Assert.Matches("^[0-9a-f]{32}$", inDirectory.AgentToken);
		Assert.NotEqual(inDirectory.NodeId, inGroup.NodeId);
		Assert.Same(inGroup, _env.Registry.FindModelByNodeId(inGroup.NodeId));
	}

	[Fact]
	public void ImportSession_IntoAGroup_UsesTheGroupsWorkingDirectory()
	{
		var directory = _env.AddDirectory("proj");
		var group     = _env.Registry.AddGroup(directory, "G");
		var path      = _env.WriteClaudeSession(Constants.ClaudeSessions.BuildProjectSlug(directory.Path), "s1");

		var outcome = _env.Service.ImportSession(null, group, _env.SummaryOf(path));

		Assert.Equal(ImportFailureKind.None, outcome.Failure);
		var node = Assert.Single(group.Sessions);
		Assert.Equal(group.WorkingDirectory, node.WorkingDirectory);
		Assert.Empty(directory.Sessions);
	}

	[Fact]
	public void TheName_IsTheGeneratedTitle_FirstWhenThereIsOne()
	{
		var directory = _env.AddDirectory("proj");
		var path      = _env.WriteClaudeSession("any", "s1", "the prompt");
		var summary   = _env.SummaryOf(path);
		summary.GeneratedTitle = "A good title";

		_env.Service.ImportSession(directory, null, summary);

		Assert.Equal("A good title", directory.Sessions.Single().Name);
	}

	[Fact]
	public void TheName_IsTheFirstLineOfThePrompt_CutTo57CharactersAndAnEllipsis()
	{
		var directory = _env.AddDirectory("proj");
		var long60    = new string('x', 61);
		var path      = _env.WriteClaudeSession("any", "s1", "  " + long60 + "\nsecond line");

		_env.Service.ImportSession(directory, null, _env.SummaryOf(path));

		Assert.Equal(new string('x', 57) + "...", directory.Sessions.Single().Name);
	}

	[Fact]
	public void TheName_IsImportedSession_WhenThereIsNoTitleAndNoPrompt()
	{
		var directory = _env.AddDirectory("proj");
		var path      = _env.WriteClaudeSession("any", "s1", "  ");
		var summary   = _env.SummaryOf(path);

		_env.Service.ImportSession(directory, null, summary);

		Assert.Equal("Imported Session", directory.Sessions.Single().Name);
	}

	[Fact]
	public void ANameOfTheCaller_WinsOverTheTitle()
	{
		var directory = _env.AddDirectory("proj");
		var summary   = _env.SummaryOf(_env.WriteClaudeSession("any", "s1"));
		summary.GeneratedTitle = "Title";

		_env.Service.ImportSession(directory, null, summary, "  My name ");

		Assert.Equal("My name", directory.Sessions.Single().Name);
	}

	[Fact]
	public void ASessionWithoutMessages_AddsNothing_AndLeavesNoSessionFile()
	{
		var directory = _env.AddDirectory("proj");
		var path      = _env.WriteClaudeSession("any", "s1", messages: 0);

		var outcome = _env.Service.ImportSession(directory, null, _env.SummaryOf(path));

		Assert.Equal(ImportFailureKind.Empty, outcome.Failure);   // old: "if (entries.Count == 0) return;"
		Assert.Empty(directory.Sessions);
		Assert.Empty(_env.SessionFiles());
	}

	[Fact]
	public void ASummaryWithoutAJsonlPath_AddsNothing()
	{
		var directory = _env.AddDirectory("proj");
		var summary   = new ClaudeSessionSummaryModel { SessionId = "s1", JsonlPath = string.Empty };

		var outcome = _env.Service.ImportSession(directory, null, summary);

		Assert.Equal(ImportFailureKind.NoSource, outcome.Failure); // old: "Cannot import without a resolvable JSONL path"
		Assert.Empty(directory.Sessions);
	}

	[Fact]
	public void AnUnreadableFile_IsReportedAndNeverThrown()
	{
		var directory = _env.AddDirectory("proj");
		var summary   = new ClaudeSessionSummaryModel { SessionId = "s1", JsonlPath = Path.Combine(_env.Root, "missing", "s1.jsonl") };

		var outcome = _env.Service.ImportSession(directory, null, summary);   // old: the catch block logged a warning

		Assert.Null(outcome.Node);
		Assert.NotEqual(ImportFailureKind.None, outcome.Failure);
		Assert.Empty(directory.Sessions);
	}

	[Fact]
	public void WithoutATarget_NothingIsCreated()
	{
		var summary = _env.SummaryOf(_env.WriteClaudeSession("any", "s1"));

		var outcome = _env.Service.ImportSession(null, null, summary);

		Assert.Equal(ImportFailureKind.NoTarget, outcome.Failure);
		Assert.Empty(_env.SessionFiles());   // the old code created the file and left it behind
	}

	[Fact]
	public void ACrossProjectSession_RemembersItsOriginalFolder_AndKeepsItAcrossARestart()
	{
		var directory = _env.AddDirectory("proj");
		var other     = @"C:\Projects\Other\my-app";
		var path      = _env.WriteClaudeSession(Constants.ClaudeSessions.BuildProjectSlug(other), "s1", cwd: other);

		_env.Service.ImportSession(directory, null, _env.SummaryOf(path));

		Assert.Equal(other, directory.Sessions.Single().OriginalProjectPath);
		var reloaded = new AppSettingsService(_env.Root);
		reloaded.Load();
		Assert.Equal(other, reloaded.Settings.Tree.Single().Sessions.Single().OriginalProjectPath);
	}

	// ── The path defect (slug.Replace('-', '/')) ─────────────────────────────────────

	/// <summary>The old rule of <c>ExecuteImport</c>, copied as it was, to show what it gave.</summary>
	private static string OldReconstruction(string slug) => slug.StartsWith('-') ? slug.Replace('-', '/') : slug;

	[Fact]
	public void Old_AWindowsSlug_WasStoredAsTheSlugItself()
	{
		Assert.Equal("C--Projects-Other-my-app", OldReconstruction("C--Projects-Other-my-app"));
	}

	[Fact]
	public void Old_ADashInAFolderName_BecameASeparator()
	{
		Assert.Equal("/Users/me/my/app", OldReconstruction("-Users-me-my-app"));
	}

	[Fact]
	public void New_TheFolderRecordedInTheSessionFile_IsTheAnswer()
	{
		var path = _env.WriteClaudeSession("C--Projects-Other-my-app", "s1", cwd: @"C:\Projects\Other\my-app");

		Assert.Equal(@"C:\Projects\Other\my-app", _env.Service.ResolveOriginalProjectPath("C--Projects-Other-my-app", path));
	}

	[Fact]
	public void New_AFolderThatDoesNotFitTheSlug_IsNotTrusted()
	{
		var path = _env.WriteClaudeSession("C--Projects-Other-my-app", "s1", cwd: @"D:\Elsewhere");

		Assert.Equal(@"C:\Projects\Other\my\app", _env.Service.ResolveOriginalProjectPath("C--Projects-Other-my-app", path));
	}

	[Fact]
	public void New_WithoutARecordedFolder_ADirectoryOfTheTreeWithThatSlugIsTheAnswer()
	{
		var directory = _env.AddDirectory("my-app");
		var slug      = Constants.ClaudeSessions.BuildProjectSlug(directory.Path);
		var path      = _env.WriteClaudeSession(slug, "s1");

		Assert.Equal(directory.Path, _env.Service.ResolveOriginalProjectPath(slug, path));
	}

	[Theory]
	[InlineData("C--Projects-Personal-Tessyn", @"C:\Projects\Personal\Tessyn")]
	[InlineData("-Users-me-app", "/Users/me/app")]
	public void New_TheLastResort_ReadsLikeThePath_AndGivesTheSameSlugAgain(string slug, string expected)
	{
		var path = _env.Service.ResolveOriginalProjectPath(slug, null);

		Assert.Equal(expected, path);
		Assert.Equal(slug, Constants.ClaudeSessions.BuildProjectSlug(path));
	}

	[Fact]
	public void TheResolvedPath_IsWhatTheCrossProjectImportStores_ForAWindowsFolder()
	{
		var directory = _env.AddDirectory("proj");
		var slug      = "C--Projects-Other";
		var path      = _env.WriteClaudeSession(slug, "s1");

		_env.Service.ImportSession(directory, null, _env.SummaryOf(path));

		Assert.Equal(@"C:\Projects\Other", directory.Sessions.Single().OriginalProjectPath);   // old: "C--Projects-Other"
	}

	// ── Listing ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task List_OfADirectory_ReturnsItsClaudeSessions_AndMarksTheImportedOnes()
	{
		var directory = _env.AddDirectory("proj");
		var slug      = Constants.ClaudeSessions.BuildProjectSlug(directory.Path);
		_env.WriteClaudeSession(slug, "s1", "Fix the build", messages: 4);
		var second = _env.WriteClaudeSession(slug, "s2", "Add search");
		_env.WriteClaudeSession(slug, "s3", "Empty one", messages: 0);
		_env.Service.ImportSession(directory, null, _env.SummaryOf(second));

		var result = await _env.Service.ListAsync(directory.NodeId, false, null, 50, 0);

		Assert.Equal(2, result.Total);                                  // the empty file is not offered (as in the picker)
		var first = result.Items.Single(i => i.ExternalId == "s1");
		Assert.Equal("Fix the build", first.FirstPrompt);
		Assert.Equal(4, first.EntryCount);
		Assert.False(first.Empty);
		Assert.False(first.AlreadyImported);
		Assert.Equal(slug, first.ProjectSlug);
		Assert.True(result.Items.Single(i => i.ExternalId == "s2").AlreadyImported);
	}

	[Fact]
	public async Task List_WithAQuery_FiltersByTitleAndPrompt_AndPages()
	{
		var directory = _env.AddDirectory("proj");
		var slug      = Constants.ClaudeSessions.BuildProjectSlug(directory.Path);
		for (var i = 0; i < 5; i++)
			_env.WriteClaudeSession(slug, "s" + i, i < 3 ? "needle " + i : "hay " + i);

		var filtered = await _env.Service.ListAsync(directory.NodeId, false, "NEEDLE", 2, 1);

		Assert.Equal(3, filtered.Total);
		Assert.Equal(2, filtered.Items.Count);
		Assert.All(filtered.Items, i => Assert.Contains("needle", i.FirstPrompt));
	}

	[Fact]
	public async Task List_Global_ReturnsTheSessionsOfTheIndexHits_InRankOrder_OneRowPerSession()
	{
		_env.Index.SearchResult =
		[
			Hit("b", rank: -9), Hit("a", rank: -8), Hit("b", rank: -7),
		];
		_env.Index.SummariesResult =
		[
			new ClaudeSessionSummaryModel { SessionId = "a", JsonlPath = "x", MessageCount = 3, ProjectSlug = "slug-a", FirstUserPrompt = "A" },
			new ClaudeSessionSummaryModel { SessionId = "b", JsonlPath = "x", MessageCount = 0, ProjectSlug = "slug-b", FirstUserPrompt = "B" },
		];

		var result = await _env.Service.ListAsync(null, true, "needle", 50, 0);

		Assert.Equal(["b", "a"], result.Items.Select(i => i.ExternalId));
		Assert.True(result.Items[0].Empty);
		Assert.Equal("slug-a", result.Items[1].ProjectSlug);
		Assert.Equal(["b", "a"], _env.Index.LastGetSummariesExternalIds);
		Assert.Equal("needle", _env.Index.LastSearchQuery);
	}

	[Fact]
	public async Task List_Refuses_AMissingSource_AnUnknownDirectory_AndAGlobalListingWithoutAQuery()
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _env.Service.ListAsync(null, false, null, 10, 0));
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _env.Service.ListAsync(null, true, "  ", 10, 0));
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _env.Service.ListAsync(null, true, new string('q', 501), 10, 0));
		var unknown = await Assert.ThrowsAsync<TdcpException>(() => _env.Service.ListAsync("nope", false, null, 10, 0));
		Assert.DoesNotContain(_env.Root, unknown.Message);
	}

	private static SessionSearchResultModel Hit(string id, double rank) =>
		new() { ExternalId = id, ProjectSlug = "p", Content = "c", Role = "user", Rank = rank };

	// ── Titles ───────────────────────────────────────────────────────────────────────

	[Fact]
	public void Titles_AreAnsweredFromTheCache_AndTheMissingOnesAreGeneratedInTheBackground()
	{
		_env.Importer.CacheTitle("cached", "Known title");
		_env.Index.SummariesResult = [new ClaudeSessionSummaryModel { SessionId = "new", JsonlPath = "x", FirstUserPrompt = "hello" }];
		_env.Index.EnsureTitlesResult = new System.Collections.Generic.Dictionary<string, string> { ["new"] = "Fresh title" };

		var first = _env.Service.GetTitles(["cached", "new"]);

		Assert.Equal([new ImportTitleDto("cached", "Known title")], first.Titles);
		Assert.Equal(1, first.Pending);

		var done = SpinUntil(() => _env.Service.GetTitles(["cached", "new"]).Pending == 0);
		Assert.True(done);
		Assert.Contains(_env.Service.GetTitles(["new"]).Titles, t => t.Title == "Fresh title");
		Assert.Equal(["new"], _env.Index.LastEnsureTitlesSummaries!.Select(s => s.SessionId));
	}

	[Fact]
	public void ASessionTheIndexDoesNotKnow_IsNotPendingForever()
	{
		_env.Index.SummariesResult = [];

		_env.Service.GetTitles(["ghost"]);

		Assert.True(SpinUntil(() => _env.Service.GetTitles(["ghost"]).Pending == 0));
	}

	[Fact]
	public void Titles_RefuseABadIdAndTooManyIds()
	{
		Assert.Throws<TdcpInvalidParamsException>(() => _env.Service.GetTitles(["../evil"]));
		Assert.Throws<TdcpInvalidParamsException>(() => _env.Service.GetTitles([.. Enumerable.Range(0, 201).Select(i => "id" + i)]));
	}

	private static bool SpinUntil(Func<bool> condition) => System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5));

	// ── Run ──────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Run_ImportsFromTheIndexAndFromAFileTheIndexDoesNotKnow_AndAnswersEveryItemOnce()
	{
		var directory = _env.AddDirectory("proj");
		var group     = _env.Registry.AddGroup(directory, "G");
		var slug      = Constants.ClaudeSessions.BuildProjectSlug(directory.Path);
		var indexed   = _env.WriteClaudeSession(slug, "indexed", "from the index");
		_env.WriteClaudeSession("other-project", "onlyfile", "found on disk");
		_env.WriteClaudeSession(slug, "taken");
		_env.WriteClaudeSession(slug, "emptyone", messages: 0);
		_env.Index.SummariesResult = [_env.SummaryOf(indexed)];
		_env.Service.ImportSession(directory, null, _env.SummaryOf(Path.Combine(_env.SlugDirectoryOf(directory.Path), "taken.jsonl")));

		var result = await _env.Service.RunAsync(group.NodeId,
		[
			new ImportRequestDto("indexed", "Named"), new ImportRequestDto("onlyfile"), new ImportRequestDto("taken"),
			new ImportRequestDto("ghost"), new ImportRequestDto("emptyone"), new ImportRequestDto("indexed"),
		]);

		Assert.Equal(["indexed", "onlyfile"], result.Imported.Select(i => i.ExternalId));
		Assert.Equal(
			[("taken", "Already imported"), ("ghost", "Session not found"), ("emptyone", "The session has no messages"), ("indexed", "Requested twice")],
			result.Failed.Select(f => (f.ExternalId, f.Reason)));
		Assert.Equal(["Named", "found on disk"], group.Sessions.Select(s => s.Name));
		Assert.Equal(group.Sessions.Select(s => s.NodeId), result.Imported.Select(i => i.NodeId));
		Assert.All(result.Failed, f => Assert.DoesNotContain(_env.Root, f.Reason));
	}

	[Fact]
	public async Task Run_RefusesAnUnknownTarget_ABadId_ALongName_AndTooManyItems()
	{
		var directory = _env.AddDirectory("proj");

		var missing = await Assert.ThrowsAsync<TdcpException>(() => _env.Service.RunAsync("nope", [new ImportRequestDto("s1")]));
		Assert.DoesNotContain(_env.Root, missing.Message);
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _env.Service.RunAsync(directory.NodeId, [new ImportRequestDto("a/b")]));
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _env.Service.RunAsync(directory.NodeId, [new ImportRequestDto("s1", new string('n', 201))]));
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _env.Service.RunAsync(directory.NodeId, [.. Enumerable.Range(0, 101).Select(i => new ImportRequestDto("s" + i))]));
		Assert.Empty(directory.Sessions);
	}
}
