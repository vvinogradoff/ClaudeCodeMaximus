using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary><c>tree.snapshot</c>, <c>tree.workers</c>, <c>tree.search</c> and <c>tree.createSession</c> (contract 3.2–3.5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeRemoteRpcModuleTests : IDisposable
{
	private readonly RemoteTreeTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string[] Names(JsonNode? array) => array!.AsArray().Select(n => n!["name"]!.GetValue<string>()).ToArray();

	// ── tree.snapshot ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task Snapshot_ListsDirectoriesGroupsAndTopLevelSessions_InModelOrder_WithLabelsAndIds()
	{
		var dir = _env.AddDirectory("proj");
		var group = _env.Registry.AddGroup(dir, "G");
		var nested = _env.Registry.AddGroupToGroup(group, "Nested");
		_env.AddSession(dir, "Direct1");
		_env.AddSession(dir, "Direct2");
		_env.AddSession(dir, "InGroup", group);
		_env.AddSession(dir, "InNested", nested);

		var result = await _env.CallAsync("tree.snapshot");

		var d = result["directories"]!.AsArray().Single()!;
		Assert.Equal(dir.NodeId, d["id"]!.GetValue<string>());
		Assert.Equal(dir.Path, d["label"]!.GetValue<string>());   // FakeDirectoryLabelService echoes the path; the real one is covered by DirectoryLabelServiceTests
		Assert.Equal(["Direct1", "Direct2"], Names(d["sessions"]));
		var g = d["groups"]!.AsArray().Single()!;
		Assert.Equal(group.NodeId, g["id"]!.GetValue<string>());
		Assert.Equal("G", g["name"]!.GetValue<string>());
		Assert.Equal(["InGroup"], Names(g["sessions"]));
		Assert.Equal(["InNested"], Names(g["groups"]!.AsArray().Single()!["sessions"]));
	}

	[Fact]
	public async Task Snapshot_ExcludesWorkers_CountsThemOnTheSupervisor_AndKeepsOrphansTopLevel()
	{
		var dir = _env.AddDirectory("proj");
		var sup = _env.AddSession(dir, "Sup");
		_env.AddSession(dir, "W1", supervisorNodeId: sup.NodeId);
		_env.AddSession(dir, "W2", supervisorNodeId: sup.NodeId);
		_env.AddSession(dir, "Orphan", supervisorNodeId: "no-such-session");

		var result = await _env.CallAsync("tree.snapshot");

		var sessions = result["directories"]![0]!["sessions"]!.AsArray();
		Assert.Equal(["Sup", "Orphan"], Names(sessions));
		Assert.Equal(2, sessions[0]!["workerCount"]!.GetValue<int>());
		Assert.Equal(0, sessions[1]!["workerCount"]!.GetValue<int>());
		Assert.Null(sessions[0]!["supervisorId"]);
	}

	[Fact]
	public async Task Snapshot_RowCarriesLiveState()
	{
		var dir = _env.AddDirectory("proj");
		var s = _env.AddSession(dir, "S");
		s.IsFavorite = true;
		_env.Registry.SetSessionRunning(s.NodeId, true);
		_env.Activity.Set(s.NodeId, new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero), hasDraft: true);
		_env.Attention.SummaryByNode[s.NodeId] = new AttentionSummaryModel { AllOpenCount = 2, AllOpenTopKind = AttentionKind.Ask };

		var result = await _env.CallAsync("tree.snapshot");

		var state = result["directories"]![0]!["sessions"]![0]!["state"]!;
		Assert.Equal(s.NodeId, state["id"]!.GetValue<string>());
		Assert.True(state["running"]!.GetValue<bool>());
		Assert.True(state["favorite"]!.GetValue<bool>());
		Assert.True(state["hasDraft"]!.GetValue<bool>());
		Assert.Equal(2, state["attention"]!["count"]!.GetValue<int>());
		Assert.Equal("ask", state["attention"]!["topKind"]!.GetValue<string>());
		Assert.NotNull(state["lastActivityUtc"]);
	}

	[Fact]
	public async Task Snapshot_NeverLeaksSecretsPathsOrFileNames()
	{
		var dir = _env.AddDirectory("proj");
		var s = _env.AddSession(dir, "S");
		s.ClaudeSessionId = "claude-session-secret";

		var raw = (await _env.CallAsync("tree.snapshot")).ToJsonString();

		Assert.DoesNotContain(s.AgentToken, raw);
		Assert.DoesNotContain(s.FileName, raw);
		Assert.DoesNotContain("claude-session-secret", raw);
		Assert.DoesNotContain(s.WorkingDirectory, raw);
		Assert.DoesNotContain("agentToken", raw, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Snapshot_ReportsTheTrackerVersion_AndOmitsUnchangedWhenNotAsked()
	{
		_env.AddDirectory("proj");
		_env.Version.Increment();
		_env.Version.Increment();

		var result = await _env.CallAsync("tree.snapshot");

		Assert.Equal(2, result["version"]!.GetValue<long>());
		Assert.Null(result["unchanged"]);
		Assert.NotNull(result["directories"]);
	}

	[Fact]
	public async Task Snapshot_SinceCurrentVersion_AnswersUnchangedWithoutTheTree()
	{
		_env.AddDirectory("proj");
		_env.Version.Increment();

		var result = await _env.CallAsync("tree.snapshot", """{"sinceVersion":1}""");

		Assert.Equal(1, result["version"]!.GetValue<long>());
		Assert.True(result["unchanged"]!.GetValue<bool>());
		Assert.Null(result["directories"]);
	}

	[Fact]
	public async Task Snapshot_SinceAnOlderVersion_ReturnsTheTree()
	{
		_env.AddDirectory("proj");
		_env.Version.Increment();
		_env.Version.Increment();

		var result = await _env.CallAsync("tree.snapshot", """{"sinceVersion":1}""");

		Assert.Null(result["unchanged"]);
		Assert.NotNull(result["directories"]);
	}

	[Fact]
	public async Task Snapshot_WithAWrongTypedSinceVersion_IsInvalidParams()
	{
		var response = await _env.CallRawAsync("tree.snapshot", """{"sinceVersion":"x"}""");

		Assert.Equal(-32602, RemoteTreeTestEnv.ErrorCode(response));
	}

	// ── tree.workers ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task Workers_ReturnsDirectWorkersInModelOrder_EachWithItsSupervisorId()
	{
		var dir = _env.AddDirectory("proj");
		var group = _env.Registry.AddGroup(dir, "G");
		var sup = _env.AddSession(dir, "Sup");
		var w1 = _env.AddSession(dir, "W1", supervisorNodeId: sup.NodeId);
		_env.AddSession(dir, "Other");
		_env.AddSession(dir, "W2", group, supervisorNodeId: sup.NodeId);
		_env.AddSession(dir, "Grandchild", supervisorNodeId: w1.NodeId);

		var result = await _env.CallAsync("tree.workers", $$"""{"supervisorId":"{{sup.NodeId}}"}""");

		Assert.Equal(sup.NodeId, result["supervisorId"]!.GetValue<string>());
		var workers = result["workers"]!.AsArray();
		Assert.Equal(["W2", "W1"], Names(workers));   // groups are listed before sessions, the desktop's display order
		Assert.All(workers, w => Assert.Equal(sup.NodeId, w!["supervisorId"]!.GetValue<string>()));
		Assert.Equal(1, workers.Single(w => w!["name"]!.GetValue<string>() == "W1")!["workerCount"]!.GetValue<int>());
	}

	[Fact]
	public async Task Workers_OfASessionWithoutWorkers_IsEmpty()
	{
		var dir = _env.AddDirectory("proj");
		var s = _env.AddSession(dir, "S");

		var result = await _env.CallAsync("tree.workers", $$"""{"supervisorId":"{{s.NodeId}}"}""");

		Assert.Empty(result["workers"]!.AsArray());
	}

	[Fact]
	public async Task Workers_UnknownSupervisor_IsSessionNotFound_AndMissingParamIsInvalid()
	{
		var unknown = await _env.CallRawAsync("tree.workers", """{"supervisorId":"nope"}""");
		var missing = await _env.CallRawAsync("tree.workers");

		Assert.Equal(-32000, RemoteTreeTestEnv.ErrorCode(unknown));
		Assert.Equal("Session not found", unknown["error"]!["message"]!.GetValue<string>());
		Assert.Equal(-32602, RemoteTreeTestEnv.ErrorCode(missing));
	}

	// ── tree.search ───────────────────────────────────────────────────────────────

	private SessionNodeModel Searchable(DirectoryNodeModel dir, string name, string external, string? supervisor = null) =>
		_env.AddSession(dir, name, supervisorNodeId: supervisor, externalId: external);

	private static SessionSearchScoreModel Score(string external, int strict, int loose) =>
		new() { ExternalId = external, StrictMatchCount = strict, LooseMatchCount = loose };

	[Fact]
	public async Task Search_BlankQuery_ReturnsNoMatchesWithoutTouchingTheIndex()
	{
		var response = await _env.CallAsync("tree.search", """{"query":"   "}""");

		Assert.Equal("", response["query"]!.GetValue<string>());
		Assert.Empty(response["matches"]!.AsArray());
		Assert.False(response["effectiveExact"]!.GetValue<bool>());
		Assert.Null(_env.Index.LastScoresQuery);
	}

	[Fact]
	public async Task Search_TrimsTheQuery_AndRanksMatchesBestFirst()
	{
		var dir = _env.AddDirectory("proj");
		var a = Searchable(dir, "A", "ext-a");
		var b = Searchable(dir, "B", "ext-b");
		Searchable(dir, "C", "ext-c");
		_env.Index.ScoresResult = [Score("ext-a", 0, 5), Score("ext-b", 2, 2)];

		var result = await _env.CallAsync("tree.search", """{"query":"  visual studio code  "}""");

		Assert.Equal("visual studio code", _env.Index.LastScoresQuery);
		Assert.Equal("visual studio code", result["query"]!.GetValue<string>());
		var matches = result["matches"]!.AsArray();
		Assert.Equal([b.NodeId, a.NodeId], matches.Select(m => m!["id"]!.GetValue<string>()));
		Assert.Equal(2, matches[0]!["strict"]!.GetValue<int>());
		Assert.Equal(5, matches[1]!["loose"]!.GetValue<int>());
		Assert.True(matches[0]!["matchedSelf"]!.GetValue<bool>());
		Assert.False(result["usedFallback"]!.GetValue<bool>());
	}

	[Theory]
	[InlineData("short", true)]                       // fewer than 8 characters
	[InlineData("exactly8", false)]
	[InlineData("a longer query", false)]
	[InlineData("https://x.example/a/long/path", true)]
	[InlineData("HTTPS://X.EXAMPLE/A/LONG/PATH", true)]
	public async Task Search_AutoExactRule_AppliesWhenTheClientSaysNothing(string query, bool expectedAuto)
	{
		var result = await _env.CallAsync("tree.search", $$"""{"query":"{{query}}"}""");

		Assert.Equal(expectedAuto, result["autoExact"]!.GetValue<bool>());
		Assert.Equal(expectedAuto, result["effectiveExact"]!.GetValue<bool>());
	}

	[Fact]
	public async Task Search_ExplicitExactFalse_OverridesTheAutoRule_AndNullMeansAuto()
	{
		var off = await _env.CallAsync("tree.search", """{"query":"short","exact":false}""");
		var nul = await _env.CallAsync("tree.search", """{"query":"short","exact":null}""");
		var on = await _env.CallAsync("tree.search", """{"query":"a long enough query","exact":true}""");

		Assert.True(off["autoExact"]!.GetValue<bool>());
		Assert.False(off["effectiveExact"]!.GetValue<bool>());
		Assert.True(nul["effectiveExact"]!.GetValue<bool>());
		Assert.False(on["autoExact"]!.GetValue<bool>());
		Assert.True(on["effectiveExact"]!.GetValue<bool>());
	}

	[Fact]
	public async Task Search_Exact_KeepsOnlyStrictMatches()
	{
		var dir = _env.AddDirectory("proj");
		Searchable(dir, "LooseOnly", "ext-loose");
		var strict = Searchable(dir, "Strict", "ext-strict");
		_env.Index.ScoresResult = [Score("ext-loose", 0, 9), Score("ext-strict", 1, 1)];

		var result = await _env.CallAsync("tree.search", """{"query":"a long enough query","exact":true}""");

		var match = Assert.Single(result["matches"]!.AsArray())!;
		Assert.Equal(strict.NodeId, match["id"]!.GetValue<string>());
	}

	[Fact]
	public async Task Search_Loose_KeepsLooseOnlyMatches()
	{
		var dir = _env.AddDirectory("proj");
		var s = Searchable(dir, "LooseOnly", "ext-loose");
		_env.Index.ScoresResult = [Score("ext-loose", 0, 9)];

		var result = await _env.CallAsync("tree.search", """{"query":"a long enough query","exact":false}""");

		Assert.Equal(s.NodeId, Assert.Single(result["matches"]!.AsArray())!["id"]!.GetValue<string>());
	}

	[Fact]
	public async Task Search_MatchesBySessionKey_ExternalIdThenClaudeSessionId_AndNeverRunSessionsNeverMatch()
	{
		var dir = _env.AddDirectory("proj");
		var viaExternal = Searchable(dir, "ViaExternal", "ext-1");
		var viaClaude = _env.AddSession(dir, "ViaClaude");
		viaClaude.ClaudeSessionId = "claude-1";
		_env.AddSession(dir, "NeverRan");
		_env.Index.ScoresResult = [Score("ext-1", 1, 1), Score("claude-1", 1, 1), Score("", 1, 1)];

		var result = await _env.CallAsync("tree.search", """{"query":"a long enough query"}""");

		var ids = result["matches"]!.AsArray().Select(m => m!["id"]!.GetValue<string>()).ToArray();
		Assert.Equal(2, ids.Length);
		Assert.Contains(viaExternal.NodeId, ids);
		Assert.Contains(viaClaude.NodeId, ids);
	}

	[Fact]
	public async Task Search_WorkerMatch_RollsUpOntoTheTopLevelSession()
	{
		var dir = _env.AddDirectory("proj");
		var sup = Searchable(dir, "Sup", "ext-sup");
		var worker = Searchable(dir, "Worker", "ext-worker", sup.NodeId);
		_env.Index.ScoresResult = [Score("ext-worker", 1, 2)];

		var result = await _env.CallAsync("tree.search", """{"query":"a long enough query"}""");

		var match = Assert.Single(result["matches"]!.AsArray())!;
		Assert.Equal(sup.NodeId, match["id"]!.GetValue<string>());
		Assert.False(match["matchedSelf"]!.GetValue<bool>());
		Assert.Equal([worker.NodeId], match["workerIds"]!.AsArray().Select(w => w!.GetValue<string>()));
	}

	[Fact]
	public async Task Search_EqualCounts_NewestLastPromptWins()
	{
		var dir = _env.AddDirectory("proj");
		var older = Searchable(dir, "Older", "ext-older");
		var newer = Searchable(dir, "Newer", "ext-newer");
		_env.Activity.Set(older.NodeId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		_env.Activity.Set(newer.NodeId, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
		_env.Index.ScoresResult = [Score("ext-older", 1, 1), Score("ext-newer", 1, 1)];

		var result = await _env.CallAsync("tree.search", """{"query":"a long enough query"}""");

		Assert.Equal([newer.NodeId, older.NodeId], result["matches"]!.AsArray().Select(m => m!["id"]!.GetValue<string>()));
	}

	[Fact]
	public async Task Search_ScopeNodeId_BecomesTheProjectSlugOfThatSessionsWorkingDirectory()
	{
		var dir = _env.AddDirectory("proj");
		var scope = Searchable(dir, "Scope", "ext-scope");

		await _env.CallAsync("tree.search", $$"""{"query":"a long enough query","scopeNodeId":"{{scope.NodeId}}"}""");

		Assert.Equal(Constants.ClaudeSessions.BuildProjectSlug(scope.WorkingDirectory), _env.Index.LastScoresProjectSlug);
	}

	[Fact]
	public async Task Search_WithoutScope_SearchesEveryProject()
	{
		await _env.CallAsync("tree.search", """{"query":"a long enough query","scopeNodeId":null}""");

		Assert.Null(_env.Index.LastScoresProjectSlug);
	}

	[Fact]
	public async Task Search_UnknownScope_IsSessionNotFound()
	{
		var response = await _env.CallRawAsync("tree.search", """{"query":"a long enough query","scopeNodeId":"nope"}""");

		Assert.Equal(-32000, RemoteTreeTestEnv.ErrorCode(response));
	}

	[Fact]
	public async Task Search_IndexFailure_FallsBackToTheFileScan_AsLooseMatches()
	{
		var dir = _env.AddDirectory("proj");
		var hit = Searchable(dir, "Hit", "ext-hit");
		Searchable(dir, "Miss", "ext-miss");
		_env.Index.ScoresException = new InvalidOperationException("index locked");
		_env.FileSearch.Matches.Add(hit.FileName);

		var result = await _env.CallAsync("tree.search", """{"query":"a long enough query"}""");

		Assert.True(result["usedFallback"]!.GetValue<bool>());
		var match = Assert.Single(result["matches"]!.AsArray())!;
		Assert.Equal(hit.NodeId, match["id"]!.GetValue<string>());
		Assert.Equal(0, match["strict"]!.GetValue<int>());
		Assert.Equal(1, match["loose"]!.GetValue<int>());
	}

	[Fact]
	public async Task Search_BothIndexAndFallbackFailing_IsADaemonError()
	{
		_env.Index.ScoresException = new InvalidOperationException("index locked");
		_env.FileSearch.Failure = new InvalidOperationException("disk");

		var response = await _env.CallRawAsync("tree.search", """{"query":"a long enough query"}""");

		Assert.Equal(-32000, RemoteTreeTestEnv.ErrorCode(response));
	}

	[Fact]
	public async Task Search_MissingQuery_IsInvalidParams()
	{
		var response = await _env.CallRawAsync("tree.search", "{}");

		Assert.Equal(-32602, RemoteTreeTestEnv.ErrorCode(response));
	}

	// ── tree.createSession ────────────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_AfterASession_ReturnsItsDtoAndPlacesItRightAfterTheAnchor()
	{
		var dir = _env.AddDirectory("proj");
		var group = _env.Registry.AddGroup(dir, "G");
		var a = _env.AddSession(dir, "A", group);
		_env.AddSession(dir, "B", group);

		var result = await _env.CallAsync("tree.createSession", $$"""{"anchorId":"{{a.NodeId}}","name":"  Fresh  "}""");

		Assert.Equal(["A", "Fresh", "B"], group.Sessions.Select(s => s.Name));
		var session = result["session"]!;
		Assert.Equal("Fresh", session["name"]!.GetValue<string>());
		Assert.Equal(group.Sessions[1].NodeId, session["id"]!.GetValue<string>());
		Assert.Equal(0, session["workerCount"]!.GetValue<int>());
		Assert.Equal(session["id"]!.GetValue<string>(), session["state"]!["id"]!.GetValue<string>());
		Assert.Equal(dir.NodeId, result["directoryId"]!.GetValue<string>());
		Assert.Equal(group.NodeId, result["groupId"]!.GetValue<string>());
	}

	[Fact]
	public async Task CreateSession_UnderADirectory_OmitsGroupId_AndGoesFirst()
	{
		var dir = _env.AddDirectory("proj");
		_env.AddSession(dir, "A");

		var result = await _env.CallAsync("tree.createSession", $$"""{"anchorId":"{{dir.NodeId}}","name":"First"}""");

		Assert.Equal(["First", "A"], dir.Sessions.Select(s => s.Name));
		Assert.Null(result["groupId"]);
	}

	[Fact]
	public async Task CreateSession_RaisesSessionAdded_SoTheDesktopTreeAndTheVersionFollow()
	{
		var dir = _env.AddDirectory("proj");
		TreeChangedEventArgs? added = null;
		_env.Registry.TreeChanged += (_, e) => { if (e.Kind == TreeChangeKind.SessionAdded) added = e; };

		await _env.CallAsync("tree.createSession", $$"""{"anchorId":"{{dir.NodeId}}","name":"N"}""");

		Assert.NotNull(added);
		Assert.Equal(0, added!.Index);
	}

	[Theory]
	[InlineData("""{"anchorId":"x","name":"   "}""")]
	[InlineData("""{"anchorId":"x","name":""}""")]
	[InlineData("""{"anchorId":"x"}""")]
	[InlineData("""{"name":"N"}""")]
	[InlineData("""{"anchorId":"x","name":5}""")]
	public async Task CreateSession_InvalidParams_AreRejectedBeforeAnythingIsCreated(string paramsJson)
	{
		var dir = _env.AddDirectory("proj");

		var response = await _env.CallRawAsync("tree.createSession", paramsJson);

		Assert.Equal(-32602, RemoteTreeTestEnv.ErrorCode(response));
		Assert.Empty(dir.Sessions);
	}

	[Fact]
	public async Task CreateSession_UnknownAnchor_IsADaemonError()
	{
		var response = await _env.CallRawAsync("tree.createSession", """{"anchorId":"nope","name":"N"}""");

		Assert.Equal(-32000, RemoteTreeTestEnv.ErrorCode(response));
	}

	// ── remote-callable registration ──────────────────────────────────────────────

	[Theory]
	[InlineData("tree.snapshot")]
	[InlineData("tree.workers")]
	[InlineData("tree.search")]
	[InlineData("tree.createSession")]
	[InlineData("recent.list")]
	[InlineData("recent.hide")]
	[InlineData("tags.search")]
	public void EveryB1Method_IsRemoteCallable(string method) => Assert.True(_env.Dispatcher.IsRemoteCallable(method));
}
