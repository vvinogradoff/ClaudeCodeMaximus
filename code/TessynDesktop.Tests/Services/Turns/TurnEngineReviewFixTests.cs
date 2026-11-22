using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>Tests for the review of the engine against the old TurnController (P1.I review fixes, findings 1-6 and the test gaps).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineReviewFixTests : IDisposable
{
	private const string NoConversation = "No conversation found with session ID old";
	private static readonly string _race = Constants.Auth.OAuthRefreshRaceMarker;

	private readonly RecordingProgressSink _sink = new();
	private readonly TurnEngineHarness _h;

	public TurnEngineReviewFixTests() => _h = new TurnEngineHarness(sinks: [_sink]);

	public void Dispose() => _h.Dispose();

	// -- Finding 2: settings are those of the moment the message was sent ---------------------------------------------------------

	[Fact]
	public async Task AQueuedSend_RunsWithTheSelectionOfItsOwnSend_NotTheOneAtDispatch()
	{
		var node = TurnEngineHarness.Node();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_h.Process.Script(async (_, _, _) => await gate.Task);
		_h.Process.ScriptEvents(Evt.Result());

		var first  = _h.Run(node, "one", settings: TurnEngineHarness.Chosen(model: "opus", effort: "high", profileDir: @"C:\profiles\p1"));
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two", settings: TurnEngineHarness.Chosen(model: "sonnet", effort: "low", profileDir: @"C:\profiles\p2", profileName: "Other"));
		gate.SetResult();
		await first.Within();
		await second.Within();

		Assert.Equal([("opus", "high", @"C:\profiles\p1"), ("sonnet", "low", @"C:\profiles\p2")],
			_h.Process.Calls.Select(c => (c.Model, c.Effort, c.ProfileConfigDir)));
		Assert.Equal("Other", _h.Files.Entries.First(e => e.Content == "two").ProfileName);
	}

	// -- Finding 4: idle order ----------------------------------------------------------------------------------------------------

	[Fact]
	public async Task TheIdleTransition_RunsBeforeTheRunningFlagIsReleased_BeforeIdleFirst()
	{
		var observer = new RecordingTurnObserver();
		var downsAt = new List<(string Step, int Downs)>();
		observer.OnIdleStep = step => downsAt.Add((step, _h.Registry.SetRunningCalls.Count(c => !c.isRunning)));
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal([("beforeIdle", 0), ("idle", 0)], downsAt);
		Assert.Equal(1, _h.Registry.SetRunningCalls.Count(c => !c.isRunning));
		Assert.Equal(["beforeIdle", "idle"], observer.Snapshot().Where(c => c is "beforeIdle" or "idle"));
	}

	[Fact]
	public async Task QueuedSends_ReachTheIdleTransitionOnce_AfterTheLastOne()
	{
		var node = TurnEngineHarness.Node();
		var observer = new RecordingTurnObserver();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_h.Process.Script(async (_, _, _) => await gate.Task);
		_h.Process.ScriptEvents(Evt.Result());

		var first = _h.Run(node, "one", observer);
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two", observer);
		gate.SetResult();
		await first.Within();
		await second.Within();

		Assert.Equal(["beforeIdle", "idle"], observer.Snapshot().Where(c => c is "beforeIdle" or "idle"));
	}

	[Fact]
	public async Task AnIdleTransitionForATurnWithoutObserver_IsReportedToTheLastObserverOfTheNode()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Result());
		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "remote").Within();

		Assert.Equal(2, observer.Snapshot().Count(c => c == "idle"));
	}

	// -- Finding 3: atomic busy / running share ------------------------------------------------------------------------------------

	[Fact]
	public void TheBusyPeriodAndItsShare_AreAtomic_NoSendEverEndsUpRunningWithoutTheShare()
	{
		var context = new TurnNodeContext();
		var opened = 0;
		var closed = 0;
		var violations = 0;

		Parallel.For(0, 20000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
		{
			context.Enter();
			if (context.TryOpenRunning())
				Interlocked.Increment(ref opened);

			// While this send is inside its period, a share must be open.
			if (!context.IsBusy)
				Interlocked.Increment(ref violations);

			context.Leave();
			if (context.TryBecomeIdle(out var wasOpen) && wasOpen)
				Interlocked.Increment(ref closed);
		});

		Assert.Equal(0, violations);
		Assert.Equal(opened, closed);
		Assert.False(context.IsBusy);
	}

	[Fact]
	public async Task ManyOverlappingSendsOnOneNode_OpenAndCloseTheRunningFlagInBalance()
	{
		var node = TurnEngineHarness.Node();
		var observer = new RecordingTurnObserver();
		for (var i = 0; i < 60; i++)
			_h.Process.ScriptEvents(Evt.Result());

		await Task.WhenAll(Enumerable.Range(0, 60).Select(i => Task.Run(() => _h.Run(node, "m" + i, observer)))).Within();

		var calls = _h.Registry.SetRunningCalls.ToList();
		var level = 0;
		foreach (var call in calls)
		{
			level += call.isRunning ? 1 : -1;
			Assert.InRange(level, 0, 60);
		}

		Assert.Equal(0, level);
		Assert.False(_h.Engine.IsBusy(TurnEngineHarness.NodeId));
	}

	// -- Finding 1 and gaps: streaming events outside a turn -----------------------------------------------------------------------

	[Fact]
	public async Task StreamingOutputBetweenTurns_IsPersistedAndRenderedByTheLastObserver_WithoutProgressOrStaleState()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		var observer = new RecordingTurnObserver();
		_h.Streaming.ScriptEvents(Evt.Assistant("answer"), Evt.Result("s1"));
		await _h.Run(node, "Hi", observer).Within();
		var before = _sink.Items.Count;

		_h.Streaming.EmitToPrimary(Evt.Assistant("a background task finished"));
		_h.Streaming.EmitToPrimary(Evt.SystemError("a stray error"));

		Assert.Equal(before, _sink.Items.Count);
		Assert.Contains("append:ASSISTANT:a background task finished", _h.Files.Log);
		Assert.Contains("append:SYSTEM:a stray error", _h.Files.Log);
		Assert.Contains("assistant:a background task finished", observer.Snapshot());
		// The model stamp of the finished turn is consumed: a background answer carries none.
		Assert.Null(_h.Files.Entries.First(e => e.Content == "a background task finished").ModelId);
	}

	[Fact]
	public async Task ARetryMarkerBetweenTurns_DoesNotMakeTheNextStreamingTurnRetry()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(Evt.Result("s1"));
		await _h.Run(node, "one").Within();

		_h.Streaming.EmitToPrimary(Evt.ResultError(_race));
		_h.Streaming.ScriptEvents(Evt.Assistant("fine"), Evt.Result("s1"));
		var outcome = await _h.Run(node, "two").Within();

		Assert.Equal(2, _h.Streaming.Sends.Count);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task AnEventWhileTheNextTurnWaitsForTheLock_LandsNeitherOnTheFinishedNorOnTheWaitingRun()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(Evt.Result("s1"));
		await _h.Run(node, "one").Within();

		var turnLock = _h.Turns.GetTurnLock(TurnEngineHarness.NodeId);
		await turnLock.WaitAsync();
		_h.Streaming.ScriptEvents(Evt.Assistant("fine"), Evt.Result("s1"));
		var second = _h.Run(node, "two");
		_h.Streaming.EmitToPrimary(Evt.ResultError(_race));
		turnLock.Release();
		var outcome = await second.Within();

		Assert.Equal(2, _h.Streaming.Sends.Count);
		Assert.Null(outcome.ErrorText);
		Assert.Equal("completed:cancelled=False:error=null", _h.Files.Log[^1]);
	}

	[Fact]
	public async Task AFlagSetInAStreamingTurn_DoesNotLeakIntoTheNextOneShotSend()
	{
		var node = TurnEngineHarness.Node(sessionId: "old", streaming: true);
		_h.Streaming.ScriptEvents(Evt.ResultError(NoConversation));
		await _h.Run(node, "one").Within();
		node.IsStreamingMode = false;
		_h.Process.ScriptEvents(Evt.SystemError("a real error"), Evt.Result("s2"));

		await _h.Run(node, "two").Within();

		Assert.Single(_h.Process.Calls);
		Assert.Contains("append:SYSTEM:a real error", _h.Files.Log);
	}

	[Fact]
	public async Task ANoConversationFollowedByASuccessfulOneShotSend_IsAnOrdinaryTurn()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.ScriptEvents(Evt.Result("s2"));
		await _h.Run(node, "one").Within();
		_h.Process.ScriptEvents(Evt.Assistant("next"), Evt.Result("s2"));

		await _h.Run(node, "two").Within();

		Assert.Equal(3, _h.Process.Calls.Count);
		Assert.Equal("s2", _h.Process.Calls[2].SessionId);
	}

	[Fact]
	public async Task AStreamingLaunchFailure_WritesTheErrorLine_ReportsCompletionWithoutErrorText_AndSurfacesTheException()
	{
		var observer = new RecordingTurnObserver();
		_h.Streaming.EnsureFailure = new InvalidOperationException("Failed to start streaming claude process");

		await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Run(TurnEngineHarness.Node(streaming: true), "Hi", observer).Within());

		Assert.Equal(
			["append:USER:Hi", "append:SYSTEM:Could not launch claude in Streaming Session Mode.", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Contains("systemError:Could not launch claude in Streaming Session Mode.", observer.Snapshot());
		Assert.Empty(_h.Streaming.Sends);
		Assert.Equal(1, _h.Registry.SetRunningCalls.Count(c => !c.isRunning));
	}

	// -- Gaps: cancelling the retries, edge events -----------------------------------------------------------------------------------

	[Fact]
	public async Task StopDuringTheOAuthRetryDelay_InterruptsTheTurn_WithoutRelaunching()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		_h.Process.ScriptEvents(Evt.ResultError(_race));

		var running = _h.Run(node);
		await Waits.Until(() => _sink.Lines().Contains("status:Retrying:auth"));
		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		var outcome = await running.Within();

		Assert.True(outcome.Cancelled);
		Assert.Single(_h.Process.Calls);
		Assert.Equal("completed:cancelled=True:error=null", _h.Files.Log[^1]);
		Assert.Contains("append:SYSTEM:[Interrupted by user]", _h.Files.Log);
	}

	[Fact]
	public async Task StopDuringTheContextRetry_InterruptsTheTurn_AndTheRetryLaunchWasCancelled()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var running = _h.Run(node, "Hi", observer);
		await Waits.Until(() => _h.Process.Calls.Count == 2);
		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		var outcome = await running.Within();

		Assert.True(outcome.Cancelled);
		Assert.Contains("contextRetrying", observer.Snapshot());
		Assert.Null(_h.Process.Calls[1].SessionId);
		Assert.Equal("completed:cancelled=True:error=null", _h.Files.Log[^1]);
	}

	[Fact]
	public async Task CancelRuns_WhileAStreamingProcessIsAlive_CancelsTheSend_SoTheAdapterMustKillTheProcessInsteadWhenItIsAlive()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.Script((_, ct) => Task.Delay(Timeout.Infinite, ct));

		var running = _h.Run(node);
		await Waits.Until(() => _h.Streaming.Sends.Count == 1);

		Assert.True(_h.Streaming.IsAlive(TurnEngineHarness.NodeId));
		Assert.True(_h.Engine.CancelRuns(TurnEngineHarness.NodeId));
		Assert.True((await running.Within()).Cancelled);
		Assert.Empty(_h.Streaming.Stopped);
	}

	[Theory]
	[InlineData("result", "NO CONVERSATION FOUND with session")]
	[InlineData("system", "claude: no conversation found")]
	public async Task TheNoConversationMarker_IsMatchedCaseInsensitively(string type, string text)
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(type == "result" ? Evt.ResultError(text) : Evt.SystemError(text));
		_h.Process.ScriptEvents(Evt.Result("s2"));

		await _h.Run(node).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
	}

	[Fact]
	public async Task AResultErrorWithBothMarkers_IsTreatedAsNoConversation_TheFirstCaseOfTheSwitch()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation + " / " + _race));
		_h.Process.ScriptEvents(Evt.Result("s2"));

		await _h.Run(node).Within();

		Assert.Equal([ "old", null ], _h.Process.Calls.Select(c => c.SessionId));
	}

	[Fact]
	public async Task ASystemEventThatIsBothTaskProgressAndAnError_IsShownAsProgress_NotWritten()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(new ClaudeStreamEvent { Type = "system", Subtype = "task_progress", IsError = true, Content = "[Bash] x" }, Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Contains("progress:[Bash] x", observer.Snapshot());
	}

	[Fact]
	public async Task ACompactEventThatIsAlsoAnError_AppendsTheSeparator()
	{
		_h.Process.ScriptEvents(new ClaudeStreamEvent { Type = "system", Subtype = "compact", IsError = true, Content = "x" }, Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(1, _h.Files.CompactionSeparators);
	}

	[Fact]
	public async Task ASuccessfulResultWithoutSessionId_DoesNotClearAnAuthError()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Result(sessionId: null));

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.DoesNotContain("authError:False", observer.Snapshot());
	}

	// -- Findings 5 and 6: registrations -----------------------------------------------------------------------------------------------

	[Fact]
	public void TheEngine_ResolvesAsASingleton_WithTheLazyServicesAndTheProgressSinks()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ISessionFileService>(_h.Files);
		services.AddSingleton<ISessionRegistry>(_h.Registry);
		services.AddSingleton<ISessionTurnService>(_h.Turns);
		services.AddSingleton<IClaudeProcessManager>(_h.Process);
		services.AddSingleton<IClaudeStreamingSessionManager>(_h.Streaming);
		services.AddSingleton<IAppSettingsService>(_h.Settings);
		services.AddSingleton<IAgentMcpServer>(_h.Mcp);
		services.AddSingleton<ITelegramBridgeService>(_h.Telegram);
		services.AddSingleton(sp => new Lazy<IAgentMcpServer>(sp.GetRequiredService<IAgentMcpServer>));
		services.AddSingleton(sp => new Lazy<ITelegramBridgeService>(sp.GetRequiredService<ITelegramBridgeService>));
		services.AddSingleton<IClaudeSessionStatusService>(_h.Status);
		services.AddSingleton<ISessionEngine, ClaudeSessionEngine>();
		services.AddSingleton<ISessionEngineRouter, SessionEngineRegistry>();
		services.AddSingleton<IAttentionService>(_h.Attention.Service);
		services.AddSingleton<ISessionTagService>(_h.Tags.Service);
		services.AddSingleton<ITurnProgressSink>(_sink);
		services.AddSingleton<ITurnEngine, TurnEngine>();

		using var provider = services.BuildServiceProvider();
		var one = provider.GetRequiredService<ITurnEngine>();

		Assert.Same(one, provider.GetRequiredService<ITurnEngine>());
	}
}
