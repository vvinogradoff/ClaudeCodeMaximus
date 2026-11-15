using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// ISSUE.013 on the agent MCP tools: acceptance criterion 3 (dead profile, <c>migrate</c>), R2 (profile stated), R5 (list_sessions
/// <c>profile=</c>), R9 / criterion 10 (spawn error and the path-separator prefix check), and the pin a spawned child inherits.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileMcpToolsTests : IDisposable
{
	private readonly ProfileSwitchEnv _env = new();
	private readonly string _p1;
	private readonly string _p2;

	public ProfileMcpToolsTests()
	{
		_p1 = _env.AddProfile("p1", "Account A");
		_p2 = _env.AddProfile("p2", "Account B");
	}

	public void Dispose() => _env.Dispose();

	private Task<JsonNode> Send(SessionNodeModel target, SessionNodeModel caller, bool? migrate = null, string? mode = null)
	{
		var args = new JsonObject { ["nodeId"] = target.NodeId, ["prompt"] = "go" };
		if (migrate != null)
			args["migrate"] = migrate;
		if (mode != null)
			args["mode"] = mode;
		return _env.Call("send_to_session", args, caller);
	}

	// ── AC3: dead profile ────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task RemovedProfile_ReturnsAnActionableError_NamingTheProfileAndTheMigrateOption_AndNeverRunsOnDefault()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator);
		child.ProfileId   = "p9";            // the pinned profile was removed from the settings
		child.ProfileName = "Retired account";

		var error = await ProfileSwitchEnv.ErrorOf(() => Send(child, orchestrator));

		Assert.Contains("'Retired account'", error);
		Assert.Contains("migrate=true", error);
		Assert.Empty(_env.Turns.RunNodeIds);
	}

	[Fact]
	public async Task LoggedOutProfile_ReturnsTheSameKindOfError_ForWaitAndAsync()
	{
		_env.AddProfile("p3", "Logged out account", loggedIn: false);
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p3");

		foreach (var mode in new[] { "wait", "async" })
		{
			var error = await ProfileSwitchEnv.ErrorOf(() => Send(child, orchestrator, mode: mode));
			Assert.Contains("'Logged out account'", error);
			Assert.Contains("logged out", error);
			Assert.Contains("migrate=true", error);
		}

		Assert.Empty(_env.Turns.RunNodeIds);
	}

	[Fact]
	public async Task MigrateTrue_MovesTheDeadChildToTheCallersProfile_ThenRuns()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p9");
		child.ProfileName = "Retired account";
		_env.WriteSession(_p1, child);   // its files are still in an old store

		var text = ProfileSwitchEnv.TextOf(await Send(child, orchestrator, migrate: true));

		Assert.Contains("[migrated to profile=Account B", text);
		Assert.EndsWith("[profile=Account B]", text);
		Assert.Equal("p2", child.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, child)));
		Assert.False(File.Exists(_env.TranscriptIn(_p1, child)));
		Assert.Equal([child.NodeId], _env.Turns.RunNodeIds);
	}

	[Fact]
	public async Task MigrateTrue_ReportsOldConfigDirPathsFoundInTheTranscript()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p1");
		var oldPath = Path.Combine(_p1, "projects", "x", "memory", "MEMORY.md");
		_env.WriteSession(_p1, child, "{\"path\":\"" + oldPath.Replace("\\", "\\\\") + "\"}\n");

		var text = ProfileSwitchEnv.TextOf(await Send(child, orchestrator, migrate: true));

		Assert.Contains("1 path(s) under the old config dir", text);
		Assert.Contains(oldPath, text);
	}

	[Fact]
	public async Task MigrateTrue_IsRefusedForASessionThatIsNotTheCallersDescendant()
	{
		var caller   = _env.AddSession("Caller", profileId: "p2");
		var stranger = _env.AddSession("Stranger", profileId: "p1");
		_env.WriteSession(_p1, stranger);

		var error = await ProfileSwitchEnv.ErrorOf(() => Send(stranger, caller, migrate: true));

		Assert.Contains("only for your own descendants", error);
		Assert.Equal("p1", stranger.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p1, stranger)));
		Assert.Empty(_env.Turns.RunNodeIds);
	}

	[Fact]
	public async Task MigrateTrue_AlsoReachesAGrandchild()
	{
		var caller     = _env.AddSession("Caller", profileId: "p2");
		var child      = _env.AddSession("Child", caller, "p2");
		var grandchild = _env.AddSession("Grandchild", child, "p1");
		_env.WriteSession(_p1, grandchild);

		await Send(grandchild, caller, migrate: true);

		Assert.Equal("p2", grandchild.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, grandchild)));
	}

	[Fact]
	public async Task MigrateTrue_NeverMovesTheCallersOwnDescendantsBeyondTheTarget()
	{
		var caller   = _env.AddSession("Caller", profileId: "p2");
		var child    = _env.AddSession("Child", caller, "p1");
		var grandkid = _env.AddSession("Grandkid", child, "p1");
		_env.WriteSession(_p1, child);
		_env.WriteSession(_p1, grandkid);

		await Send(child, caller, migrate: true);

		Assert.Equal("p2", child.ProfileId);
		Assert.Equal("p1", grandkid.ProfileId);   // an MCP caller moves only the explicit target, no cascade
	}

	[Fact]
	public async Task HealthyChild_RunsOnItsPinnedProfile_AndTheResultStatesIt_ForWaitAndAsync()
	{
		var orchestrator = _env.AddSession("O", profileId: "p2");
		var child = _env.AddSession("C", orchestrator, "p1");

		var wait  = ProfileSwitchEnv.TextOf(await Send(child, orchestrator));
		var async = ProfileSwitchEnv.TextOf(await Send(child, orchestrator, mode: "async"));

		Assert.Equal("ok\n[profile=Account A]", wait);
		Assert.Contains("on profile=Account A", async);
	}

	// ── R5 ───────────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task ListSessions_EveryRowStatesItsProfile_DefaultForUnpinnedAndSystemLabels()
	{
		var pinned    = _env.AddSession("Pinned", profileId: "p1");
		var unpinned  = _env.AddSession("Unpinned");
		var sysLabel  = _env.AddSession("SysLabel");
		sysLabel.ProfileName = "[Sys] me@example.com";
		var missing   = _env.AddSession("Missing", profileId: "p9");
		missing.ProfileName = "Gone";

		var lines = ProfileSwitchEnv.TextOf(await _env.Call("list_sessions", new() { ["scope"] = "all" }, pinned)).Split('\n');

		string Row(string name) => lines.Single(l => l.Contains($"  {name}  "));
		Assert.Contains("profile=Account A", Row("Pinned"));
		Assert.Contains("profile=default", Row("Unpinned"));
		Assert.Contains("profile=default", Row("SysLabel"));
		Assert.Contains("profile=Gone (missing)", Row("Missing"));
	}

	[Fact]
	public void ToolSchemas_DeclareMigrate_AndTheWorkingDirRule()
	{
		var tools = ((JsonNode)typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!)["tools"]!.AsArray();
		var send  = tools.Single(t => t!["name"]!.GetValue<string>() == "send_to_session")!;
		var spawn = tools.Single(t => t!["name"]!.GetValue<string>() == "spawn_session")!;

		Assert.Equal("boolean", send["inputSchema"]!["properties"]!["migrate"]!["type"]!.GetValue<string>());
		Assert.Contains("never registers a new directory", spawn["inputSchema"]!["properties"]!["workingDir"]!["description"]!.GetValue<string>());
	}

	// ── R9 / AC10 ────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Spawn_WithAnUnregisteredDir_ExplainsAndListsTheRegisteredOnes_AndDoesNotRegisterIt()
	{
		var caller = _env.AddSession("Caller");
		var outside = Path.Combine(_env.Root, "elsewhere");

		var error = await ProfileSwitchEnv.ErrorOf(() => _env.Call("spawn_session", new() { ["name"] = "w", ["workingDir"] = outside }, caller));

		Assert.Equal(
			$"'{outside}' is not under a project in the Tessyn tree. Registered: {_env.Dir.Path}. Add it in Tessyn or use parentNodeId.",
			error);
		Assert.DoesNotContain(_env.Settings.Settings.Tree, d => d.Path == outside);
	}

	[Fact]
	public async Task Spawn_WithANeighbourDirThatSharesAPrefix_IsRefused_ButASubdirectoryIsAccepted()
	{
		var caller = _env.AddSession("Caller");
		var neighbour = _env.Dir.Path + "c";          // <dir>c shares the text prefix of <dir>
		var below     = Path.Combine(_env.Dir.Path, "sub");

		var error = await ProfileSwitchEnv.ErrorOf(() => _env.Call("spawn_session", new() { ["name"] = "w", ["workingDir"] = neighbour }, caller));
		Assert.Contains("is not under a project in the Tessyn tree", error);

		var ok = ProfileSwitchEnv.TextOf(await _env.Call("spawn_session", new() { ["name"] = "w", ["workingDir"] = below }, caller));
		Assert.Contains("created", ok);
	}

	[Theory]
	[InlineData(@"C:\a\bc", @"C:\a\b", false)]
	[InlineData(@"C:\a\b", @"C:\a\b", true)]
	[InlineData(@"C:\a\b\", @"C:\a\b", true)]
	[InlineData(@"C:\a\b", @"C:\a\b\", true)]
	[InlineData(@"C:\a\b\c", @"C:\a\b", true)]
	[InlineData(@"c:\A\B\c", @"C:\a\b", true)]
	[InlineData(@"C:\a", @"C:\a\b", false)]
	[InlineData(@"C:\a\b/c", @"C:\a\b", true)]
	public void IsUnderDirectory_RespectsPathSeparators(string workingDir, string registered, bool expected) =>
		Assert.Equal(expected, SessionRegistry.IsUnderDirectory(workingDir, registered));

	[Fact]
	public async Task Spawn_WithManyRegisteredDirs_ListsTheFirstOnesAndCountsTheRest()
	{
		for (var i = 0; i < 15; i++)
			_env.Registry.AddDirectory(Path.Combine(_env.Root, $"proj{i}"));
		var caller = _env.AddSession("Caller");

		var error = await ProfileSwitchEnv.ErrorOf(() => _env.Call("spawn_session", new() { ["name"] = "w", ["workingDir"] = Path.Combine(_env.Root, "x") }, caller));

		Assert.Contains("(+4 more)", error);
	}

	// ── The child inherits its supervisor's profile by id ────────────────────────────────────

	[Fact]
	public async Task SpawnedChild_IsPinnedByProfileId_SoARenameDoesNotRerouteIt()
	{
		var orchestrator = _env.AddSession("O", profileId: "p1");

		await _env.Call("spawn_session", new() { ["name"] = "worker" }, orchestrator);
		var child = _env.Settings.Settings.Tree.SelectMany(d => d.Sessions).Single(s => s.Name == "worker");
		Assert.Equal("p1", child.ProfileId);

		_env.Settings.Settings.Profiles.Single(p => p.ProfileId == "p1").DisplayName = "Account A (renamed)";
		var resolution = _env.Resolver.Resolve(child, child.ProfileName);

		Assert.True(resolution.IsResolved);
		Assert.Equal(_p1, resolution.ConfigDir);
	}
}
