using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The five majors of the phase 2b/3 review (<c>.codex-work/phase2b3-review.md</c>): M1 the engine change under the turn lock, M2 a queued
/// ChatGPT switch, M3 a profile deleted during a turn, M4 profile id reuse, M5 a local <c>gpt-oss</c> model. Each test fails without its fix.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ReviewFixMajorTests : IAsyncLifetime
{
	private readonly CodexFullHarness _h = new();
	private FakeTurnStart? _held;

	public Task InitializeAsync() => _h.InitializeAsync(secondProfile: true);

	public async Task DisposeAsync() => await _h.DisposeAsync();

	/// <summary>The first turn of any host streams and then waits for <see cref="FinishHeldTurnAsync"/>; later turns answer at once.</summary>
	private void HoldFirstTurn()
	{
		Func<FakeTurnStart, FakeCodexScripts, System.Collections.Generic.List<FakeCodexStep>> script = (start, scripts) =>
		{
			if (_held is null)
			{
				_held = start;
				return scripts.StreamingThenHang(start.ThreadId, start.TurnId, "Working");
			}
			return scripts.TextTurn(start.ThreadId, start.TurnId, "Second answer.");
		};
		_h.S.Launcher.Configure = server => server.TurnScript = start => script(start, server.Scripts);
		foreach (var server in _h.S.Launcher.Servers)
			server.TurnScript = start => script(start, server.Scripts);
	}

	/// <summary>Completes the held turn normally: its agentMessage, then turn/completed.</summary>
	private async Task FinishHeldTurnAsync(string? codexHome)
	{
		var server  = _h.S.ServerOf(codexHome);
		var scripts = server.Scripts;
		await server.NotifyAsync("item/completed", new JsonObject
		{
			["item"] = scripts.AgentMessage("msg-long", "Working done."), ["threadId"] = _held!.ThreadId, ["turnId"] = _held.TurnId,
		});
		await server.NotifyAsync("turn/completed", new JsonObject { ["threadId"] = _held.ThreadId, ["turn"] = scripts.Turn(_held.TurnId, "completed") });
	}

	private Task WaitForHeldTurnAsync() => _h.S.Eventually.TrueAsync(() => _held is not null, "the first turn started");

	[Fact]
	public async Task M1_a_Claude_send_queued_behind_a_running_Codex_turn_changes_nothing_until_that_turn_has_ended()
	{
		var node = _h.Node("gpt-x-sol");
		HoldFirstTurn();
		var first = _h.RunAsync(node, "Long GPT job");
		await WaitForHeldTurnAsync();
		var thread = node.CodexThreadId;

		_h.H.Process.ScriptEvents(Evt.Assistant("Claude answer."), Evt.Result("claude-1"));
		var second = _h.RunAsync(node, "Now on Claude", new TurnOptionsModel(FixedClaudeModels.Opus, null, null));
		await _h.S.Eventually.TrueAsync(() => _h.H.Files.Entries.Any(e => e.Content == "Now on Claude"), "the second message was written");
		await Task.Delay(200);

		// While the Codex turn runs, its thread, engine and host stay as they were.
		Assert.Equal(("codex", thread), (node.EngineId, node.CodexThreadId));
		Assert.Empty(_h.Requests("thread/unsubscribe"));
		Assert.DoesNotContain(_h.H.Files.Entries, e => e.Content.StartsWith("Engine changed", StringComparison.Ordinal));
		Assert.Empty(_h.H.Process.Calls);

		await FinishHeldTurnAsync(_h.HomeOf("codex_1"));
		var firstOutcome  = await first.WaitAsync(TimeSpan.FromSeconds(10));
		var secondOutcome = await second.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Null(firstOutcome.ErrorText);
		Assert.Null(secondOutcome.ErrorText);
		Assert.Contains(_h.H.Files.Entries, e => e.Role == "ASSISTANT" && e.Content == "Working done.");
		Assert.Null(node.EngineId);
		Assert.Equal([thread!], node.PriorCodexThreadIds);
		Assert.Single(_h.H.Process.Calls);
	}

	[Fact]
	public async Task M1_a_GPT_send_queued_behind_a_running_Claude_streaming_turn_does_not_kill_its_process()
	{
		var node = _h.Node(FixedClaudeModels.Opus);
		node.IsStreamingMode = true;
		var gate = new TaskCompletionSource();
		_h.H.Streaming.Script(async (primary, _) =>
		{
			await gate.Task;
			primary(Evt.Assistant("Claude streamed."));
			primary(Evt.Result("claude-s1"));
		});
		var first = _h.RunAsync(node, "Long Claude job");
		await _h.S.Eventually.TrueAsync(() => _h.H.Streaming.Sends.Count == 1, "the streaming turn started");

		var second = _h.RunAsync(node, "Now on GPT", new TurnOptionsModel("gpt-x-sol", null, null));
		await _h.S.Eventually.TrueAsync(() => _h.H.Files.Entries.Any(e => e.Content == "Now on GPT"), "the second message was written");
		await Task.Delay(200);

		Assert.Empty(_h.H.Streaming.Stopped);
		Assert.Null(node.EngineId);

		gate.SetResult();
		Assert.Null((await first.WaitAsync(TimeSpan.FromSeconds(10))).ErrorText);
		Assert.Null((await second.WaitAsync(TimeSpan.FromSeconds(10))).ErrorText);
		Assert.Contains(_h.H.Files.Entries, e => e.Role == "ASSISTANT" && e.Content == "Claude streamed.");
		Assert.Equal([node.NodeId], _h.H.Streaming.Stopped); // stopped by the engine change, after the Claude turn ended
		Assert.Equal("codex", node.EngineId);
	}

	[Fact]
	public async Task M2_a_ChatGPT_switch_queued_during_a_turn_is_applied_by_the_next_send_which_is_seeded_from_the_transcript()
	{
		var node = _h.Node("gpt-x-sol");
		var switcher = new ProfileSwitchService(_h.H.Settings, Registry(node), _h.H.Files, new ProfileResolver(_h.H.Settings, new FakeProfileServiceForTurnService()),
			null!, _h.Router, new Lazy<ISessionTurnService>(() => _h.H.Turns), _h.S.Resolver);
		var pipeline = _h.NewInteractive(switcher);
		HoldFirstTurn();
		var first = _h.RunAsync(node, "Remember PERIWINKLE", via: pipeline);
		await WaitForHeldTurnAsync();

		var switched = switcher.Switch(node.NodeId, "codex_2", includeDescendants: false);
		Assert.False(switched.NodeMoved); // queued: the session is running
		Assert.Equal("codex_2", node.PendingProfileId);

		var second = _h.RunAsync(node, "What was the word?", via: pipeline);
		await FinishHeldTurnAsync(_h.HomeOf("codex_1"));
		await first.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Null((await second.WaitAsync(TimeSpan.FromSeconds(10))).ErrorText);

		Assert.Equal(("codex_2", (string?)null), (node.ProfileId, node.PendingProfileId));
		var codex2 = _h.S.ServerOf(_h.HomeOf("codex_2"));
		Assert.Single(codex2.RequestsOf("thread/start"));
		var input = codex2.RequestsOf("turn/start").Single().Params.GetProperty("input")[0].GetProperty("text").GetString()!;
		Assert.StartsWith("The following is the conversation history", input);
		Assert.Contains("[Human]: Remember PERIWINKLE", input);
		Assert.Equal("ChatGPT · w@example.com", _h.H.Files.Entries.Last(e => e.Role == "USER").ProfileName);
	}

	[Fact]
	public async Task M3_deleting_the_profile_of_a_running_turn_ends_it_with_the_profile_removed_error()
	{
		var node = _h.Node("gpt-x-sol", profileId: "codex_2");
		node.ProfileName = "ChatGPT · w@example.com";
		HoldFirstTurn();
		var turn = _h.RunAsync(node, "Work on codex_2");
		await WaitForHeldTurnAsync();

		await _h.S.Profiles.DeleteAsync("codex_2").WaitAsync(TimeSpan.FromSeconds(10));
		var outcome = await turn.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Equal("Codex stopped: the profile was removed.", outcome.ErrorText);
		Assert.Equal(("SYSTEM", "Codex stopped: the profile was removed."), (_h.H.Files.Entries[^1].Role, _h.H.Files.Entries[^1].Content));
		Assert.False(_h.Interactive.IsBusy(node.NodeId));
	}

	[Fact]
	public async Task M4_a_deleted_profile_s_id_is_never_given_to_a_new_sign_in_and_its_pinned_session_fails_with_the_missing_pin_error()
	{
		var pinned = new SessionNodeModel { Name = "Old", FileName = "old.txt", ProfileId = "codex_2", ProfileName = "ChatGPT · w@example.com" };

		await _h.S.Profiles.DeleteAsync("codex_2");
		var started = await _h.S.Profiles.BeginSignInAsync(deviceCode: false);

		Assert.Equal("codex_3", started.ProfileId);
		var choice = _h.S.Resolver.ResolveForTurn(pinned);
		Assert.False(choice.IsResolved);
		Assert.Equal("Session 'Old' is pinned to ChatGPT profile 'ChatGPT · w@example.com', which no longer exists. It does not fall back to another profile.", choice.Error);
	}

	[Fact]
	public async Task M4_the_number_of_a_removed_folder_is_skipped_even_without_the_settings_record()
	{
		Directory.CreateDirectory(Path.Combine(_h.S.StoredRoot, "codex_7_rem2"));

		var started = await _h.S.Profiles.BeginSignInAsync(deviceCode: false);

		Assert.Equal("codex_8", started.ProfileId);
	}

	[Fact]
	public async Task M4_two_sign_ins_at_the_same_time_get_different_ids()
	{
		var both = await Task.WhenAll(_h.S.Profiles.BeginSignInAsync(deviceCode: false), _h.S.Profiles.BeginSignInAsync(deviceCode: true));

		Assert.NotEqual(both[0].ProfileId, both[1].ProfileId);
		Assert.Equal(["codex_3", "codex_4"], both.Select(b => b.ProfileId).Order());
	}

	[Fact]
	public async Task M5_a_local_gpt_oss_model_is_a_Claude_engine_model_with_the_local_tier_and_is_never_refused()
	{
		_h.S.Claude.Models = [.. _h.S.Claude.Models, new ClaudeModelInfo("gpt-oss:20b", string.Empty, "GPT-OSS 20B", ModelProvider.Ollama)];
		var node = new SessionNodeModel { Name = "S", FileName = "s.txt" };

		Assert.Equal("claude", _h.S.Selector.SelectEngineId(node, "gpt-oss:20b"));
		Assert.False(_h.S.Models.LooksLikeGpt("gpt-oss:20b"));
		Assert.False(_h.S.Models.IsGpt("gpt-oss:20b"));
		Assert.Equal(0, _h.S.Tiers.GetTier("gpt-oss:20b"));
		Assert.True(_h.S.Models.LooksLikeGpt("gpt-oss:120b")); // not in any catalog: the GPT-looking rule still applies

		await using var env = await GptOrchestrationTestEnv.CreateAsync();
		env.S.Claude.Models = [.. env.S.Claude.Models, new ClaudeModelInfo("gpt-oss:20b", string.Empty, "GPT-OSS 20B", ModelProvider.Ollama)];
		var sup = env.AddClaudeSupervisor("Haiku sup", FixedClaudeModels.Haiku);
		await env.Call("spawn_session", new JsonObject { ["name"] = "Local", ["model"] = "gpt-oss:20b" }, sup);
		var local = env.Named("Local");
		Assert.Equal(("gpt-oss:20b", (string?)null, "p2"), (local.ModelId, local.EngineId, local.ProfileId));
	}

	private FakeSessionRegistry Registry(SessionNodeModel node)
	{
		var registry = new FakeSessionRegistry();
		registry.AddNode(node);
		return registry;
	}
}
