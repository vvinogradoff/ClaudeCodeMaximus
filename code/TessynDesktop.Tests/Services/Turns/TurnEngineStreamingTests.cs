using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Engines;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// <see cref="TurnEngine"/> in Streaming Session Mode (FR.19) through the session-engine seam (FR.33.1): the process sink routes events to the
/// turn holding the lock or to a background run, the streaming OAuth-race retry, the retry state's lifetime, and the FR.19.13 headless redirect
/// into the live process.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineStreamingTests : IDisposable
{
	private static readonly string _race = "API Error: " + Constants.Auth.OAuthRefreshRaceMarker + ".";

	private readonly RecordingProgressSink _progress = new();
	private readonly TurnEngineHarness _h;

	public TurnEngineStreamingTests()
	{
		_h = new TurnEngineHarness(sinks: [_progress]);
	}

	public void Dispose() => _h.Dispose();

	private SessionEntryModel[] EntriesOf(string role) => _h.Files.Entries.Where(e => e.Role == role).ToArray();

	[Fact]
	public async Task A_streaming_turn_writes_its_assistant_entry_once_with_the_model_and_effort_stamp()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(Evt.Assistant("hello"), Evt.Result("s1"));

		var outcome = await _h.Run(node, "Hi").Within();

		var assistant = Assert.Single(EntriesOf(Constants.SessionFile.RoleAssistant));
		Assert.Equal("hello", assistant.Content);
		Assert.Equal("opus", assistant.ModelId);
		Assert.Equal("high", assistant.Effort);
		Assert.Single(_h.Streaming.EnsureCalls);
		Assert.Single(_h.Streaming.Sends);
		Assert.Null(outcome.ErrorText);
		Assert.Equal("s1", node.ClaudeSessionId);
	}

	[Fact]
	public async Task An_oauth_race_in_a_streaming_turn_is_retried_once_without_a_system_entry_or_an_error()
	{
		var node = TurnEngineHarness.Node(sessionId: "s0", streaming: true);
		_h.Streaming.ScriptEvents(Evt.ResultError(_race));
		_h.Streaming.ScriptEvents(Evt.Assistant("recovered"), Evt.Result("s0"));

		var outcome = await _h.Run(node, "Hi").Within();

		Assert.Equal(2, _h.Streaming.Sends.Count);
		Assert.Equal(_h.Streaming.Sends[0].Message, _h.Streaming.Sends[1].Message);
		Assert.Empty(EntriesOf(Constants.SessionFile.RoleSystem));
		Assert.Equal("recovered", Assert.Single(EntriesOf(Constants.SessionFile.RoleAssistant)).Content);
		var completion = Assert.Single(_h.Files.Completions);
		Assert.Null(completion.ErrorText);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task Events_after_the_turn_go_to_a_background_run_persisted_without_progress_or_stamp()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		var observer = new RecordingTurnObserver();
		_h.Streaming.ScriptEvents(Evt.Assistant("answer"), Evt.Result("s1"));
		await _h.Run(node, "Hi", observer).Within();
		var progressBefore = _progress.Items.Count;

		_h.Streaming.EmitToPrimary(Evt.Assistant("background note"));

		var background = EntriesOf(Constants.SessionFile.RoleAssistant).Last();
		Assert.Equal("background note", background.Content);
		Assert.Null(background.ModelId);
		Assert.Null(background.Effort);
		Assert.Equal(progressBefore, _progress.Items.Count);
		Assert.DoesNotContain(_progress.Items.OfType<TurnDeltaProgress>(), d => d.Text == "background note");
	}

	[Fact]
	public async Task The_retry_state_lasts_until_the_turn_ends_so_a_stdout_error_while_the_lock_is_held_stays_suppressed()
	{
		var node = TurnEngineHarness.Node(sessionId: "old", streaming: true);
		var observer = new RecordingTurnObserver
		{
			// After the reply, still holding the turn lock (where Auto-Compact runs), the live process reports an error on stdout.
			AfterResponse = () =>
			{
				_h.Streaming.EmitToPrimary(Evt.SystemError("echo while locked"));
				return Task.CompletedTask;
			},
		};
		_h.Streaming.ScriptEvents(Evt.ResultError("No conversation found with session ID old"));

		await _h.Run(node, "Hi", observer).Within();

		Assert.DoesNotContain(EntriesOf(Constants.SessionFile.RoleSystem), e => e.Content == "echo while locked");

		// Once the turn has ended, the same output is background output with fresh state and is written.
		_h.Streaming.EmitToPrimary(Evt.SystemError("after the turn"));
		Assert.Contains(EntriesOf(Constants.SessionFile.RoleSystem), e => e.Content == "after the turn");
	}

	[Fact]
	public async Task A_headless_turn_into_the_live_process_writes_the_transcript_once_and_returns_text_and_tokens()
	{
		var live     = new LiveProcessStreamingManager();
		var router   = TestEngines.Claude(live, _h.Process, _h.Files, _h.Settings, _h.Status, new Lazy<IAgentMcpServer>(() => _h.Mcp));
		var profiles = new FakeProfileServiceForTurnService();
		var turns    = new SessionTurnService(router, _h.Files, _h.Settings, profiles, new FakeRegistryForTurnService(),
			new FakeModelServiceForTurnService(), new Lazy<ITelegramBridgeService>(() => _h.Telegram), _h.Attention.Service, _h.Tags.Service,
			new ProfileLabelService(profiles, _h.Settings));
		var engine   = new TurnEngine(_h.Files, _h.Registry, turns, router, _h.Settings, new Lazy<ITelegramBridgeService>(() => _h.Telegram),
			_h.Attention.Service, _h.Tags.Service);
		var node     = TurnEngineHarness.Node(streaming: true);

		// The interactive turn starts the persistent process and binds its primary sink.
		live.ScriptTurn(Evt.Assistant("first answer"), Evt.Result("s1"));
		await engine.RunAsync(new TurnRequest(node, "Hi", TurnEngineHarness.Chosen())).Within();

		live.ScriptTurn(Evt.Assistant("headless answer"), Evt.Result("s1", inputTokens: 5, outputTokens: 7, cost: 0.25));
		var result = await turns.RunTurnAsync(node, "do it", TurnSource.Scheduled).Within();

		Assert.Equal(1, live.EnsureCalls);
		Assert.Equal(2, live.Sends.Count);
		Assert.Empty(_h.Process.Calls);
		Assert.Equal(["first answer", "headless answer"], EntriesOf(Constants.SessionFile.RoleAssistant).Select(e => e.Content).ToArray());
		Assert.Equal(["Hi", "do it"], EntriesOf(Constants.SessionFile.RoleUser).Select(e => e.Content).ToArray());
		Assert.False(result.IsError);
		Assert.Equal("headless answer", result.AssistantText);
		Assert.Equal(5, result.InputTokens);
		Assert.Equal(7, result.OutputTokens);
		Assert.Equal(0.25, result.CostUsd);
		Assert.Equal("s1", result.SessionId);
	}
}
