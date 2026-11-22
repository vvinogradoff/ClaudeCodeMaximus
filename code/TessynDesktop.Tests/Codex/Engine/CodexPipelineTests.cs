using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The Codex engine inside the real pipelines: a full interactive turn through <see cref="TurnEngine"/>, the router and the fake app-server
/// (transcript, <c>turn.*</c> progress, observer); the engine change both ways (TC.CX.31); native compaction (TC.CX.33); reset conversation
/// (FR.33.12); the headless worker result (TC.CX.15); and a mailbox delivery to a Codex supervisor (TC.CX.30).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexPipelineTests : IAsyncLifetime
{
	private static readonly string EngineChangedToCodex = "Engine changed: Claude -> Codex (GPT). The conversation continues from the transcript.";
	private static readonly string EngineChangedToClaude = "Engine changed: Codex (GPT) -> Claude. The conversation continues from the transcript.";

	private readonly CodexPipelineHarness _p = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _p.DisposeAsync();

	/// <summary>Reset conversation does not resolve turn settings.</summary>
	private sealed class UnusedSettingsResolver : ITurnSettingsResolver
	{
		public Task<TurnSettings> ResolveAsync(SessionNodeModel node, TurnOptionsModel? options) => throw new NotSupportedException();
	}

	[Fact]
	public async Task A_full_interactive_Codex_turn_writes_the_transcript_reports_turn_progress_and_tells_the_observer()
	{
		var node     = _p.CodexNode();
		var observer = new RecordingTurnObserver();
		_p.Launcher.Configure = server => server.TurnScript = start => server.Scripts.TextTurn(start.ThreadId, start.TurnId, "Checking.", "Done: all green.");

		var outcome = await _p.RunInteractiveAsync(node, "Run the checks", CodexPipelineHarness.Gpt(), observer);

		Assert.False(outcome.Cancelled);
		Assert.Null(outcome.ErrorText);

		var entries = _p.H.Files.Entries;
		Assert.Equal(["USER", "ASSISTANT", "ASSISTANT"], entries.Select(e => e.Role));
		Assert.Equal("Run the checks", entries[0].Content);
		Assert.Equal("ChatGPT", entries[0].ProfileName);
		Assert.Equal(("Checking.", "gpt-6.2-sol", "low"), (entries[1].Content, entries[1].ModelId, entries[1].Effort));
		Assert.Equal(("Done: all green.", (string?)null, (string?)null), (entries[2].Content, entries[2].ModelId, entries[2].Effort)); // FR.2.3.5: the first entry only

		Assert.Equal(
		[
			"user:ChatGPT",
			"assistant:Checking.",
			"assistant:Done: all green.",
			"authError:False",
			"sessionId",
			"resultEnded",
			"afterResponse",
			"beforeIdle",
			"idle",
		], observer.Snapshot());

		var lines = _p.Progress.Lines();
		Assert.Equal("started:-", lines[0]);
		Assert.Equal("status:Running", lines[1]);
		Assert.Contains("delta:Text:Checking.", lines);
		Assert.Contains("usage:15600/50/0", lines);
		Assert.Equal("usage:31201/101/0", lines[^2]);
		Assert.StartsWith("finished:", lines[^1]);

		var input = CodexEngineFixture.InputText(_p.Requests("turn/start").Single());
		Assert.StartsWith("Run the checks" + Constants.Instructions.Delimiter, input);
		Assert.Contains(Constants.Instructions.Codex.Shell, input);
		Assert.DoesNotContain("ScheduleWakeup", input);
		Assert.Equal(node.CodexThreadId, _p.Requests("turn/start").Single().GetProperty("threadId").GetString());
		Assert.Empty(_p.H.Process.Calls);
		Assert.Single(_p.H.Files.Completions);
	}

	[Fact]
	public async Task A_failed_Codex_turn_writes_its_SYSTEM_entry_and_reports_the_error_with_turn_completed()
	{
		var node = _p.CodexNode();
		_p.Launcher.Configure = server => server.TurnScript = start => server.Scripts.FailedTurn(start.ThreadId, start.TurnId, "modelNotFound", "The model is not supported.");

		var outcome = await _p.RunInteractiveAsync(node, "Hi", CodexPipelineHarness.Gpt("gpt-does-not-exist"));

		Assert.Equal("Codex error: The model is not supported. (modelNotFound)", outcome.ErrorText);
		Assert.Equal(("SYSTEM", "Codex error: The model is not supported. (modelNotFound)"), (_p.H.Files.Entries[^1].Role, _p.H.Files.Entries[^1].Content));
	}

	[Fact]
	public async Task A_Stop_of_an_interactive_Codex_turn_writes_the_partial_answer_before_the_interrupted_line()
	{
		var node = _p.CodexNode();
		_p.Launcher.Configure = server => server.TurnScript = start => server.Scripts.StreamingThenHang(start.ThreadId, start.TurnId, "Half an ", "answer");

		var turn = _p.RunInteractiveAsync(node, "Write a lot", CodexPipelineHarness.Gpt());
		await Waits.Until(() => _p.Launcher.Servers.Count > 0 && _p.Server.RequestsOf("turn/start").Count > 0);
		await Task.Delay(200);
		Assert.True(_p.Interactive.CancelRuns(node.NodeId));
		var outcome = await turn;

		Assert.True(outcome.Cancelled);
		var tail = _p.H.Files.Entries.Skip(1).Select(e => (e.Role, e.Content)).ToList();
		Assert.Equal([("ASSISTANT", "Half an answer\n[interrupted]"), ("SYSTEM", Constants.Turns.InterruptedByUserText)], tail);
		Assert.Single(_p.Requests("turn/interrupt"));
	}

	[Fact]
	public async Task TC_CX_31_a_Claude_session_moved_to_Codex_detaches_the_Claude_id_and_seeds_the_thread_from_the_transcript()
	{
		var node = _p.CodexNode(claudeSessionId: "claude-sess-1");
		_p.H.Files.Seed("USER", "Plan the release");
		_p.H.Files.Seed("ASSISTANT", "Friday, after the freeze.");

		await _p.RunInteractiveAsync(node, "Go on", CodexPipelineHarness.Gpt());

		Assert.Null(node.ClaudeSessionId);
		Assert.Equal(["claude-sess-1"], node.PriorClaudeSessionIds);
		Assert.NotNull(node.CodexThreadId);
		var written = _p.H.Files.Entries.Skip(2).Select(e => (e.Role, e.Content)).Take(2).ToList();
		Assert.Equal([("SYSTEM", EngineChangedToCodex), ("USER", "Go on")], written);
		var input = CodexEngineFixture.InputText(_p.Requests("turn/start").Single());
		Assert.StartsWith("The following is the conversation history", input);
		Assert.Contains("[Human]: Plan the release", input);
		Assert.Contains("[Assistant]: Friday, after the freeze.", input);
		Assert.Contains(Constants.Instructions.Codex.Shell, input); // a new thread gets the session-start lines
		Assert.Empty(_p.H.Process.Calls);
	}

	[Fact]
	public async Task TC_CX_31_a_Codex_session_moved_back_to_Claude_detaches_the_thread_and_seeds_Claude_from_the_transcript()
	{
		var node = _p.CodexNode();
		await _p.RunInteractiveAsync(node, "First on GPT", CodexPipelineHarness.Gpt());
		var threadId = node.CodexThreadId!;

		node.EngineId = null;
		_p.H.Process.ScriptEvents(Evt.Assistant("Claude here."), Evt.Result("claude-sess-2"));
		await _p.RunInteractiveAsync(node, "Now on Claude", TurnEngineHarness.Chosen());

		Assert.Null(node.CodexThreadId);
		Assert.Equal([threadId], node.PriorCodexThreadIds);
		Assert.Equal("claude-sess-2", node.ClaudeSessionId);
		Assert.Contains(_p.H.Files.Entries, e => e.Role == "SYSTEM" && e.Content == EngineChangedToClaude);
		var call = _p.H.Process.Calls.Single();
		Assert.Null(call.SessionId);
		Assert.StartsWith("The following is the conversation history", call.Message);
		Assert.Contains("[Human]: First on GPT", call.Message);
		await Waits.Until(() => _p.Requests("thread/unsubscribe").Length == 1); // the host forgets the detached thread
	}

	[Fact]
	public async Task A_turn_on_the_same_engine_writes_no_engine_change()
	{
		var node = _p.CodexNode();
		await _p.RunInteractiveAsync(node, "one", CodexPipelineHarness.Gpt());
		await _p.RunInteractiveAsync(node, "two", CodexPipelineHarness.Gpt());

		Assert.DoesNotContain(_p.H.Files.Entries, e => e.Content.StartsWith("Engine changed", StringComparison.Ordinal));
		Assert.Null(node.PriorCodexThreadIds);
	}

	[Fact]
	public async Task TC_CX_33_Compact_now_on_a_Codex_session_calls_thread_compact_start_writes_the_separator_and_keeps_the_thread()
	{
		var node = _p.CodexNode();
		await _p.RunInteractiveAsync(node, "one", CodexPipelineHarness.Gpt());
		var threadId = node.CodexThreadId;

		var result = await _p.Compaction.CompactAsync(node, new SessionCompactionOptionsModel("gpt-6.2-sol", "low", null, null)).WaitAsync(TimeSpan.FromSeconds(20));

		Assert.Equal(SessionCompactionOutcome.Compacted, result.Outcome);
		Assert.False(result.SessionDetached);
		Assert.Equal(threadId, Assert.Single(_p.Requests("thread/compact/start")).GetProperty("threadId").GetString());
		Assert.Equal(1, _p.H.Files.CompactionSeparators);
		Assert.Equal(threadId, node.CodexThreadId);
		Assert.Empty(_p.H.Process.Calls); // no Claude process
	}

	[Fact]
	public async Task TC_CX_33_Auto_Compact_after_a_Codex_turn_compacts_natively_under_the_turn_lock()
	{
		var node     = _p.CodexNode();
		var observer = new RecordingTurnObserver();

		await _p.RunInteractiveAsync(node, "one", CodexPipelineHarness.Gpt(), observer, autoCompact: true);

		Assert.Single(_p.Requests("thread/compact/start"));
		Assert.Equal(1, _p.H.Files.CompactionSeparators);
		Assert.Contains("compactionFinished:Compacted", observer.Snapshot());
		Assert.NotNull(node.CodexThreadId);
		Assert.Empty(_p.H.Process.Calls);
	}

	[Fact]
	public async Task TC_CX_33_compacting_a_Codex_session_without_a_thread_does_nothing()
	{
		var node = _p.CodexNode();

		var result = await _p.Compaction.CompactAsync(node, new SessionCompactionOptionsModel(null, null, null, null));

		Assert.Equal(SessionCompactionOutcome.NothingToCompact, result.Outcome);
		Assert.Empty(_p.Launcher.Servers);
		Assert.Empty(_p.H.Process.Calls);
	}

	[Fact]
	public async Task Reset_conversation_moves_the_thread_to_the_prior_ids_and_the_next_turn_starts_a_new_thread_from_the_transcript()
	{
		var node = _p.CodexNode();
		await _p.RunInteractiveAsync(node, "Remember PERIWINKLE", CodexPipelineHarness.Gpt());
		var threadId = node.CodexThreadId!;
		var registry = new FakeSessionRegistry();
		registry.AddNode(node);
		var options  = new SessionOptionsService(registry, _p.H.Settings, _p.Interactive, new UnusedSettingsResolver(), _p.Router, _p.H.Turns);

		var reset = options.ResetConversation(node.NodeId);
		await _p.RunInteractiveAsync(node, "What was the word?", CodexPipelineHarness.Gpt());

		Assert.True(reset.Cleared);
		Assert.Equal([threadId], node.PriorCodexThreadIds);
		Assert.NotEqual(threadId, node.CodexThreadId);
		Assert.Equal(2, _p.Requests("thread/start").Length);
		Assert.Contains("[Human]: Remember PERIWINKLE", CodexEngineFixture.InputText(_p.Requests("turn/start")[1]));
	}

	[Fact]
	public async Task TC_CX_15_a_headless_turn_returns_the_joined_agent_messages_and_the_summed_tokens_with_no_cost()
	{
		var node = _p.CodexNode();
		_p.Launcher.Configure = server => server.TurnScript = start => server.Scripts.TextTurn(start.ThreadId, start.TurnId, "Part one.", "Part two.");

		var result = await _p.Headless.RunTurnAsync(node, "Summarise", TurnSource.Orchestrated).WaitAsync(TimeSpan.FromSeconds(20));

		Assert.False(result.IsError);
		Assert.Equal("Part one.\n\nPart two.", result.AssistantText);
		Assert.Equal(15600 + 15601, result.InputTokens);
		Assert.Equal(50 + 51, result.OutputTokens);
		Assert.Equal(0, result.CostUsd);
		Assert.Equal(node.CodexThreadId, result.SessionId);
		Assert.Equal(["USER", "ASSISTANT", "ASSISTANT"], _p.H.Files.Entries.Select(e => e.Role));
	}

	[Fact]
	public async Task TC_CX_30_two_worker_results_that_arrive_during_the_Codex_supervisor_s_turn_become_one_delivery_through_resume_and_turn_start()
	{
		var supervisor = _p.CodexNode(threadId: "01a20229-5555-7000-8000-000000000001");
		var registry   = new FakeSessionRegistry();
		registry.AddNode(supervisor);
		var scheduler  = new SchedulerService(_p.H.Settings, _p.Headless, registry);

		var turnLock = _p.Headless.GetTurnLock(supervisor.NodeId);
		await turnLock.WaitAsync(); // the supervisor's own turn is in progress
		scheduler.AddOrMergeMailboxDelivery(supervisor.NodeId, "Worker A", "Result A: tests pass.");
		scheduler.AddOrMergeMailboxDelivery(supervisor.NodeId, "Worker B", "Result B: docs updated.");
		scheduler.Poll();
		await Task.Delay(100);
		Assert.Empty(_p.Launcher.Servers); // held back while the turn runs
		turnLock.Release();

		scheduler.Poll();
		await Waits.Until(() => _p.H.Files.Completions.Count == 1);

		var resume = Assert.Single(_p.Requests("thread/resume"));
		Assert.Equal("01a20229-5555-7000-8000-000000000001", resume.GetProperty("threadId").GetString());
		var input = CodexEngineFixture.InputText(Assert.Single(_p.Requests("turn/start")));
		Assert.Contains("Result A: tests pass.", input);
		Assert.Contains("Result B: docs updated.", input);
		Assert.Empty(_p.Requests("thread/start"));
		Assert.Contains(_p.H.Files.Entries, e => e.Role == "ASSISTANT" && e.Content == "Hello from the fake.");
	}
}
