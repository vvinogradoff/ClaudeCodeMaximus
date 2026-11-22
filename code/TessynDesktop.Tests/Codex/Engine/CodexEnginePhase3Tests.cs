using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// Phase 3 inside the pipelines: the engine stored from the model (TC.CX.01), effort per model (TC.CX.05), limits (TC.CX.20), auth (TC.CX.21),
/// the context-window hint (TC.CX.22), the ChatGPT pin and a profile switch (TC.CX.32), and the idle unload of a thread (FR.33.6).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexEnginePhase3Tests : IAsyncLifetime
{
	private readonly CodexFullHarness _h = new();

	public Task InitializeAsync() => _h.InitializeAsync(secondProfile: true);

	public async Task DisposeAsync() => await _h.DisposeAsync();

	/// <summary>The turn script of every fake host, the running ones (codex_1 already runs for the catalog) and those launched later.</summary>
	private void Script(Func<Fakes.FakeTurnStart, Fakes.FakeCodexScripts, System.Collections.Generic.List<Fakes.FakeCodexStep>> script)
	{
		_h.S.Launcher.Configure = server => server.TurnScript = start => script(start, server.Scripts);
		foreach (var server in _h.S.Launcher.Servers)
			server.TurnScript = start => script(start, server.Scripts);
	}

	[Fact]
	public async Task TC_CX_01_the_engine_is_stored_when_the_turn_starts_and_follows_the_model_back_to_Claude()
	{
		var node = _h.Node("gpt-x-sol");

		await _h.RunAsync(node);
		Assert.Equal("codex", node.EngineId);
		Assert.NotNull(node.CodexThreadId);
		Assert.Empty(_h.H.Process.Calls);

		node.ModelId = FixedClaudeModels.Opus;
		_h.H.Process.ScriptEvents(Evt.Assistant("Claude again."), Evt.Result("claude-1"));
		await _h.RunAsync(node);

		Assert.Null(node.EngineId);
		Assert.Single(_h.H.Process.Calls);
		Assert.Contains(_h.H.Files.Entries, e => e.Role == "SYSTEM" && e.Content.StartsWith("Engine changed: Codex (GPT) -> Claude.", StringComparison.Ordinal));
	}

	[Fact]
	public async Task A_GPT_turn_pins_the_session_to_its_ChatGPT_profile_and_stamps_it_on_the_USER_entry()
	{
		var node = _h.Node("gpt-x-sol");

		await _h.RunAsync(node);

		Assert.Equal(("codex_1", "ChatGPT · u@example.com"), (node.ProfileId, node.ProfileName));
		Assert.Equal("ChatGPT · u@example.com", _h.H.Files.Entries.First(e => e.Role == "USER").ProfileName);
		Assert.Equal(_h.HomeOf("codex_1"), _h.S.Launcher.Launches.Last().CodexHome);
	}

	[Fact]
	public async Task TC_CX_05_a_stored_effort_the_GPT_model_lacks_is_sent_as_none_and_stays_stored()
	{
		var node = _h.Node("gpt-x-luna", effort: "high");

		await _h.RunAsync(node);
		node.ModelId = "gpt-x-sol";
		await _h.RunAsync(node);

		var turns = _h.Requests("turn/start");
		Assert.False(turns[0].TryGetProperty("effort", out _));
		Assert.Equal("gpt-x-luna", turns[0].GetProperty("model").GetString());
		Assert.Equal("high", turns[1].GetProperty("effort").GetString());
		Assert.Equal("high", node.Effort);
	}

	[Fact]
	public async Task TC_CX_20_a_usage_limit_sets_the_limits_error_with_the_reset_time_and_the_next_successful_turn_clears_it()
	{
		var node = _h.Node();
		await _h.RunAsync(node);
		await _h.S.ServerOf(_h.HomeOf("codex_1")).NotifyAsync("account/rateLimits/updated", new JsonObject
		{
			["rateLimits"] = new JsonObject
			{
				["limitId"] = "codex", ["rateLimitReachedType"] = "rateLimitReached",
				["primary"] = new JsonObject { ["usedPercent"] = 100, ["windowDurationMins"] = 300, ["resetsAt"] = _h.S.Clock.GetUtcNow().AddHours(2).ToUnixTimeSeconds() },
			},
		});
		await _h.S.Eventually.TrueAsync(() => _h.S.Usage.GetReading("codex_1") is not null, "the reading arrived");
		Script((start, scripts) => scripts.FailedTurn(start.ThreadId, start.TurnId, "usageLimitExceeded", "You've hit your usage limit."));

		var failed = await _h.RunAsync(node);

		var resets = _h.S.Clock.GetUtcNow().AddHours(2).ToLocalTime().ToString("ddd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
		Assert.Equal($"Codex error: You've hit your usage limit. (usageLimitExceeded) (resets {resets})", failed.ErrorText);
		Assert.NotNull(_h.H.AuthErrors.GetLimitError(node.NodeId));
		Assert.Contains($"resets {resets}", _h.H.AuthErrors.GetLimitError(node.NodeId));

		Script((start, scripts) => scripts.TextTurn(start.ThreadId, start.TurnId, "Back."));
		await _h.RunAsync(node);
		Assert.Null(_h.H.AuthErrors.GetLimitError(node.NodeId));
	}

	[Fact]
	public async Task TC_CX_20_a_turn_whose_text_only_mentions_a_usage_limit_sets_nothing()
	{
		var node = _h.Node();
		Script((start, scripts) => scripts.TextTurn(start.ThreadId, start.TurnId, "You've hit your usage limit. Upgrade to Plus to continue."));

		var outcome = await _h.RunAsync(node);

		Assert.Null(outcome.ErrorText);
		Assert.Null(_h.H.AuthErrors.GetLimitError(node.NodeId));
		Assert.False(_h.H.AuthErrors.Has(node.NodeId));
	}

	[Fact]
	public async Task TC_CX_21_unauthorized_sets_the_session_s_and_the_profile_s_auth_error()
	{
		var node = _h.Node();
		Script((start, scripts) => scripts.FailedTurn(start.ThreadId, start.TurnId, "unauthorized", "Your access token could not be refreshed."));

		await _h.RunAsync(node);

		Assert.True(_h.H.AuthErrors.Has(node.NodeId));
		Assert.True(_h.S.Profiles.HasAuthError("codex_1"));
	}

	[Fact]
	public async Task TC_CX_21_a_sign_out_during_a_turn_sets_the_auth_error_of_the_sessions_running_on_the_profile()
	{
		var node = _h.Node();
		Script((start, scripts) => scripts.StreamingThenHang(start.ThreadId, start.TurnId, "thinking"));
		var turn = _h.RunAsync(node);
		await _h.S.Eventually.TrueAsync(() => _h.Requests("turn/start").Length == 1, "the turn started");

		await _h.S.ServerOf(_h.HomeOf("codex_1")).NotifyAsync("account/updated", new JsonObject { ["authMode"] = null, ["planType"] = null, ["reason"] = null });

		await _h.S.Eventually.TrueAsync(() => _h.H.AuthErrors.Has(node.NodeId), "the running session got the auth error");
		Assert.True(_h.S.Profiles.HasAuthError("codex_1"));
		_h.Interactive.CancelRuns(node.NodeId);
		await turn;
	}

	[Fact]
	public async Task TC_CX_22_context_window_exceeded_writes_the_compact_hint_and_does_not_retry()
	{
		var node = _h.Node();
		Script((start, scripts) => scripts.FailedTurn(start.ThreadId, start.TurnId, "contextWindowExceeded", "too long"));

		var outcome = await _h.RunAsync(node);

		Assert.Equal(Constants.Codex.Texts.ContextWindowExceeded, outcome.ErrorText);
		Assert.Single(_h.Requests("turn/start"));
	}

	[Fact]
	public async Task TC_CX_32_a_switch_from_codex_1_to_codex_2_starts_a_new_thread_on_codex_2_seeded_from_the_transcript()
	{
		var node = _h.Node();
		await _h.RunAsync(node, "Remember PERIWINKLE");
		var firstThread = node.CodexThreadId!;
		var registry = new FakeSessionRegistry();
		registry.AddNode(node);
		var switcher = new ProfileSwitchService(_h.H.Settings, registry, _h.H.Files, new ProfileResolver(_h.H.Settings, new FakeProfileServiceForTurnService()),
			null!, _h.Router, new Lazy<ISessionTurnService>(() => _h.H.Turns), _h.S.Resolver);

		var result = switcher.Switch(node.NodeId, "codex_2", includeDescendants: false);
		await _h.RunAsync(node, "What was the word?");

		Assert.True(result.NodeMoved);
		Assert.Equal(("codex_2", "ChatGPT · w@example.com"), (node.ProfileId, node.ProfileName));
		Assert.Equal([firstThread], node.PriorCodexThreadIds);
		var secondHost = _h.S.ServerOf(_h.HomeOf("codex_2"));
		Assert.Single(secondHost.RequestsOf("thread/start")); // the new thread is on codex_2 (each fake host numbers its threads from 1)
		var input = secondHost.RequestsOf("turn/start").Single().Params.GetProperty("input")[0].GetProperty("text").GetString()!;
		Assert.StartsWith("The following is the conversation history", input);
		Assert.Contains("[Human]: Remember PERIWINKLE", input);
		Assert.Empty(Directory.EnumerateFiles(_h.HomeOf("codex_2")!, "*.jsonl", SearchOption.AllDirectories)); // no rollout file is moved
	}

	[Fact]
	public async Task TC_CX_32_a_per_turn_pick_of_another_ChatGPT_profile_reseeds_on_it()
	{
		var node = _h.Node();
		await _h.RunAsync(node, "First");
		var firstThread = node.CodexThreadId;

		await _h.RunAsync(node, "Second", new TurnOptionsModel(null, null, "ChatGPT · w@example.com"));

		Assert.Equal("codex_2", node.ProfileId);
		Assert.Equal([firstThread!], node.PriorCodexThreadIds);
		Assert.Single(_h.S.ServerOf(_h.HomeOf("codex_2")).RequestsOf("thread/start"));
		Assert.StartsWith("The following is the conversation history",
			_h.S.ServerOf(_h.HomeOf("codex_2")).RequestsOf("turn/start").Single().Params.GetProperty("input")[0].GetProperty("text").GetString());
	}

	[Fact]
	public async Task A_Claude_profile_picked_for_a_GPT_turn_is_the_cross_engine_error()
	{
		var node = _h.Node();

		var error = await Assert.ThrowsAsync<TdcpException>(() => _h.RunAsync(node, options: new TurnOptionsModel(null, null, "Account A")));

		Assert.StartsWith("Profile 'Account A' is a Claude profile; GPT models need a ChatGPT profile:", error.Message);
	}

	[Fact]
	public async Task FR_33_6_a_thread_with_no_turn_for_15_minutes_is_unloaded_and_resumed_on_its_next_turn()
	{
		var node = _h.Node();
		await _h.RunAsync(node);
		var thread = node.CodexThreadId!;
		var host = _h.S.Pool.Hosts.Single(h => h.ProfileId == "codex_1");
		Assert.True(host.IsThreadLoaded(thread));

		_h.EngineClock.Advance(TimeSpan.FromMinutes(14));
		await Task.Delay(50);
		Assert.True(host.IsThreadLoaded(thread));
		_h.EngineClock.Advance(TimeSpan.FromMinutes(1));
		await _h.S.Eventually.TrueAsync(() => !host.IsThreadLoaded(thread), "the idle thread was unloaded");
		Assert.Equal(thread, _h.Requests("thread/unsubscribe").Single().GetProperty("threadId").GetString());

		await _h.RunAsync(node);
		Assert.Single(_h.Requests("thread/resume"));
		Assert.Equal(thread, node.CodexThreadId);
	}

	[Fact]
	public async Task A_headless_GPT_turn_runs_on_the_resolved_ChatGPT_profile_and_refuses_when_there_is_none()
	{
		var node = _h.Node();
		var result = await _h.Headless.RunTurnAsync(node, "Hi", TurnSource.Orchestrated).WaitAsync(TimeSpan.FromSeconds(20));
		Assert.False(result.IsError);
		Assert.Equal("codex_1", node.ProfileId);

		var orphan = TurnEngineHarness.Node();
		orphan.NodeId = "orphan";
		orphan.ModelId = "gpt-x-sol";
		orphan.WorkingDirectory = @"C:\Elsewhere";
		_h.S.Profiles.SetAuthError("codex_1", true);
		_h.S.Profiles.SetAuthError("codex_2", true);
		var refused = await _h.Headless.RunTurnAsync(orphan, "Hi", TurnSource.Orchestrated).WaitAsync(TimeSpan.FromSeconds(20));
		Assert.True(refused.Refused);
		Assert.Equal("No signed-in ChatGPT profile. Sign in under Settings > Profiles.", refused.ErrorMessage);
	}
}
