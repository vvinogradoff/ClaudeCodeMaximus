using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Tests;

/// <summary>The UI-thread watchdog reports only stalls longer than its threshold.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiStallMonitorTests
{
	private static async Task<List<TimeSpan>> RunAsync(Func<CancellationToken, Task> ping, int runMs)
	{
		var stalls = new List<TimeSpan>();
		var monitor = new UiStallMonitor(ping, NullLogger<UiStallMonitor>.Instance, TimeProvider.System,
			threshold: TimeSpan.FromMilliseconds(250), pollInterval: TimeSpan.FromMilliseconds(5), onStall: stall => { lock (stalls) stalls.Add(stall); });
		using var cts = new CancellationTokenSource(runMs);
		await monitor.RunAsync(cts.Token);
		lock (stalls)
			return stalls.ToList();
	}

	[Fact]
	public async Task ResponsiveUiThread_ReportsNoStall()
	{
		var stalls = await RunAsync(_ => Task.Delay(2), runMs: 300);

		Assert.Empty(stalls);
	}

	[Fact]
	public async Task BlockedUiThread_ReportsTheStallWithItsDuration()
	{
		var calls = 0;
		var stalls = await RunAsync(async ct =>
		{
			if (Interlocked.Increment(ref calls) == 2)
				await Task.Delay(600, CancellationToken.None);
		}, runMs: 1500);

		Assert.Single(stalls);
		Assert.True(stalls[0] >= TimeSpan.FromMilliseconds(500), stalls[0].ToString());
	}

	[Fact]
	public async Task LongStall_IsLoggedWhenItPassesTheThresholdNotOnlyWhenItEnds()
	{
		var logger = new Fakes.CapturingLogger<UiStallMonitor>();
		var release = new TaskCompletionSource();
		var calls = 0;
		var monitor = new UiStallMonitor(_ => Interlocked.Increment(ref calls) == 1 ? release.Task : Task.CompletedTask, logger, TimeProvider.System,
			TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5));
		using var cts = new CancellationTokenSource();
		var run = monitor.RunAsync(cts.Token);

		await Task.Delay(150);
		string[] during;
		lock (logger.Messages)
			during = logger.Messages.ToArray();
		release.SetResult();
		await Task.Delay(50);
		cts.Cancel();
		await run;

		Assert.Single(during);
		Assert.Contains("not responding", during[0]);
		Assert.Contains(logger.Messages, m => m.Contains("stalled for", StringComparison.Ordinal));
	}
}
