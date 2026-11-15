using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Auto-Compact in the engine (package J2, FR.11.6). The first tests are the facts that <see cref="AutoCompactCharacterisationTests"/> pinned for the
/// Avalonia controller, now through <see cref="TurnRequest.AutoCompact"/> instead of an observer hook; the rest cover what the engine path adds:
/// the empty prompt, the per-run flag, queued sends (A14), the live toggle and the auth error of a failed compaction.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineAutoCompactTests : IDisposable
{
	private readonly TurnEngineHarness _h = new();

	public void Dispose() => _h.Dispose();

	[Fact]
	public async Task CompactsAfterTheAnswer_UnderTheTurnLock_BeforeTheCompletionIsReported()
	{
		var settings = TurnEngineHarness.Chosen(model: "sonnet", effort: "low", profileDir: @"C:\profiles\p2");
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", settings: settings, autoCompact: true).Within();

		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "compactHeld", "completed:cancelled=False:error=null"], _h.Files.Log);
		var call = Assert.Single(_h.Compaction.Calls);
		Assert.Equal(0, call.LockCount);
		Assert.Equal(new SessionCompactionOptionsModel("sonnet", "low", @"C:\profiles\p2", null), call.Options);
	}

	[Fact]
	public async Task AlsoCompactsAfterATurnThatEndedWithAnErrorResult()
	{
		_h.Process.ScriptEvents(Evt.ResultError("boom"));

		await _h.Run(TurnEngineHarness.Node(), "Hi", autoCompact: true).Within();

		Assert.Equal(["append:USER:Hi", "append:SYSTEM:boom", "compactHeld", "completed:cancelled=False:error=boom"], _h.Files.Log);
	}

	[Fact]
	public async Task DoesNotCompactAfterAStoppedTurn()
	{
		_h.Process.Script((_, _, _) => throw new OperationCanceledException());

		var outcome = await _h.Run(TurnEngineHarness.Node(), "Hi", autoCompact: true).Within();

		Assert.True(outcome.Cancelled);
		Assert.Empty(_h.Compaction.Calls);
	}

	[Fact]
	public async Task AlsoCompactsAfterAStreamingTurn()
	{
		_h.Streaming.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(streaming: true), "Hi", autoCompact: true).Within();

		Assert.Equal(["compactHeld"], _h.Compaction.Calls.ConvertAll(c => c.Variant));
	}

	[Fact]
	public async Task WithoutTheFlag_NoCompactionRuns()
	{
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi").Within();

		Assert.Empty(_h.Compaction.Calls);
	}

	[Fact]
	public async Task TheObserverIsToldAboutTheCompaction_BetweenTheAnswerAndTheIdleTransition()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer, autoCompact: true).Within();

		var calls = observer.Snapshot().ToList();
		Assert.True(calls.IndexOf("afterResponse") < calls.IndexOf("compactionStarted"));
		Assert.True(calls.IndexOf("compactionStarted") < calls.IndexOf("compactionFinished:Compacted"));
		Assert.True(calls.IndexOf("compactionFinished:Compacted") < calls.IndexOf("beforeIdle"));
	}

	// -- The empty prompt (smoke checklist B.1 step 11) -----------------------------------------------------------------------------

	[Fact]
	public async Task EmptyPrompt_SendsNoTurn_AndOnlyCompacts_TakingTheLockItself()
	{
		var observer = new RecordingTurnObserver();

		var outcome = await _h.Run(TurnEngineHarness.Node(sessionId: "s0"), message: "", observer, autoCompact: true).Within();

		Assert.Empty(_h.Process.Calls);
		Assert.Empty(_h.Streaming.Sends);
		Assert.Equal(["compact"], _h.Files.Log);
		var call = Assert.Single(_h.Compaction.Calls);
		Assert.Equal("compact", call.Variant);
		Assert.Equal(1, call.LockCount);
		Assert.False(outcome.Cancelled);
		Assert.Null(outcome.ErrorText);
		Assert.Equal(["compactionStarted", "compactionFinished:Compacted"], observer.Snapshot());
		Assert.Empty(_h.Registry.SetRunningCalls);
	}

	[Fact]
	public async Task EmptyPrompt_ReportsAFailedCompactionAsTheOutcomeError()
	{
		_h.Compaction.Result = new SessionCompactionResultModel(SessionCompactionOutcome.Failed, "cli died", "[failed]", false);

		var outcome = await _h.Run(TurnEngineHarness.Node(), message: "", autoCompact: true).Within();

		Assert.Equal("cli died", outcome.ErrorText);
	}

	[Fact]
	public async Task EmptyPromptWithoutTheFlag_IsNotShortCut_ItIsAnOrdinaryTurn()
	{
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), message: "").Within();

		Assert.Single(_h.Process.Calls);
		Assert.Empty(_h.Compaction.Calls);
	}

	// -- Per run ---------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task TheFlagIsPerRun_TheNextRunWithoutItDoesNotCompact()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.Result());
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "one", autoCompact: true).Within();
		await _h.Run(node, "two").Within();

		Assert.Single(_h.Compaction.Calls);
	}

	[Fact]
	public async Task AQueuedSendKeepsItsOwnFlag_NotTheOneOfTheRunInFrontOfIt()
	{
		var node = TurnEngineHarness.Node();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_h.Process.Script(async (_, onEvent, _) =>
		{
			await gate.Task;
			onEvent(Evt.Result());
		});
		_h.Process.ScriptEvents(Evt.Result());

		var first  = _h.Run(node, "one");                       // no Auto-Compact
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two", autoCompact: true);    // queued behind it, with Auto-Compact

		gate.SetResult();
		await first.Within();
		await second.Within();

		// Exactly one compaction, after the second answer and before the second completion; the first run completes without one.
		Assert.Single(_h.Compaction.Calls);
		Assert.Equal(
			["append:USER:one", "append:USER:two", "completed:cancelled=False:error=null", "compactHeld", "completed:cancelled=False:error=null"],
			_h.Files.Log);
	}

	[Fact]
	public async Task TheObserversLiveToggleDecides_ThatIsHowAMidRunToggleOfTheDesktopComposerStillWorks()
	{
		var enabledMidRun = new RecordingTurnObserver { AutoCompactOverride = _ => true };
		var disabledMidRun = new RecordingTurnObserver { AutoCompactOverride = _ => false };
		_h.Process.ScriptEvents(Evt.Result());
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "one", enabledMidRun, autoCompact: false).Within();
		await _h.Run(TurnEngineHarness.Node(), "two", disabledMidRun, autoCompact: true).Within();

		Assert.Single(_h.Compaction.Calls);
	}

	// -- Failure ---------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task AFailedCompactionForInvalidCredentials_RaisesTheAuthError_ForTheObserverAndTheSessionState()
	{
		var observer = new RecordingTurnObserver();
		_h.Compaction.Result = new SessionCompactionResultModel(
			SessionCompactionOutcome.Failed, "401 Invalid authentication credentials", "[failed]", false);
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer, autoCompact: true).Within();

		Assert.Contains("authError:True", observer.Snapshot());
		Assert.True(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task AFailedCompactionForAnotherReason_RaisesNoAuthError()
	{
		_h.Compaction.Result = new SessionCompactionResultModel(SessionCompactionOutcome.Failed, "timeout", "[failed]", false);
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", autoCompact: true).Within();

		Assert.False(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task AnEngineWithoutACompactionService_IgnoresTheFlag()
	{
		var bare = new TurnEngine(
			_h.Files, _h.Registry, _h.Turns, _h.Process, _h.Streaming, _h.Settings,
			new Lazy<IAgentMcpServer>(() => _h.Mcp), new Lazy<ITelegramBridgeService>(() => _h.Telegram),
			_h.Status, _h.Attention.Service, _h.Tags.Service);
		_h.Process.ScriptEvents(Evt.Result());

		var outcome = await bare.RunAsync(new TurnRequest(TurnEngineHarness.Node(), "Hi", TurnEngineHarness.Chosen(), AutoCompact: true)).Within();

		Assert.False(outcome.Cancelled);
	}
}
