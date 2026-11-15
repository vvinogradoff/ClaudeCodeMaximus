using System;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// ISSUE.013 acceptance criteria 1 (cascade), 2 (decline), 7 (live streaming process) and R3 (complete migration), R1 (queued running
/// sessions), over real settings, registry, migrator and switch service on temp directories.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileSwitchServiceTests : IDisposable
{
	private readonly ProfileSwitchEnv _env = new();
	private readonly string _p1;
	private readonly string _p2;

	public ProfileSwitchServiceTests()
	{
		_p1 = _env.AddProfile("p1", "Account A");
		_p2 = _env.AddProfile("p2", "Account B");
	}

	public void Dispose() => _env.Dispose();

	/// <summary>An orchestrator with two idle children, all pinned to P1, with their sessions stored under P1.</summary>
	private (SessionNodeModel O, SessionNodeModel C1, SessionNodeModel C2) Family()
	{
		var o  = _env.AddSession("O", profileId: "p1");
		var c1 = _env.AddSession("C1", o, "p1");
		var c2 = _env.AddSession("C2", o, "p1");
		foreach (var s in new[] { o, c1, c2 })
			_env.WriteSession(_p1, s);
		return (o, c1, c2);
	}

	// ── AC1 ──────────────────────────────────────────────────────────────────────────────────

	[Fact]
	public void Cascade_MovesTheNodeAndItsDescendants_TranscriptsFoldersAndPins()
	{
		var (o, c1, c2) = Family();

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: true);

		Assert.True(result.NodeMoved);
		Assert.Equal("p2", result.ProfileId);
		Assert.Equal("Account B", result.ProfileName);
		Assert.Equal(new[] { c1.NodeId, c2.NodeId }.Order(), result.Moved.Select(m => m.NodeId).Order());
		Assert.All(result.Moved, m => Assert.Equal("p1", m.FromProfileId));
		Assert.Empty(result.Queued);

		foreach (var s in new[] { o, c1, c2 })
		{
			Assert.Equal("p2", s.ProfileId);
			Assert.Equal("Account B", s.ProfileName);
			Assert.True(File.Exists(_env.TranscriptIn(_p2, s)));
			Assert.True(File.Exists(Path.Combine(_env.FolderIn(_p2, s), "tool-result.txt")));
			Assert.False(File.Exists(_env.TranscriptIn(_p1, s)));  // nothing remains under P1
			Assert.False(Directory.Exists(_env.FolderIn(_p1, s)));
		}
	}

	[Fact]
	public void Cascade_ReachesGrandchildren_AndNeverTouchesSessionsOutsideTheSubtree()
	{
		var (o, c1, _) = Family();
		var grandchild = _env.AddSession("G", c1, "p1");
		var stranger   = _env.AddSession("Stranger", profileId: "p1");
		_env.WriteSession(_p1, grandchild);
		_env.WriteSession(_p1, stranger);

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: true);

		Assert.Contains(result.Moved, m => m.NodeId == grandchild.NodeId);
		Assert.Equal("p2", grandchild.ProfileId);
		Assert.Equal("p1", stranger.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, stranger)));
	}

	[Fact]
	public async System.Threading.Tasks.Task Cascade_ThenSendToAChild_SucceedsWithoutUserAction()
	{
		var (o, c1, _) = Family();
		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: true);

		var text = ProfileSwitchEnv.TextOf(await _env.Call("send_to_session", new() { ["nodeId"] = c1.NodeId, ["prompt"] = "go" }, o));

		Assert.Equal("ok\n[profile=Account B]", text);
		Assert.Equal([c1.NodeId], _env.Turns.RunNodeIds);
	}

	// ── AC2 ──────────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async System.Threading.Tasks.Task Decline_OnlyTheNodeMoves_ChildrenStayOnP1_AndSendRunsOnP1AndSaysSo()
	{
		var (o, c1, c2) = Family();

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.True(result.NodeMoved);
		Assert.Empty(result.Moved);
		Assert.Equal("p2", o.ProfileId);
		Assert.Equal("p1", c1.ProfileId);
		Assert.Equal("p1", c2.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, c1)));

		var list = ProfileSwitchEnv.TextOf(await _env.Call("list_sessions", new(), o));   // the caller's project: its own row, workers nested under it
		Assert.Contains(list.Split('\n'), l => l.Contains("profile=Account B") && l.Contains("(you)"));
		Assert.Equal(2, list.Split('\n').Count(l => l.Contains("profile=Account A")));

		var text = ProfileSwitchEnv.TextOf(await _env.Call("send_to_session", new() { ["nodeId"] = c1.NodeId, ["prompt"] = "go" }, o));
		Assert.EndsWith("[profile=Account A]", text);
	}

	// ── R1: running sessions are queued, never interrupted ────────────────────────────────────

	[Fact]
	public void RunningDescendant_IsQueued_NotMoved_AndNotInterrupted_ThenAppliedWhenItsNextTurnStarts()
	{
		var (o, c1, c2) = Family();
		_env.Registry.SetSessionRunning(c1.NodeId, true);
		_env.Streaming.Alive.Add(c1.NodeId);

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: true);

		Assert.Equal([c2.NodeId], result.Moved.Select(m => m.NodeId));
		Assert.Equal([c1.NodeId], result.Queued.Select(m => m.NodeId));
		Assert.Equal("p1", c1.ProfileId);
		Assert.Equal("p2", c1.PendingProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, c1)));  // untouched while it runs
		Assert.Empty(_env.Streaming.Stopped);                  // the live process is not interrupted

		_env.Registry.SetSessionRunning(c1.NodeId, false);
		Assert.True(_env.Switch.ApplyPending(c1));

		Assert.Equal("p2", c1.ProfileId);
		Assert.Null(c1.PendingProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, c1)));
		Assert.Equal([c1.NodeId], _env.Streaming.Stopped);
	}

	[Fact]
	public async System.Threading.Tasks.Task QueuedSwitch_IsShownInListSessions()
	{
		var (o, c1, _) = Family();
		_env.Registry.SetSessionRunning(c1.NodeId, true);
		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: true);

		var list = ProfileSwitchEnv.TextOf(await _env.Call("list_sessions", new() { ["scope"] = "subtree", ["nodeId"] = o.NodeId }, o));

		Assert.Contains(list.Split('\n'), l => l.Contains("profile=Account A") && l.Contains("switching-to=Account B") && l.Contains("running"));
	}

	[Fact]
	public void RunningNode_ItselfIsQueued_AndNodeMovedIsFalse()
	{
		var (o, _, _) = Family();
		_env.Registry.SetSessionRunning(o.NodeId, true);

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.False(result.NodeMoved);
		Assert.Equal([o.NodeId], result.Queued.Select(q => q.NodeId));
		Assert.Equal("p1", o.ProfileId);
	}

	// ── R7 / AC7 ─────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async System.Threading.Tasks.Task LiveStreamingProcess_IsStoppedOnSwitch_SoTheNextLaunchUsesTheNewConfigDir()
	{
		var (o, _, _) = Family();
		_env.Streaming.Alive.Add(o.NodeId);

		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.Equal([o.NodeId], _env.Streaming.Stopped);
		Assert.False(_env.Streaming.IsAlive(o.NodeId));
		// The next launch resolves the new profile: its config dir is P2's (the dir CLAUDE_CONFIG_DIR - and with it the memory path - comes from).
		var settings = await new TurnSettingsResolver(
			_env.Settings, _env.Turns, _env.ProfileService, new FakeModelServiceForTurnService(),
			new ProfileLabelService(_env.ProfileService, _env.Settings), _env.Resolver).ResolveAsync(o, null);
		Assert.Equal(_p2, settings.ProfileConfigDir);
		Assert.Equal("Account B", settings.ProfileDisplayName);
	}

	// ── R3 ───────────────────────────────────────────────────────────────────────────────────

	[Fact]
	public void Migration_MergesTheSessionFolderWhenItExistsInBothStores_AndKeepsTheTargetsFiles()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o, transcript: "{\"from\":\"p1\"}\n", toolResultName: "only-in-p1.txt");
		// The session folder already exists under P2 (the incident: the folder was under both stores).
		Directory.CreateDirectory(_env.FolderIn(_p2, o));
		File.WriteAllText(Path.Combine(_env.FolderIn(_p2, o), "only-in-p2.txt"), "p2");
		File.WriteAllText(Path.Combine(_env.FolderIn(_p2, o), "tool-result.txt"), "p2-version");
		File.WriteAllText(Path.Combine(_env.FolderIn(_p1, o), "tool-result.txt"), "p1-version");

		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
		Assert.False(File.Exists(_env.TranscriptIn(_p1, o)));
		Assert.Equal("result", File.ReadAllText(Path.Combine(_env.FolderIn(_p2, o), "only-in-p1.txt")));
		Assert.True(File.Exists(Path.Combine(_env.FolderIn(_p2, o), "only-in-p2.txt")));
		Assert.Equal("p2-version", File.ReadAllText(Path.Combine(_env.FolderIn(_p2, o), "tool-result.txt"))); // the target's file wins
	}

	[Fact]
	public void Migration_ReportsAbsolutePathsUnderTheOldConfigDir_AndDoesNotRewriteThem()
	{
		var o = _env.AddSession("O", profileId: "p1");
		var oldMemory = Path.Combine(_p1, "projects", "slug", "memory", "MEMORY.md");
		var jsonEscaped = oldMemory.Replace("\\", "\\\\");
		var transcript = "{\"type\":\"tool_result\",\"path\":\"" + jsonEscaped + "\"}\n";
		_env.WriteSession(_p1, o, transcript);

		var result = _env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.Contains(oldMemory, result.OldConfigPaths);
		Assert.Equal(transcript, File.ReadAllText(_env.TranscriptIn(_p2, o))); // reported, never rewritten
	}

	[Fact]
	public void Migration_FromTheDefaultHome_IsMovedToo()
	{
		var o = _env.AddSession("O");
		_env.WriteSession(null, o);

		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
		Assert.False(File.Exists(_env.TranscriptIn(null, o)));
		Assert.Equal("p2", o.ProfileId);
	}

	[Fact]
	public void Migration_AlsoMovesThePriorClaudeSessions()
	{
		var o = _env.AddSession("O", profileId: "p1");
		var prior = Guid.NewGuid().ToString();
		o.PriorClaudeSessionIds.Add(prior);
		_env.WriteSession(_p1, o);
		var priorFile = Path.Combine(_env.SlugDir(_p1, o), prior + ".jsonl");
		File.WriteAllText(priorFile, "{}");

		_env.Switch.Switch(o.NodeId, "p2", includeDescendants: false);

		Assert.True(File.Exists(Path.Combine(_env.SlugDir(_p2, o), prior + ".jsonl")));
		Assert.False(File.Exists(priorFile));
	}

	[Fact]
	public void SwitchToTheDefaultProfile_PinsDefaultAndMovesIntoTheDefaultHome()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		var result = _env.Switch.Switch(o.NodeId, "default", includeDescendants: false);

		Assert.Equal("default", result.ProfileId);
		Assert.Equal("default", o.ProfileId);
		Assert.Null(o.ProfileName);
		Assert.True(File.Exists(_env.TranscriptIn(null, o)));
	}

	[Fact]
	public void SwitchToTheProfileTheNodeIsAlreadyOn_MovesNothing()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		var result = _env.Switch.Switch(o.NodeId, "p1", includeDescendants: false);

		Assert.False(result.NodeMoved);
		Assert.Empty(result.Moved);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, o)));
	}

	[Fact]
	public void UnknownSessionOrProfile_IsAnError()
	{
		var o = _env.AddSession("O");

		Assert.Equal("Session not found", Assert.Throws<ProfileSwitchException>(() => _env.Switch.Switch("nope", "p1", false)).Message);
		Assert.Contains("Profile not found", Assert.Throws<ProfileSwitchException>(() => _env.Switch.Switch(o.NodeId, "p9", false)).Message);
	}

	[Fact]
	public void SameAccountProfiles_AreAlwaysMovedToo_NoSkip()
	{
		// Two profiles that share one claude.ai account are still two stores: R3 is never skipped (decision A3).
		_env.AddProfile("p3", "Account A (second login)");
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		_env.Switch.Switch(o.NodeId, "p3", includeDescendants: false);

		Assert.True(File.Exists(_env.TranscriptIn(Path.Combine(_env.ProfilesRoot, "p3"), o)));
		Assert.False(File.Exists(_env.TranscriptIn(_p1, o)));
	}

	// ── The Avalonia profile change (a turn on another profile): switch without cascade ───────

	[Fact]
	public void RecordTurnProfile_ToAnotherProfile_SwitchesTheNodeOnly_NoCascade()
	{
		var (o, c1, _) = Family();

		_env.Switch.RecordTurnProfile(o, "Account B");

		Assert.Equal("p2", o.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
		Assert.Equal("p1", c1.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, c1)));
	}

	[Fact]
	public void RecordTurnProfile_ForAnUnpinnedNode_RecordsThePin_AndAnUnknownNameIsIgnored()
	{
		var o = _env.AddSession("O");

		_env.Switch.RecordTurnProfile(o, "[Sys] me@example.com");
		Assert.Equal("default", o.ProfileId);

		var p = _env.AddSession("P");
		_env.Switch.RecordTurnProfile(p, "No such profile");
		Assert.Null(p.ProfileId);
	}

	[Fact]
	public void RecordTurnProfile_SameProfile_DoesNotStopAStreamingProcess()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.Streaming.Alive.Add(o.NodeId);

		_env.Switch.RecordTurnProfile(o, "Account A");

		Assert.Empty(_env.Streaming.Stopped);
	}
}
