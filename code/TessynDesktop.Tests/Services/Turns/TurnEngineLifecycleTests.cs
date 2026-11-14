using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Safety net for the lifecycle of an interactive turn (P1.I stage 1): the running-flag reference count, queued sends and the turn
/// lock, Stop / cancellation, failures, the post-response hook, Streaming Session Mode and the launch settings (MCP config, Local Tool Profile).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineLifecycleTests : IDisposable
{
	private readonly TurnEngineHarness _h = new();

	public void Dispose() => _h.Dispose();

	private static bool RunningUpDown(TurnEngineHarness h, int up, int down) =>
		h.Registry.SetRunningCalls.Count(c => c.isRunning) == up && h.Registry.SetRunningCalls.Count(c => !c.isRunning) == down;

	// -- Running flag -------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task RunningFlag_IsOpenedBeforeTheProcessStarts_AndClosedOnceTheTurnIsOver()
	{
		var upWhenLaunched = -1;
		_h.Process.OnLaunch = _ => upWhenLaunched = _h.Registry.SetRunningCalls.Count(c => c.isRunning);
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(1, upWhenLaunched);
		Assert.Equal([(TurnEngineHarness.NodeId, true), (TurnEngineHarness.NodeId, false)], _h.Registry.SetRunningCalls);
	}

	[Fact]
	public async Task RunningFlag_IsStillOpenWhileTheCompletionIsReported_AndClosedAfterIt()
	{
		var upDownAtCompletion = (-1, -1);
		_h.Files.LockCount = () =>
		{
			upDownAtCompletion = (_h.Registry.SetRunningCalls.Count(c => c.isRunning), _h.Registry.SetRunningCalls.Count(c => !c.isRunning));
			return 0;
		};
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal((1, 0), upDownAtCompletion);
		Assert.True(RunningUpDown(_h, 1, 1));
	}

	[Fact]
	public async Task QueuedSends_ShareOneRunningFlag_ClosedWhenTheLastOneEnds()
	{
		var node = TurnEngineHarness.Node();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_h.Process.Script(async (_, onEvent, _) =>
		{
			await gate.Task;
			onEvent(Evt.Assistant("first"));
		});
		_h.Process.ScriptEvents(Evt.Assistant("second"));

		var first  = _h.Run(node, "one");
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two");

		// The second message is in the transcript at once; its process waits behind the first.
		Assert.Equal(["append:USER:one", "append:USER:two"], _h.Files.Log);
		await Task.Delay(50);
		Assert.Single(_h.Process.Calls);

		gate.SetResult();
		var firstOutcome  = await first.Within();
		var secondOutcome = await second.Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.True(RunningUpDown(_h, 1, 1));
		Assert.Equal(
			["append:USER:one", "append:USER:two", "append:ASSISTANT:first", "completed:cancelled=False:error=null",
			 "append:ASSISTANT:second", "completed:cancelled=False:error=null"],
			_h.Files.Log);
		Assert.NotEqual(firstOutcome.TurnId, secondOutcome.TurnId);
	}

	// -- Stop and cancellation -----------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Stop_InterruptsTheTurn_WritesTheInterruptionLine_AndReportsItCancelled()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.Script((_, onEvent, ct) =>
		{
			onEvent(Evt.Assistant("partial"));
			return Task.Delay(Timeout.Infinite, ct);
		});

		var running = _h.Run(TurnEngineHarness.Node(), "Hi", observer);
		await Waits.Until(() => _h.Process.Calls.Count == 1);

		Assert.True(_h.Engine.CancelRuns(TurnEngineHarness.NodeId));
		var outcome = await running.Within();

		Assert.True(outcome.Cancelled);
		Assert.Null(outcome.ErrorText);
		Assert.Equal(
			["append:USER:Hi", "append:ASSISTANT:partial", "append:SYSTEM:[Interrupted by user]", "completed:cancelled=True:error=null"],
			_h.Files.Log);
		Assert.Contains("interrupted", observer.Snapshot());
		Assert.DoesNotContain("afterResponse", observer.Snapshot());
		Assert.True(RunningUpDown(_h, 1, 1));
		Assert.Equal(1, _h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount);
	}

	[Fact]
	public async Task Stop_DropsAnErrorTextTheTurnHadCollected()
	{
		_h.Process.Script(async (_, onEvent, ct) =>
		{
			onEvent(Evt.ResultError("Usage limit reached"));
			await Task.Delay(Timeout.Infinite, ct);
		});

		var running = _h.Run(TurnEngineHarness.Node());
		await Waits.Until(() => _h.Files.Log.Contains("append:SYSTEM:Usage limit reached"));
		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		await running.Within();

		Assert.Equal("completed:cancelled=True:error=null", _h.Files.Log[^1]);
	}

	[Fact]
	public async Task CancellationTokenOfTheCaller_StopsTheTurnLikeStop()
	{
		using var cts = new CancellationTokenSource();
		_h.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var running = _h.Run(TurnEngineHarness.Node(), ct: cts.Token);
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		cts.Cancel();
		var outcome = await running.Within();

		Assert.True(outcome.Cancelled);
	}

	[Fact]
	public async Task StopWhileQueued_WritesTheInterruptionLine_ButReportsNoCompletionForTheSendThatNeverRan()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var first = _h.Run(node, "one");
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two");

		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		var firstOutcome  = await first.Within();
		var secondOutcome = await second.Within();

		Assert.True(firstOutcome.Cancelled);
		Assert.True(secondOutcome.Cancelled);
		Assert.Single(_h.Process.Calls);
		Assert.Equal(2, _h.Files.Log.Count(l => l == "append:SYSTEM:[Interrupted by user]"));
		Assert.Single(_h.Files.Completions);
		Assert.True(RunningUpDown(_h, 1, 1));
	}

	[Fact]
	public void CancelRuns_ForANodeWithoutTurns_ReturnsFalse()
	{
		Assert.False(_h.Engine.CancelRuns("nobody"));
	}

	[Fact]
	public async Task CancelRuns_AfterTheTurnEnded_ReturnsFalse()
	{
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.False(_h.Engine.CancelRuns(TurnEngineHarness.NodeId));
	}

	// -- Failures ------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task ProcessFailure_IsRethrown_AfterTheCompletionWasReported_AndEverythingIsReleased()
	{
		_h.Process.Script((_, _, _) => throw new InvalidOperationException("spawn failed"));

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Run(TurnEngineHarness.Node()).Within());

		Assert.Equal("spawn failed", ex.Message);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.True(RunningUpDown(_h, 1, 1));
		Assert.Equal(1, _h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount);
	}

	[Fact]
	public async Task FailureBeforeTheLock_ReportsNoCompletion_AndNeverOpensTheRunningFlag()
	{
		_h.Settings.Settings.AgentToolsEnabled = true;
		_h.Mcp.Failure = new InvalidOperationException("mcp config failed");

		await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Run(TurnEngineHarness.Node()).Within());

		Assert.Equal(["append:USER:Hi"], _h.Files.Log);
		Assert.Empty(_h.Registry.SetRunningCalls);
		Assert.Equal(1, _h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount);
	}

	[Fact]
	public async Task ANodeWithoutId_RunsWithoutLockOrRunningFlag_AndStillReportsCompletion()
	{
		var node = TurnEngineHarness.Node();
		node.NodeId = string.Empty;
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(node).Within();

		Assert.Empty(_h.Registry.SetRunningCalls);
		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "completed:cancelled=False:error=null"], _h.Files.Log);
	}

	// -- Post-response hook --------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task PostResponseHook_RunsAfterTheResponse_WhileTheTurnLockIsHeld_BeforeTheCompletion()
	{
		var lockCountInHook = -1;
		var observer = new RecordingTurnObserver
		{
			AfterResponse = () =>
			{
				lockCountInHook = _h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount;
				_h.Files.Log.Add("hook");
				return Task.CompletedTask;
			},
		};
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(0, lockCountInHook);
		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "hook", "completed:cancelled=False:error=null"], _h.Files.Log);
	}

	[Fact]
	public async Task PostResponseHook_CancelledByStop_ProducesTheInterruptedOutcome()
	{
		var observer = new RecordingTurnObserver { AfterResponse = () => throw new OperationCanceledException() };
		_h.Process.ScriptEvents(Evt.Result());

		var outcome = await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.True(outcome.Cancelled);
		Assert.Equal("completed:cancelled=True:error=null", _h.Files.Log[^1]);
	}

	[Fact]
	public async Task PostResponseHook_AlsoRunsAfterAStreamingTurn()
	{
		var observer = new RecordingTurnObserver();
		_h.Streaming.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(streaming: true), "Hi", observer).Within();

		Assert.Contains("afterResponse", observer.Snapshot());
	}

	// -- Streaming Session Mode ------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Streaming_SpawnsTheProcessWithTheTurnsSettings_AndWritesTheTurnToItsStdin()
	{
		var node = TurnEngineHarness.Node(sessionId: "s0", streaming: true);
		var observer = new RecordingTurnObserver();
		_h.Streaming.ScriptEvents(Evt.Assistant("hi"), Evt.Result("s1"));

		await _h.Run(node, "Hello", observer, TurnEngineHarness.Chosen(model: "opus", effort: "high", profileDir: @"C:\profiles\p1")).Within();

		var (nodeId, spawn) = Assert.Single(_h.Streaming.EnsureCalls);
		Assert.Equal(TurnEngineHarness.NodeId, nodeId);
		Assert.Equal((@"C:\proj", "claude-test", "s0", @"C:\profiles\p1", "opus", "high"),
			(spawn.WorkingDirectory, spawn.ClaudePath, spawn.SessionId, spawn.ProfileConfigDir, spawn.Model, spawn.Effort));
		Assert.Equal("Hello" + _h.ExpectedBlock(node, false, isNewSession: false, streamingOverride: true), Assert.Single(_h.Streaming.Sends).Message);
		Assert.Empty(_h.Process.Calls);
		Assert.Equal(["append:USER:Hello", "append:ASSISTANT:hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Equal("s1", node.ClaudeSessionId);
		Assert.Contains("streamingStarted", observer.Snapshot());
	}

	[Fact]
	public async Task Streaming_SecondTurnReusesTheAliveProcess_AndOnlyTheFirstAnnouncesTheStart()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		var observer = new RecordingTurnObserver();
		_h.Streaming.ScriptEvents(Evt.Result("s1"));
		_h.Streaming.ScriptEvents(Evt.Assistant("again"), Evt.Result("s1"));

		await _h.Run(node, "one", observer).Within();
		await _h.Run(node, "two", observer).Within();

		Assert.Equal(2, _h.Streaming.EnsureCalls.Count);
		Assert.Equal(1, observer.Snapshot().Count(c => c == "streamingStarted"));
		Assert.Contains("append:ASSISTANT:again", _h.Files.Log);
	}

	[Fact]
	public async Task Streaming_StreamingFlagOfTheNode_DecidesThePath_AfterTheLockWasAcquired()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node).Within();

		Assert.Single(_h.Process.Calls);
		Assert.Empty(_h.Streaming.Sends);
	}

	// -- Launch settings -----------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task AgentToolsOn_ToolCapableModel_GetsTheNodesMcpConfig_AndTheNativeSchedulingBlock()
	{
		_h.Settings.Settings.AgentToolsEnabled = true;
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.Equal($@"C:\mcp\{TurnEngineHarness.NodeId}.json", call.McpConfigPath);
		Assert.True(call.DisallowNativeScheduling);
		Assert.Equal([(TurnEngineHarness.NodeId, "tok", (bool?)null, (System.Collections.Generic.IReadOnlyList<string>?)null)], _h.Mcp.Requests);
	}

	[Fact]
	public async Task AgentToolsOn_ModelWithoutToolSupport_GetsNoMcpConfig()
	{
		_h.Settings.Settings.AgentToolsEnabled = true;
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), settings: TurnEngineHarness.Chosen(disableTools: true)).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.Null(call.McpConfigPath);
		Assert.True(call.DisableTools);
		Assert.Empty(_h.Mcp.Requests);
	}

	[Fact]
	public async Task AgentToolsOff_GetsNoMcpConfig_AndNativeSchedulingIsNotBlocked()
	{
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.Null(call.McpConfigPath);
		Assert.False(call.DisallowNativeScheduling);
		Assert.Empty(_h.Mcp.Requests);
	}

	[Fact]
	public async Task LocalToolProfile_ToolCapableOllamaModel_GetsTheCuratedToolsAndAStrictFilteredMcpConfig()
	{
		var ollama = new ClaudeModelInfo("gemma4:26b", string.Empty, "Gemma", ModelProvider.Ollama, SupportsTools: true);
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), settings: TurnEngineHarness.Chosen(model: "gemma4:26b", modelInfo: ollama,
			ollamaUrl: "http://localhost:11434")).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.True(call.StrictMcpConfig);
		Assert.NotNull(call.LocalToolSpec);
		Assert.Equal(Constants.LocalTools.DefaultNativeTools, call.LocalToolSpec!.NativeToolNames);
		Assert.Equal($@"C:\mcp\{TurnEngineHarness.NodeId}.local.json", call.McpConfigPath);
		Assert.Equal((TurnEngineHarness.NodeId, "tok", (bool?)false), (_h.Mcp.Requests[0].NodeId, _h.Mcp.Requests[0].AgentToken, _h.Mcp.Requests[0].IncludeCmx));
	}

	// -- Mid-run corrections -------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Correction_ResumesTheCurrentSession_WithTheBarePrompt_AndOnlyKeepsTheNewSessionId()
	{
		var node = TurnEngineHarness.Node(sessionId: "s9");
		_h.Settings.Settings.AgentToolsEnabled = true;
		_h.Process.ScriptEvents(Evt.Assistant("ignored"), Evt.Result("s10"));

		_h.Engine.SendCorrection(node, TurnEngineHarness.Chosen(model: "opus", effort: "low", profileDir: @"C:\profiles\p2"), Constants.Instructions.MidRunAutoCommitOff);
		await Waits.Until(() => node.ClaudeSessionId == "s10");

		var call = Assert.Single(_h.Process.Calls);
		Assert.Equal(("s9", Constants.Instructions.MidRunAutoCommitOff, "opus", "low", @"C:\profiles\p2"),
			(call.SessionId, call.Message, call.Model, call.Effort, call.ProfileConfigDir));
		Assert.True(call.DisallowNativeScheduling);
		Assert.Null(call.McpConfigPath);
		Assert.False(call.Chrome);
		Assert.Empty(_h.Files.Log);
		Assert.Empty(_h.Registry.SetRunningCalls);
	}

	[Fact]
	public async Task Correction_IgnoresAnErrorResult_AndStreamingModeDoesNotChangeItsPath()
	{
		var node = TurnEngineHarness.Node(sessionId: "s9", streaming: true);
		var done = new TaskCompletionSource();
		_h.Process.Script((_, onEvent, _) =>
		{
			onEvent(Evt.ResultError("nope", sessionId: "bad"));
			done.SetResult();
			return Task.CompletedTask;
		});

		_h.Engine.SendCorrection(node, TurnEngineHarness.Chosen(), "Correction");
		await done.Task.Within();

		Assert.Equal("s9", node.ClaudeSessionId);
		Assert.Empty(_h.Streaming.Sends);
		Assert.Single(_h.Process.Calls);
	}
}
