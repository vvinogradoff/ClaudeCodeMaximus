using System;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Pins what the Avalonia <c>TurnController</c> does for Auto-Compact (FR.11.6) before the behaviour moves into the engine (package J2).
/// The controller ran it through the engine's post-response hook: after the answer, under the node's turn lock, before the turn's
/// completion is reported, also after a failed turn, never after a stopped one, in Streaming Session Mode too, with the model, effort
/// and profile of the send. With an empty prompt it sent no turn and compacted through the lock-taking variant. <see cref="LegacyObserver"/>
/// is that controller logic, reduced to its calls, so these facts hold for the old and the new implementation alike.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutoCompactCharacterisationTests : IDisposable
{
	private readonly TurnEngineHarness _h = new();
	private readonly RecordingCompactionService _compaction;

	public AutoCompactCharacterisationTests()
	{
		_compaction = new RecordingCompactionService(() => _h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount, _h.Files.Log);
	}

	public void Dispose() => _h.Dispose();

	/// <summary>The controller's observer: after the response it compacts with the lock held, with the send's model, effort and profile.</summary>
	private RecordingTurnObserver Legacy(TurnSettings settings) => new()
	{
		AfterResponse = () => _compaction.CompactWhileHoldingTurnLockAsync(
			TurnEngineHarness.Node(), new SessionCompactionOptionsModel(settings.ModelId, settings.Effort, settings.ProfileConfigDir, settings.ModelInfo)),
	};

	[Fact]
	public async Task Legacy_CompactsAfterTheAnswer_UnderTheTurnLock_BeforeTheCompletionIsReported()
	{
		var settings = TurnEngineHarness.Chosen(model: "sonnet", effort: "low", profileDir: @"C:\profiles\p2");
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", Legacy(settings), settings).Within();

		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "compactHeld", "completed:cancelled=False:error=null"], _h.Files.Log);
		var call = Assert.Single(_compaction.Calls);
		Assert.Equal(0, call.LockCount);
		Assert.Equal(new SessionCompactionOptionsModel("sonnet", "low", @"C:\profiles\p2", null), call.Options);
	}

	[Fact]
	public async Task Legacy_AlsoCompactsAfterATurnThatEndedWithAnErrorResult()
	{
		_h.Process.ScriptEvents(Evt.ResultError("boom"));

		await _h.Run(TurnEngineHarness.Node(), "Hi", Legacy(TurnEngineHarness.Chosen())).Within();

		Assert.Equal(["append:USER:Hi", "append:SYSTEM:boom", "compactHeld", "completed:cancelled=False:error=boom"], _h.Files.Log);
	}

	[Fact]
	public async Task Legacy_DoesNotCompactAfterAStoppedTurn()
	{
		_h.Process.Script((_, _, _) => throw new OperationCanceledException());

		var outcome = await _h.Run(TurnEngineHarness.Node(), "Hi", Legacy(TurnEngineHarness.Chosen())).Within();

		Assert.True(outcome.Cancelled);
		Assert.Empty(_compaction.Calls);
	}

	[Fact]
	public async Task Legacy_AlsoCompactsAfterAStreamingTurn()
	{
		_h.Streaming.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(streaming: true), "Hi", Legacy(TurnEngineHarness.Chosen())).Within();

		Assert.Equal(["compactHeld"], _compaction.Calls.ConvertAll(c => c.Variant));
	}
}
