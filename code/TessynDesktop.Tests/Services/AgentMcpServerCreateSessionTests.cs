using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;
using static TessynDesktop.Tests.Services.CreateSessionTestEnv;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.15.8 (<c>create_session</c>) and TC.ME.04 (the caller's tier) over a real <see cref="SessionRegistry"/>, settings and profile resolver —
/// FR.15.15, FR.16.6, FR.12.35; UC.15.4.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerCreateSessionTests : IDisposable
{
	private readonly CreateSessionTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private static async Task<string> ErrorOf(Func<Task<JsonNode>> action) => (await Assert.ThrowsAnyAsync<Exception>(action)).Message;

	private static JsonObject Args(string name, params (string Key, JsonNode? Value)[] more)
	{
		var args = new JsonObject { ["name"] = name };
		foreach (var (key, value) in more)
			args[key] = value;
		return args;
	}

	private static JsonNode ToolsList()
	{
		var method = typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!;
		return (JsonNode)method.Invoke(null, null)!;
	}

	// ── Tool surface ────────────────────────────────────────────────────────────

	[Fact]
	public void ToolsList_CreateSession_DeclaresAllArguments()
	{
		var tool = ToolsList()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "create_session")!;
		var schema = tool["inputSchema"]!;

		Assert.Equal(
			new[] { "after", "effort", "group", "model", "name", "profile", "prompt", "sound", "streaming", "workingDir" },
			schema["properties"]!.AsObject().Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
		Assert.Equal(["name"], schema["required"]!.AsArray().Select(v => v!.GetValue<string>()));
		Assert.Equal(Constants.Remote.Efforts, schema["properties"]!["effort"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
		Assert.Equal("boolean", schema["properties"]!["streaming"]!["type"]!.GetValue<string>());
		Assert.Contains("not a worker", tool["description"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
		Assert.Contains("spawn_session", tool["description"]!.GetValue<string>());
	}

	// ── Placement ───────────────────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_After_GoesRightAfterAnchorInItsGroup_TopLevelFields()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("after", _env.Anchor.NodeId)), caller);

		Assert.Equal(["First", "Anchor", "New", "Last"], _env.Ui.Sessions.Select(s => s.Name));
		var created = _env.Named("New");
		Assert.Null(created.SupervisorNodeId);
		Assert.Equal(0, created.OrchestrationDepth);
		Assert.Equal(32, created.NodeId.Length);
		Assert.False(string.IsNullOrEmpty(created.AgentToken));
		Assert.Equal(_env.Ui.WorkingDirectory, created.WorkingDirectory);
	}

	[Fact]
	public async Task CreateSession_AfterWorker_GoesAfterItsTopLevelSupervisor()
	{
		var caller = _env.AddCaller();
		var worker = _env.Registry.AddSessionToGroup(_env.Ui, "Worker of anchor");
		worker.SupervisorNodeId   = _env.Anchor.NodeId;
		worker.OrchestrationDepth = 1;

		await _env.Create(Args("New", ("after", worker.NodeId)), caller);

		Assert.Equal(["First", "Anchor", "New", "Last", "Worker of anchor"], _env.Ui.Sessions.Select(s => s.Name));
	}

	[Fact]
	public async Task CreateSession_AfterShortId_Resolved()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("after", _env.Anchor.NodeId[..8])), caller);

		Assert.Equal(["First", "Anchor", "New", "Last"], _env.Ui.Sessions.Select(s => s.Name));
	}

	[Fact]
	public async Task CreateSession_AfterUnknownOrAmbiguous_ErrorAndNothingCreated()
	{
		var caller = _env.AddCaller();
		_env.First.NodeId  = "abcdef01" + new string('0', 24);
		_env.Anchor.NodeId = "abcdef02" + new string('0', 24);
		var before = _env.AllSessions().Count;

		Assert.Contains("Session for 'after' not found: zzzzzzzz", await ErrorOf(() => _env.Create(Args("New", ("after", "zzzzzzzz")), caller)));
		Assert.Contains("matches 2 sessions", await ErrorOf(() => _env.Create(Args("New", ("after", "abcdef")), caller)));
		Assert.Contains("too short", await ErrorOf(() => _env.Create(Args("New", ("after", "abc")), caller)));

		Assert.Equal(before, _env.AllSessions().Count);
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_NoPlacement_GoesRightAfterCaller()
	{
		var caller = _env.Registry.AddSessionToGroup(_env.Ui, "Caller");
		_env.Ui.Sessions.Remove(caller);
		_env.Ui.Sessions.Insert(1, caller);   // First, Caller, Anchor, Last
		caller.ModelId = Opus;

		await _env.Create(Args("New"), caller);

		Assert.Equal(["First", "Caller", "New", "Anchor", "Last"], _env.Ui.Sessions.Select(s => s.Name));
	}

	[Fact]
	public async Task CreateSession_WorkingDir_GoesLastInDirectory()
	{
		var caller = _env.AddCaller();
		_env.Registry.AddSession(_env.Dir2, "Existing");

		await _env.Create(Args("New", ("workingDir", _env.Dir2.Path)), caller);

		Assert.Equal(["Existing", "New"], _env.Dir2.Sessions.Select(s => s.Name));
		Assert.Equal(_env.Dir2.Path, _env.Named("New").WorkingDirectory);
	}

	[Fact]
	public async Task CreateSession_WorkingDirBelowAProject_UsesThatProject()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("workingDir", System.IO.Path.Combine(_env.Dir2.Path, "src", "deep"))), caller);

		Assert.Equal(["New"], _env.Dir2.Sessions.Select(s => s.Name));
	}

	[Fact]
	public async Task CreateSession_WorkingDirAndGroup_MissingGroupCreated_GoesLastInIt()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("workingDir", _env.Dir2.Path), ("group", "Fresh group")), caller);

		var group = Assert.Single(_env.Dir2.Groups);
		Assert.Equal("Fresh group", group.Name);
		Assert.Equal(32, group.NodeId.Length);
		Assert.Equal(["New"], group.Sessions.Select(s => s.Name));
		Assert.Single(_env.Added);   // the group comes with the session's one event
	}

	[Fact]
	public async Task CreateSession_GroupOnly_UsesCallersDirectory()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("group", "ui")), caller);   // matched case-insensitively

		Assert.Equal(["First", "Anchor", "Last", "New"], _env.Ui.Sessions.Select(s => s.Name));
	}

	[Fact]
	public async Task CreateSession_AfterWithWorkingDirOrGroup_Error()
	{
		var caller = _env.AddCaller();

		Assert.Equal(Constants.CreateSession.PlacementConflictText,
			await ErrorOf(() => _env.Create(Args("New", ("after", _env.Anchor.NodeId), ("workingDir", _env.Dir.Path)), caller)));
		Assert.Equal(Constants.CreateSession.PlacementConflictText,
			await ErrorOf(() => _env.Create(Args("New", ("after", _env.Anchor.NodeId), ("group", "UI")), caller)));
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_WorkingDirOutsideProjects_ErrorListsRegistered()
	{
		var caller = _env.AddCaller();

		var error = await ErrorOf(() => _env.Create(Args("New", ("workingDir", @"C:\Nowhere\At\All")), caller));

		Assert.Contains("is not under a project", error);
		Assert.Contains(_env.Dir.Path, error);
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_BlankName_Error()
	{
		var caller = _env.AddCaller();

		Assert.Equal(Constants.CreateSession.BlankNameText, await ErrorOf(() => _env.Create(Args("   "), caller)));
		Assert.Equal(Constants.CreateSession.NameRequiredText, await ErrorOf(() => _env.Create(new JsonObject(), caller)));
		Assert.Empty(_env.Added);
	}

	// ── Settings ────────────────────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_Settings_Stored()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("Set", ("model", Opus), ("effort", "max"), ("profile", "p1"), ("streaming", true), ("sound", false)), caller);
		await _env.Create(Args("Plain"), caller);

		var set = _env.Named("Set");
		Assert.Equal((Opus, "max", "p1", "Account A"), (set.ModelId, set.Effort, set.ProfileId, set.ProfileName));
		Assert.True(set.IsStreamingMode);
		Assert.False(set.PlayCompletionSound);

		var plain = _env.Named("Plain");
		Assert.Null(plain.ModelId);
		Assert.Null(plain.Effort);
		Assert.Equal(("p2", "Account B"), (plain.ProfileId, plain.ProfileName));   // the caller's profile
		Assert.False(plain.IsStreamingMode);
		Assert.True(plain.PlayCompletionSound);
	}

	[Fact]
	public async Task CreateSession_Profile_ByIdByNameOrDefault()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("ById", ("profile", "p1")), caller);
		await _env.Create(Args("ByName", ("profile", "account b")), caller);
		await _env.Create(Args("ByDefault", ("profile", "default")), caller);

		Assert.Equal("p1", _env.Named("ById").ProfileId);
		Assert.Equal("p2", _env.Named("ByName").ProfileId);
		Assert.Equal("default", _env.Named("ByDefault").ProfileId);
		Assert.Null(_env.Named("ByDefault").ProfileName);
	}

	[Fact]
	public async Task CreateSession_UnknownModelEffortOrProfile_ErrorListsValidValues()
	{
		var caller = _env.AddCaller();

		var model = await ErrorOf(() => _env.Create(Args("New", ("model", "gpt-9")), caller));
		Assert.Equal($"Unknown model 'gpt-9'. Models: {Opus}, {Sonnet}, {Haiku}.", model);

		var effort = await ErrorOf(() => _env.Create(Args("New", ("effort", "ultra")), caller));
		Assert.Equal("Unknown effort 'ultra'. Efforts: default, max, high, medium, low.", effort);

		var profile = await ErrorOf(() => _env.Create(Args("New", ("profile", "nobody")), caller));
		Assert.Equal("Unknown profile 'nobody'. Profiles: default, Account A, Account B.", profile);

		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_ManyModels_ErrorListsTwelveAndCountsTheRest()
	{
		var caller = _env.AddCaller();
		_env.Models.Models = Enumerable.Range(1, 15).Select(i => new ClaudeModelInfo($"m{i}", $"m{i}", $"M{i}")).ToList();

		var error = await ErrorOf(() => _env.Create(Args("New", ("model", "x")), caller));

		Assert.Equal("Unknown model 'x'. Models: m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12 (+3 more).", error);
	}

	[Fact]
	public async Task CreateSession_ModelAndEffort_MatchedCaseInsensitively_StoredInCatalogForm()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("model", Sonnet.ToUpperInvariant()), ("effort", "MAX")), caller);

		Assert.Equal((Sonnet, "max"), (_env.Named("New").ModelId, _env.Named("New").Effort));
	}

	[Fact]
	public async Task CreateSession_DefaultModelAndEffort_StoredAsNoFlag()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("model", "default"), ("effort", "default")), caller);

		Assert.Equal("default", _env.Named("New").ModelId);
		Assert.Null(_env.Named("New").Effort);
	}

	[Fact]
	public async Task CreateSession_LoggedOutProfile_Error()
	{
		var caller = _env.AddCaller();
		_env.AddProfile("p3", "Logged out account", loggedIn: false);

		var error = await ErrorOf(() => _env.Create(Args("New", ("profile", "p3")), caller));

		Assert.Equal("Profile 'Logged out account' is signed out (no credentials): sign it in again in Tessyn, or pick another profile.", error);
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_OneSessionAddedEvent_SettingsAlreadySet()
	{
		var caller = _env.AddCaller();

		await _env.Create(Args("New", ("after", _env.Anchor.NodeId), ("model", Sonnet), ("effort", "high"), ("streaming", true)), caller);

		var added = Assert.Single(_env.Added);
		Assert.Equal("New", added.Session!.Name);
		Assert.Equal((Sonnet, "high", true), (added.Session.ModelId, added.Session.Effort, added.Session.IsStreamingMode));
		Assert.Same(_env.Ui, added.ParentGroup);
		Assert.Equal(2, added.Index);
		Assert.Same(_env.Anchor, added.PrecedingSession);
	}

	// ── Guardrails ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_WorkerCaller_Refused()
	{
		var supervisor = _env.AddCaller();
		var worker = _env.AddCaller("Worker");
		worker.SupervisorNodeId   = supervisor.NodeId;
		worker.OrchestrationDepth = 1;

		Assert.Equal(Constants.CreateSession.WorkerRefusalText, await ErrorOf(() => _env.Create(Args("New"), worker)));
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_ModelAboveCallerTier_Refused()
	{
		var caller = _env.AddCaller(modelId: Sonnet);

		var error = await ErrorOf(() => _env.Create(Args("New", ("model", Opus)), caller));

		Assert.Equal($"Tier violation: a Sonnet session cannot create a Opus session. Caller model: '{Sonnet}', requested: '{Opus}'.", error);
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_ModelAtCallerTier_Allowed()
	{
		var caller = _env.AddCaller(modelId: Sonnet);

		await _env.Create(Args("Same", ("model", Sonnet)), caller);
		await _env.Create(Args("Lower", ("model", Haiku)), caller);
		await _env.Create(Args("NoFlag", ("model", "default")), caller);   // no model asked for: nothing to check

		Assert.Equal(3, _env.Added.Count);
	}

	[Fact]
	public async Task CreateSession_RateCap_EleventhCallWithinAnHourRefused_RefusedCallsDoNotCount()
	{
		var caller = _env.AddCaller();

		for (var i = 1; i <= 5; i++)
			await _env.Create(Args($"S{i}"), caller);
		await ErrorOf(() => _env.Create(Args("Bad effort", ("effort", "ultra")), caller));           // refused before a slot is taken
		await ErrorOf(() => _env.Create(Args("Bad dir", ("workingDir", @"C:\Nowhere")), caller));    // slot taken, then given back
		for (var i = 6; i <= 10; i++)
			await _env.Create(Args($"S{i}"), caller);

		var error = await ErrorOf(() => _env.Create(Args("S11"), caller));
		Assert.Equal("Session creation limit reached: at most 10 sessions per hour. Try again later.", error);
		Assert.Equal(10, _env.Added.Count);

		_env.Clock.Advance(Constants.Agent.CreatedSessionsWindow);
		await _env.Create(Args("S11"), caller);
		Assert.Equal(11, _env.Added.Count);
	}

	// ── First prompt and result ─────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_Prompt_StartsOrchestratedTurnAndReturnsAtOnce()
	{
		var caller = _env.AddCaller();
		_env.Turns.Block = true;

		var text = TextOf(await _env.Create(Args("New", ("prompt", "Start here"), ("effort", "max")), caller));

		Assert.EndsWith(Constants.CreateSession.FirstTurnStarted, text);
		Assert.True(await _env.Turns.Started.WaitAsync(TimeSpan.FromSeconds(10)));
		var run = Assert.Single(_env.Turns.Runs);
		Assert.Same(_env.Named("New"), run.Node);
		Assert.Equal(("Start here", TurnSource.Orchestrated), (run.Prompt, run.Source));
		Assert.Null(run.Options);   // the session's own model, effort and profile apply
		Assert.Equal(0, _env.Turns.Finished);   // the tool answered while the turn still runs
		Assert.Empty(_env.Scheduler.Deliveries);
		_env.Turns.Release();
	}

	[Fact]
	public async Task CreateSession_NoPrompt_NoTurn()
	{
		var caller = _env.AddCaller();

		var text = TextOf(await _env.Create(Args("New", ("prompt", "  ")), caller));

		Assert.EndsWith(Constants.CreateSession.NoPromptRun, text);
		Assert.Empty(_env.Turns.Runs);
	}

	[Fact]
	public async Task CreateSession_ResultText_NamesPlaceAndSettings()
	{
		var caller = _env.AddCaller();

		var after = TextOf(await _env.Create(Args("Root level sessions", ("after", _env.Anchor.NodeId), ("model", Opus), ("effort", "max"),
			("profile", "default"), ("streaming", true)), caller));
		var created = _env.Named("Root level sessions");
		Assert.Equal(
			$"Session 'Root level sessions' created (nodeId={created.NodeId}) in {_env.Dir.Path} / UI, right after 'Anchor'.\n" +
			$"model={Opus}, effort=max, profile=default, streaming=on, sound=on.\n" +
			"No prompt run.",
			after);

		_env.Dir2.SelectedModelId = Haiku;
		var last = TextOf(await _env.Create(Args("Plain", ("workingDir", _env.Dir2.Path), ("sound", false)), caller));
		var plain = _env.Named("Plain");
		Assert.Equal(
			$"Session 'Plain' created (nodeId={plain.NodeId}) in {_env.Dir2.Path}, last.\n" +
			$"model={Haiku}, effort=default, profile=Account B, streaming=off, sound=off.\n" +
			"No prompt run.",
			last);
	}

	[Fact]
	public void OrchestrationSkill_AndNoWorkflowTool_NameCreateSession()
	{
		Assert.Contains("`mcp__cmx__create_session`", Constants.Instructions.NoWorkflowTool);
		Assert.Contains("## Creating a session for the user", Constants.Agent.OrchestrationSkillMarkdown);
		Assert.Contains("Use it only when the user asks for a new session.", Constants.Agent.OrchestrationSkillMarkdown);
	}

	// ── TC.ME.04 — the caller's tier follows the session's model rule (FR.16.6) ───

	[Fact]
	public async Task CallerTier_SessionDefaultModel_GivesDefaultTier()
	{
		var caller = _env.AddCaller(modelId: "default");
		_env.Dir.SelectedModelId = Opus;   // not used: "default" stops the fallback

		var error = await ErrorOf(() => _env.Create(Args("New", ("model", Opus)), caller));
		Assert.StartsWith("Tier violation: a Sonnet session cannot create a Opus session. Caller model: 'default'", error);

		await _env.Create(Args("Sonnet one", ("model", Sonnet)), caller);   // DefaultModelTier is Sonnet's
		Assert.Single(_env.Added);
	}

	[Fact]
	public async Task CallerTier_FollowsSessionThenDirectoryThenApp()
	{
		var caller = _env.AddCaller(modelId: null);

		_env.Settings.Settings.SelectedModelId = Haiku;
		Assert.StartsWith("Tier violation: a Haiku session", await ErrorOf(() => _env.Create(Args("A", ("model", Sonnet)), caller)));

		_env.Dir.SelectedModelId = Opus;
		await _env.Create(Args("B", ("model", Opus)), caller);

		caller.ModelId = Haiku;
		Assert.StartsWith("Tier violation: a Haiku session", await ErrorOf(() => _env.Create(Args("C", ("model", Sonnet)), caller)));

		Assert.Single(_env.Added);
	}

	// ── Review follow-ups (L6) ──────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_InheritedProfileSignedOut_Error()
	{
		_env.AddProfile("p3", "Logged out account", loggedIn: false);
		var caller = _env.AddCaller();
		caller.ProfileId   = "p3";
		caller.ProfileName = "Logged out account";

		var error = await ErrorOf(() => _env.Create(Args("New"), caller));

		Assert.StartsWith("Profile 'Logged out account' is signed out", error);
		Assert.Empty(_env.Added);
	}

	[Fact]
	public async Task CreateSession_RateCap_ParallelCalls_ExactlyTheCapSucceeds()
	{
		var caller = _env.AddCaller();

		var calls = Enumerable.Range(1, 2 * Constants.Agent.MaxCreatedSessionsPerHour).Select(async i =>
		{
			try
			{
				await Task.Run(() => _env.Create(Args($"P{i}"), caller));
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		});
		var results = await Task.WhenAll(calls);

		Assert.Equal(Constants.Agent.MaxCreatedSessionsPerHour, results.Count(ok => ok));
		Assert.Equal(Constants.Agent.MaxCreatedSessionsPerHour, _env.Added.Count);
	}
}
