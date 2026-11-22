using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Records every call of the turn paths into the switch service and whether the node's turn lock was held at that moment.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class LockObservingSwitchService(Func<bool> lockHeld) : IProfileSwitchService
{
	public List<string> Calls { get; } = [];

	public ProfileSwitchResultDto Switch(string nodeId, string profileId, bool includeDescendants) => throw new NotSupportedException();
	public IReadOnlyList<SessionNodeModel> GetDescendants(string nodeId) => [];
	public bool IsDescendantOf(string nodeId, string ancestorNodeId) => false;

	public bool ApplyPending(SessionNodeModel node)
	{
		Calls.Add("ApplyPending lockHeld=" + lockHeld());
		return true;
	}

	public void RecordTurnProfile(SessionNodeModel node, string? displayName) => Calls.Add("RecordTurnProfile lockHeld=" + lockHeld());
}

/// <summary>
/// The review of ISSUE.013 (313-review-issue013): P1 turn lock, P4 failed moves, P5 serialisation with turn start, P6 stale choice, P7 both
/// copies, P8 migrate on a running target, P10 pin migration robustness, P11 label cap and the extra cycle / depth / forged-id tests.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class Issue013ReviewFixTests : IDisposable
{
	private readonly ProfileSwitchEnv _env = new();
	private readonly string _p1;
	private readonly string _p2;

	public Issue013ReviewFixTests()
	{
		_p1 = _env.AddProfile("p1", "Account A");
		_p2 = _env.AddProfile("p2", "Account B");
	}

	public void Dispose() => _env.Dispose();

	private Task<JsonNode> Send(SessionNodeModel target, SessionNodeModel caller, bool migrate = false) =>
		_env.Call("send_to_session", new() { ["nodeId"] = target.NodeId, ["prompt"] = "go", ["migrate"] = migrate }, caller);

	// ── P1: the engine applies a queued switch only under the turn lock ──────────────────────

	[Fact]
	public async Task TurnEngine_AppliesAQueuedSwitchUnderTheTurnLock_BeforeRecordingTheTurnProfile_AndAdoptsOnlyAfterThat()
	{
		using var h = new TurnEngineHarness();
		var observer = new LockObservingSwitchService(() => h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount == 0);
		var engine = new TurnEngine(
			h.Files, h.Registry, h.Turns, TestEngines.Claude(h.Streaming, h.Process, h.Files, h.Settings, h.Status, new Lazy<IAgentMcpServer>(() => h.Mcp)), h.Settings, new Lazy<ITelegramBridgeService>(() => h.Telegram), h.Attention.Service, h.Tags.Service, null, observer);
		h.Process.ScriptEvents(new ClaudeStreamEvent { Type = "result", SessionId = "s1" });
		var node = TurnEngineHarness.Node(sessionId: "s1");
		node.PendingProfileId = "p2";   // a switch is queued for this session

		await engine.RunAsync(new TurnRequest(node, "hi", TurnEngineHarness.Chosen(), false));

		Assert.Equal(["ApplyPending lockHeld=True", "RecordTurnProfile lockHeld=True"], observer.Calls);
		Assert.Single(h.Status.Adoptions);   // the adoption runs once, after both calls above and under the same lock (N1)
	}

	// ── P2/P3 on send_to_session ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task SendToSession_LegacyRecordedProfileThatNoLongerExists_RunsOnDefault_AndTheTextSaysSo()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator);
		child.ProfileName = "Retired account";   // a name only, no ProfileId pin

		var text = ProfileSwitchEnv.TextOf(await Send(child, orchestrator));

		Assert.Equal("ok\n[profile=default; the recorded profile 'Retired account' no longer exists]", text);
		Assert.Equal([child.NodeId], _env.Turns.RunNodeIds);
	}

	// ── P4: a failed move is reported, not pinned, and retried ───────────────────────────────

	[Fact]
	public void LockedTranscript_IsReportedAsFailed_NotPinned_PendingKept_AndRetriedAtTheNextTurn()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);
		ProfileSwitchResultDto result;
		using (new FileStream(_env.TranscriptIn(_p1, o), FileMode.Open, FileAccess.Read, FileShare.None))   // held open, e.g. by a CLI
			result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.False(result.NodeMoved);
		var failure = Assert.Single(result.Failed);
		Assert.Equal(o.NodeId, failure.NodeId);
		Assert.Contains(_env.TranscriptIn(_p1, o), failure.Paths);
		Assert.Equal("p1", o.ProfileId);                    // not pinned: the transcript did not move
		Assert.Equal("p2", o.PendingProfileId);             // queued for the retry
		Assert.True(File.Exists(_env.TranscriptIn(_p1, o)));

		Assert.True(_env.Switch.ApplyPending(o));            // the file is free now
		Assert.Equal("p2", o.ProfileId);
		Assert.Null(o.PendingProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
	}

	[Fact]
	public void LockedFolderFile_DoesNotStopTheOthers_TranscriptMoved_PinnedAndRetryKept()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o, toolResultName: "a-locked.txt");
		File.WriteAllText(Path.Combine(_env.FolderIn(_p1, o), "b-free.txt"), "free");
		Directory.CreateDirectory(Path.Combine(_env.FolderIn(_p1, o), "sub"));
		File.WriteAllText(Path.Combine(_env.FolderIn(_p1, o), "sub", "c-free.txt"), "free");
		ProfileSwitchResultDto result;
		using (new FileStream(Path.Combine(_env.FolderIn(_p1, o), "a-locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
			result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		var failure = Assert.Single(result.Failed);
		Assert.Single(failure.Paths);
		Assert.EndsWith("a-locked.txt", failure.Paths[0]);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
		Assert.True(File.Exists(Path.Combine(_env.FolderIn(_p2, o), "b-free.txt")));                 // the others still moved
		Assert.True(File.Exists(Path.Combine(_env.FolderIn(_p2, o), "sub", "c-free.txt")));
		Assert.Equal("p2", o.ProfileId);                                                               // the transcript moved: pinned
		Assert.Equal("p2", o.PendingProfileId);                                                        // the leftover is retried

		Assert.True(_env.Switch.ApplyPending(o));
		Assert.True(File.Exists(Path.Combine(_env.FolderIn(_p2, o), "a-locked.txt")));
		Assert.Null(o.PendingProfileId);
	}

	[Fact]
	public async Task SendToSession_Migrate_WithALockedTranscript_ReportsTheFailure_AndDoesNotRun()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p1");
		_env.WriteSession(_p1, child);

		string error;
		using (new FileStream(_env.TranscriptIn(_p1, child), FileMode.Open, FileAccess.Read, FileShare.None))
			error = await ProfileSwitchEnv.ErrorOf(() => Send(child, orchestrator, migrate: true));

		Assert.Contains("failed: 1 file(s) could not be moved", error);
		Assert.Contains(".jsonl", error);
		Assert.Empty(_env.Turns.RunNodeIds);
		Assert.Equal("p1", child.ProfileId);
	}

	// ── P5: serialised with turn start ───────────────────────────────────────────────────────

	[Fact]
	public void Switch_WhileTheNodesTurnLockIsHeld_QueuesInsteadOfMoving()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);
		var turnLock = _env.Turns.GetTurnLock(o.NodeId);
		turnLock.Wait();   // a turn is just starting: it holds the lock but may not have set the running flag yet
		try
		{
			var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

			Assert.False(result.NodeMoved);
			Assert.Equal([o.NodeId], result.Queued.Select(q => q.NodeId));
			Assert.Equal("p2", o.PendingProfileId);
			Assert.Equal("p1", o.ProfileId);
			Assert.True(File.Exists(_env.TranscriptIn(_p1, o)));
		}
		finally
		{
			turnLock.Release();
		}
	}

	[Fact]
	public void Switch_WhenTheLockIsFree_AppliesUnderIt_AndReleasesIt()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.True(result.NodeMoved);
		Assert.Equal(1, _env.Turns.GetTurnLock(o.NodeId).CurrentCount);
	}

	[Fact]
	public async Task ConcurrentSwitches_OfOneNode_NeverRunTwoMovesAtOnce_AndEndOnOneProfile()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
			Task.Run(() => _env.Switch.Switch(o.NodeId, i % 2 == 0 ? "p2" : "p1", includeDescendants: false))));

		Assert.Equal(1, _env.Turns.GetTurnLock(o.NodeId).CurrentCount);
		Assert.Equal(1, new[] { _p1, _p2 }.Count(dir => File.Exists(_env.TranscriptIn(dir, o))));   // exactly one copy, never lost or split
		Assert.All(results, r => Assert.Empty(r.Failed));
	}

	// ── P6: a stale choice must not silently undo a switch ───────────────────────────────────

	[Fact]
	public void SessionGet_LastUsed_FollowsTheProfileIdPin_NotTheStaleHistoryName()
	{
		var profiles = _env.Settings.Settings.Profiles;
		var history = new LastUsedSettingsDto("default", "default", "p1", "Account A");   // what the last user entry recorded

		var pinned = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileId = "p2" }, profiles);
		var queued = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileId = "p1", PendingProfileId = "p2" }, profiles);
		var toDefault = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileId = "default" }, profiles);
		var unpinned = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt" }, profiles);

		Assert.Equal(("p2", "Account B"), (pinned.ProfileId, pinned.ProfileName));
		Assert.Equal("p2", queued.ProfileId);
		Assert.Equal("default", toDefault.ProfileId);
		Assert.Equal(history, unpinned);
	}

	[Fact]
	public async Task SwitchThroughMcp_ThenATurnWithTheOldPerTurnName_IsAnExplicitChoiceAndMovesBack_WhileARestoredClientSendsTheNewName()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p1");
		_env.WriteSession(_p1, child);
		await Send(child, orchestrator, migrate: true);
		Assert.Equal("p2", child.ProfileId);

		// A client that restores its choice from session.get gets the pin, so its next turn does not undo the switch.
		var restored = SessionRpcModule.ApplyPin(new LastUsedSettingsDto("default", "default", "p1", "Account A"), child, _env.Settings.Settings.Profiles);
		_env.Switch.RecordTurnProfile(child, restored.ProfileName);
		Assert.Equal("p2", child.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, child)));

		// A client still sending its stale per-turn name is an explicit choice: it moves the node (and only it) back.
		_env.Switch.RecordTurnProfile(child, "Account A");
		Assert.Equal("p1", child.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, child)));
	}

	// ── P7: both transcripts, one early return, nothing deleted ──────────────────────────────

	[Fact]
	public void BothTranscripts_TheTargetIsKept_AndTheSourceCopyIsLeftIntact()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o, transcript: "{\"from\":\"p1\"}\n");
		_env.WriteSession(_p2, o, transcript: "{\"from\":\"p2\"}\n");
		File.SetLastWriteTimeUtc(_env.TranscriptIn(_p1, o), DateTime.UtcNow.AddHours(1));   // the source is the newer one

		var report = _env.Migrator.Migrate(o.WorkingDirectory, o.ClaudeSessionId!, _p2);

		Assert.True(report.Found);
		Assert.False(report.TranscriptMoved);
		Assert.Equal("{\"from\":\"p2\"}\n", File.ReadAllText(_env.TranscriptIn(_p2, o)));
		Assert.Equal("{\"from\":\"p1\"}\n", File.ReadAllText(_env.TranscriptIn(_p1, o)));      // nothing deleted or overwritten
		Assert.True(File.Exists(Path.Combine(_env.FolderIn(_p2, o), "tool-result.txt")));
		Assert.False(File.Exists(Path.Combine(_env.FolderIn(_p1, o), "tool-result.txt")) && !File.Exists(Path.Combine(_env.FolderIn(_p2, o), "tool-result.txt")));
	}

	[Fact]
	public void Adoption_WhenTheTargetAlreadyHoldsTheTranscript_ReturnsAtOnce_AndScansNoOtherStore()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o, transcript: "source");
		_env.WriteSession(_p2, o, transcript: "target");

		var report = _env.Migrator.Migrate(o.WorkingDirectory, o.ClaudeSessionId!, _p2, skipWhenTargetExists: true);

		Assert.True(report.Found);
		Assert.False(report.Moved);
		Assert.Empty(report.Failed);
		Assert.Equal("source", File.ReadAllText(_env.TranscriptIn(_p1, o)));
		Assert.True(Directory.Exists(_env.FolderIn(_p1, o)));   // the source folder was not even merged
	}

	[Fact]
	public void Adoption_WhenOnlyAnotherStoreHoldsIt_StillMovesIt()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		var report = _env.Migrator.Migrate(o.WorkingDirectory, o.ClaudeSessionId!, _p2, skipWhenTargetExists: true);

		Assert.True(report.TranscriptMoved);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
	}

	// ── P8: migrate on a running target ──────────────────────────────────────────────────────

	[Fact]
	public async Task Migrate_OnARunningTarget_SkipsTheLoginCheckOfTheOldProfile_StatesQueued_AndRuns()
	{
		_env.AddProfile("p3", "Logged out", loggedIn: false);
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p3");
		_env.Registry.SetSessionRunning(child.NodeId, true);

		var text = ProfileSwitchEnv.TextOf(await Send(child, orchestrator, migrate: true));

		Assert.Contains("[migration queued: the session is running; it moves to profile=Account B when its turn ends]", text);
		Assert.EndsWith("[profile=Account B]", text);
		Assert.Equal("p2", child.PendingProfileId);
		Assert.Equal([child.NodeId], _env.Turns.RunNodeIds);
	}

	// ── P10: pin migration robustness ────────────────────────────────────────────────────────

	[Fact]
	public void PinMigration_SurvivesNullListsAndNullGroups()
	{
		var settings = new AppSettingsModel { Profiles = null!, Tree = null! };
		Assert.Equal(new ProfilePinMigrationSummary(0, 0, 0), new ProfilePinMigration().Migrate(settings));

		var withNulls = new AppSettingsModel { Profiles = null! };
		var dir = new DirectoryNodeModel { Path = "d", Sessions = null!, Groups = null! };
		withNulls.Tree.Add(dir);
		new ProfilePinMigration().Migrate(withNulls);   // must not throw
	}

	[Fact]
	public void PinMigration_WithDuplicateDisplayNames_PinsTheFirstMatch()
	{
		var settings = new AppSettingsModel();
		settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "first", DisplayName = "Same" });
		settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "second", DisplayName = "same" });
		var dir = new DirectoryNodeModel { Path = "d" };
		dir.Sessions.Add(new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileName = "Same" });
		settings.Tree.Add(dir);

		var summary = new ProfilePinMigration().Migrate(settings);

		Assert.Equal(1, summary.Migrated);
		Assert.Equal("first", dir.Sessions[0].ProfileId);
	}

	[Fact]
	public void SettingsLoad_WithNullTreeInTheJson_DoesNotFail()
	{
		File.WriteAllText(Path.Combine(_env.Root, "settings-null"), "x");
		var path = Path.Combine(_env.Root, "load-test");
		Directory.CreateDirectory(path);
		var service = new AppSettingsService(path);
		service.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "A" });
		service.Save();
		var file = Directory.GetFiles(path, "*.json").Single();
		File.WriteAllText(file, File.ReadAllText(file).Replace("\"Tree\": []", "\"Tree\": null"));

		var reloaded = new AppSettingsService(path);
		var ex = Record.Exception(() => reloaded.Load());

		Assert.Null(ex);
	}

	// ── P11: the profile column is capped ────────────────────────────────────────────────────

	[Fact]
	public async Task ListSessions_CapsALongProfileNameLikeASessionName()
	{
		var longName = new string('x', 200);
		_env.AddProfile("p4", longName);
		var s = _env.AddSession("S", profileId: "p4");

		var line = ProfileSwitchEnv.TextOf(await _env.Call("list_sessions", new(), s)).Split('\n').Single(l => l.Contains("profile="));

		var field = line.Split("  ").Single(f => f.StartsWith("profile="))["profile=".Length..];
		Assert.Equal(Constants.Agent.ListSessionsNameMaxLength, field.Length);
		Assert.EndsWith("...", field);
	}

	// ── Cycles, depth and forged ids ─────────────────────────────────────────────────────────

	[Fact]
	public void IncludeDescendants_WithASupervisorCycle_Terminates_AndMovesEachSessionOnce()
	{
		var a = _env.AddSession("A", profileId: "p1");
		var b = _env.AddSession("B", a, "p1");
		a.SupervisorNodeId = b.NodeId;   // a cycle: A -> B -> A
		_env.WriteSession(_p1, a);
		_env.WriteSession(_p1, b);

		var result = _env.Switch.Switch(a.NodeId, "p2", includeDescendants: true);

		Assert.Equal([b.NodeId], result.Moved.Select(m => m.NodeId));
		Assert.Equal("p2", a.ProfileId);
		Assert.Equal("p2", b.ProfileId);
	}

	[Fact]
	public void IncludeDescendants_WithADeepChain_ReachesTheBottom_WithoutRecursion()
	{
		var top = _env.AddSession("L0", profileId: "p1");
		var all = new List<SessionNodeModel> { top };
		for (var i = 1; i <= 60; i++)
			all.Add(_env.AddSession("L" + i, all[^1], "p1"));

		var result = _env.Switch.Switch(top.NodeId, "p2", includeDescendants: true);

		Assert.Equal(60, result.Moved.Count);
		Assert.All(all, s => Assert.Equal("p2", s.ProfileId));
	}

	[Fact]
	public async Task Migrate_WithAForgedOrUnknownNodeId_IsRefused_AndAPrefixOfADescendantWorks()
	{
		var caller   = _env.AddSession("Caller", profileId: "p2");
		var child    = _env.AddSession("Child", caller, "p1");
		var stranger = _env.AddSession("Stranger", profileId: "p1");
		_env.WriteSession(_p1, child);
		_env.WriteSession(_p1, stranger);

		var forged  = await ProfileSwitchEnv.ErrorOf(() => _env.Call("send_to_session", new() { ["nodeId"] = "no-such-node-id", ["prompt"] = "x", ["migrate"] = true }, caller));
		var prefixOfStranger = await ProfileSwitchEnv.ErrorOf(() => _env.Call("send_to_session",
			new() { ["nodeId"] = stranger.NodeId[..Constants.Agent.ShortNodeIdMinLength], ["prompt"] = "x", ["migrate"] = true }, caller));
		var ok = ProfileSwitchEnv.TextOf(await _env.Call("send_to_session",
			new() { ["nodeId"] = child.NodeId[..Constants.Agent.ShortNodeIdMinLength], ["prompt"] = "x", ["migrate"] = true }, caller));

		Assert.Contains("not found", forged, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("only for your own descendants", prefixOfStranger);
		Assert.Equal("p1", stranger.ProfileId);
		Assert.Contains("[migrated to profile=Account B", ok);
		Assert.Equal("p2", child.ProfileId);
	}

	// ── P12: the compaction module uses the DI resolver ──────────────────────────────────────

	[Fact]
	public void CompactionModule_TakesTheResolverFromDi()
	{
		var parameter = typeof(SessionCompactRpcModule).GetConstructors().Single().GetParameters().Single(p => p.ParameterType == typeof(IProfileResolver));
		Assert.True(parameter.HasDefaultValue);   // optional: callers without DI fall back to the shared implementation
	}

	// ── N1: the adoption moves files, so it runs under the turn lock ─────────────────────────

	[Fact]
	public async Task TurnEngine_RunningNodeAndAnExplicitPerTurnProfile_NothingMovesBeforeTheLockIsAcquired()
	{
		using var h = new TurnEngineHarness();
		h.Process.ScriptEvents(new ClaudeStreamEvent { Type = "result", SessionId = "s1" });
		var node = TurnEngineHarness.Node(sessionId: "s1");
		var turnLock = h.Turns.GetTurnLock(TurnEngineHarness.NodeId);
		await turnLock.WaitAsync();   // the node's previous turn is still running

		var queued = h.Run(node, settings: TurnEngineHarness.Chosen(profileDir: @"C:\profiles\p2", profileName: "Other"));
		await Task.Delay(300);

		Assert.Empty(h.Status.Adoptions);   // no pre-lock move from under the running turn
		turnLock.Release();
		await queued;
		Assert.Single(h.Status.Adoptions);  // done once, under the lock, with the chosen profile's dir
		Assert.Equal(@"C:\profiles\p2", h.Status.Adoptions[0].ProfileConfigDir);
	}

	[Fact]
	public async Task HeadlessTurn_AdoptsOnlyAfterItHoldsTheTurnLock_NoPreLockMove()
	{
		using var attention = new AttentionTestEnv();
		using var tags = new TagTestEnv();
		var dir = Path.Combine(_env.Root, "headless");
		Directory.CreateDirectory(dir);
		var settings = new AppSettingsService(dir);
		settings.Settings.SessionFilesRoot = dir;
		settings.Settings.AgentToolsEnabled = false;
		var profiles = new FakeProfileServiceForTurnService();
		var process = new FakeProcessManagerForTurnService
		{
			EmitEvents = onEvent => { onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "s1" }); return Task.CompletedTask; },
		};
		var status = new RecordingSessionStatusService();
		var files  = new FakeFileServiceForTurnService();
		var sut = new SessionTurnService(
			TestEngines.Claude(new FakeStreamingManagerForTurnService(), process, files, settings, status, new Lazy<IAgentMcpServer>(() => new FakeMcpServerForTurnService())), files, settings, profiles, new FakeRegistryForTurnService(),
			new FakeModelServiceForTurnService(), new Lazy<ITelegramBridgeService>(() => new FakeBridgeForTurnService()), attention.Service, tags.Service, new ProfileLabelService(profiles, settings));
		var node = new SessionNodeModel { NodeId = "n1", Name = "N", FileName = "n1.txt", WorkingDirectory = @"C:\proj", ClaudeSessionId = "s1" };
		var turnLock = sut.GetTurnLock(node.NodeId);
		await turnLock.WaitAsync();   // a turn is running

		var queued = sut.RunTurnAsync(node, "hi", TurnSource.Orchestrated, new TurnOptionsModel(ProfileName: "[Sys] me@example.com"));
		await Task.Delay(300);

		Assert.Empty(status.Adoptions);
		turnLock.Release();
		await queued;
		Assert.Single(status.Adoptions);
	}

	// ── N2: a switch back cancels an earlier queued switch ───────────────────────────────────

	[Fact]
	public async Task SwitchingBackToTheCurrentProfile_CancelsAQueuedSwitch()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);
		_env.Registry.SetSessionRunning(o.NodeId, true);
		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);   // queued
		Assert.Equal("p2", o.PendingProfileId);

		_env.Switch.Switch(o.NodeId, "p1", includeDescendants: false);   // back to where it is
		_env.Registry.SetSessionRunning(o.NodeId, false);

		Assert.Null(o.PendingProfileId);
		Assert.False(_env.Switch.ApplyPending(o));                        // the next turn start has nothing to apply
		_env.Switch.RecordTurnProfile(o, "Account A");
		Assert.Equal("p1", o.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, o)));
		var list = ProfileSwitchEnv.TextOf(await _env.Call("list_sessions", new(), o));
		Assert.DoesNotContain("switching-to=", list);
	}
}
