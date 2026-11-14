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
			threshold: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromMilliseconds(5), onStall: stall => { lock (stalls) stalls.Add(stall); });
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
				await Task.Delay(200, CancellationToken.None);
		}, runMs: 600);

		Assert.Single(stalls);
		Assert.True(stalls[0] >= TimeSpan.FromMilliseconds(150), stalls[0].ToString());
	}
}
