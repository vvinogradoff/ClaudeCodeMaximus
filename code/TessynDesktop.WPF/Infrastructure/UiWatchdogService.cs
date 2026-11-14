using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>Hosted service that runs the <see cref="UiStallMonitor"/> against the WPF dispatcher for the life of the app.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiWatchdogService : IHostedService
{
	private readonly Dispatcher _dispatcher;
	private readonly ILogger<UiStallMonitor> _logger;
	private CancellationTokenSource? _cts;
	private Task _loop = Task.CompletedTask;

	public UiWatchdogService(Dispatcher dispatcher, ILogger<UiStallMonitor> logger)
	{
		_dispatcher = dispatcher;
		_logger = logger;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_cts = new CancellationTokenSource();
		var monitor = new UiStallMonitor(
			ct => _dispatcher.InvokeAsync(() => { }, DispatcherPriority.Send, ct).Task,
			_logger,
			TimeProvider.System,
			Constants.Timeouts.UiStallThreshold,
			Constants.Timeouts.WatchdogPoll);
		_loop = Task.Run(() => monitor.RunAsync(_cts.Token));
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_cts?.Cancel();
		try
		{
			await _loop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
		}
	}
}
