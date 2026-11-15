using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Stop with a send queued behind the running one (P1.I): <c>SemaphoreSlim.WaitAsync(token)</c> may still grant the lock to a waiter whose
/// token was cancelled a moment before, so the engine re-checks its token after acquiring and gives the lock back. Run many times in
/// parallel, because the window is only a few instructions wide.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineStopRaceTests
{
	[Fact]
	public async Task AQueuedSendThatIsStopped_NeverLaunchesAProcess_NoMatterHowTheLockHandoverRaces()
	{
		var failures = new ConcurrentBag<string>();

		await Parallel.ForEachAsync(Enumerable.Range(0, 300), new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (_, _) =>
		{
			using var h = new TurnEngineHarness();
			var node = TurnEngineHarness.Node();
			h.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

			var first = h.Run(node, "one");
			await Waits.Until(() => h.Process.Calls.Count == 1);
			var second = h.Run(node, "two");

			h.Engine.CancelRuns(TurnEngineHarness.NodeId);
			var firstOutcome  = await first.Within();
			var secondOutcome = await second.Within();

			if (!firstOutcome.Cancelled || !secondOutcome.Cancelled || h.Process.Calls.Count != 1 || h.Files.Completions.Count != 1
			    || h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount != 1)
				failures.Add($"first={firstOutcome.Cancelled} second={secondOutcome.Cancelled} launches={h.Process.Calls.Count} completions={h.Files.Completions.Count}");
		});

		Assert.True(failures.IsEmpty, string.Join("\n", failures.Take(5)));
	}
}
