using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>
/// Watchdog for the UI thread: it repeatedly sends a trivial job to the UI thread and times how long it takes to run. A job that
/// takes longer than the threshold means the thread was blocked that long, and the stall is logged.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiStallMonitor
{
	private readonly Func<CancellationToken, Task> _pingUiThread;
	private readonly ILogger<UiStallMonitor> _logger;
	private readonly TimeProvider _time;
	private readonly TimeSpan _threshold;
	private readonly TimeSpan _pollInterval;
	private readonly Action<TimeSpan>? _onStall;

	/// <param name="pingUiThread">Completes once the UI thread ran a trivial job.</param>
	/// <param name="logger">Receives the stall warnings.</param>
	/// <param name="time">The clock.</param>
	/// <param name="threshold">A ping slower than this is a stall.</param>
	/// <param name="pollInterval">Pause between pings.</param>
	/// <param name="onStall">Optional observer of every stall (tests).</param>
	public UiStallMonitor(Func<CancellationToken, Task> pingUiThread, ILogger<UiStallMonitor> logger, TimeProvider time, TimeSpan threshold, TimeSpan pollInterval, Action<TimeSpan>? onStall = null)
	{
		_pingUiThread = pingUiThread;
		_logger = logger;
		_time = time;
		_threshold = threshold;
		_pollInterval = pollInterval;
		_onStall = onStall;
	}

	/// <summary>Runs until cancelled.</summary>
	public async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var start = _time.GetTimestamp();
				var ping = _pingUiThread(cancellationToken);
				var timeout = Task.Delay(_threshold, _time, cancellationToken);
				if (await Task.WhenAny(ping, timeout) != ping)
				{
					await ping;
					var stalled = _time.GetElapsedTime(start);
					_logger.LogWarning("UI thread stalled for {Ms:0} ms", stalled.TotalMilliseconds);
					_onStall?.Invoke(stalled);
				}

				await Task.Delay(_pollInterval, _time, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}
}
