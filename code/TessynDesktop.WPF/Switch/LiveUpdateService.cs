using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// The self-update while the client runs and when it closes (as the Avalonia <c>SelfUpdateService</c> does): about once a minute it compares the newest build with the run folder
/// (write times, the start-up test) and shows the <see cref="UpdateNoticeViewModel"/> when a newer one exists; "Restart now" runs the start-up update (copy, restart); on exit a newer
/// build is copied by a script that starts nothing. Created only in the real environment from the run folder (never under <c>--test-window</c>, <c>--data-root</c>, <c>--daemon-file</c>
/// or from a build output).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LiveUpdateService : IDisposable
{
	private readonly WpfSelfUpdater _updater;
	private readonly UpdateNoticeViewModel _notice;
	private readonly IUiDispatcher _ui;
	private readonly IReadOnlyList<string> _arguments;
	private readonly Action _shutdown;
	private readonly TimeSpan _interval;
	private readonly int _processId;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _stop = new();
	private int _restarting;

	public LiveUpdateService(WpfSelfUpdater updater, UpdateNoticeViewModel notice, IUiDispatcher ui, IReadOnlyList<string> arguments, Action shutdown, TimeSpan interval, int processId, ILogger logger)
	{
		_updater = updater;
		_notice = notice;
		_ui = ui;
		_arguments = arguments;
		_shutdown = shutdown;
		_interval = interval;
		_processId = processId;
		_logger = logger;
		_notice.RestartHandler = RestartNow;
	}

	/// <summary>Raised after every check for a newer build, on the thread that ran it (FR.32.9: the stale-daemon banner is evaluated again then).</summary>
	public event Action? Checked;

	/// <summary>Starts the periodic check on the thread pool.</summary>
	public void Start() => _ = Task.Run(() => LoopAsync(_stop.Token));

	/// <summary>One check: shows or hides the notice. True when a newer build is ready.</summary>
	public bool CheckNow()
	{
		var pending = _updater.FindPendingClientBuild() != null;
		_ui.Post(() => _notice.IsVisible = pending);
		Checked?.Invoke();
		return pending;
	}

	/// <summary>"Restart now": copies the build and starts the client again (the start-up update), then shuts this process down.</summary>
	public void RestartNow()
	{
		if (_updater.CheckAndScheduleRestart(_processId, _arguments) == SelfUpdateOutcome.RestartScheduled)
		{
			MarkRestarting();
			_shutdown();
		}
	}

	/// <summary>A restart script is already waiting for this process (a daemon restart, or "Restart now"): the exit must not start a second, copy-only script beside it.</summary>
	public void MarkRestarting() => Interlocked.Exchange(ref _restarting, 1);

	/// <summary>On exit: copies a newer build (and the host's, when no host runs) without starting anything. Does nothing when a restart script is already pending.</summary>
	public SelfUpdateOutcome OnExit()
	{
		_stop.Cancel();
		return Volatile.Read(ref _restarting) == 1 ? SelfUpdateOutcome.Disabled : _updater.ScheduleCopyOnExit(_processId);
	}

	private async Task LoopAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_interval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					CheckNow();
				}
				catch (Exception ex)
				{
					_logger.LogDebug(ex, "The live update check failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public void Dispose() => _stop.Dispose();
}
