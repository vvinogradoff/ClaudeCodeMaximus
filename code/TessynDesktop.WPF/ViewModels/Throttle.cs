using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Runs an action at most once per interval, trailing: a request inside the interval schedules one run at its end and further requests until
/// then share it. A zero interval runs the action at once (tests). The action runs on the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class Throttle
{
	private readonly TimeSpan _interval;
	private readonly IUiDispatcher _ui;
	private DateTime _lastRun = DateTime.MinValue;
	private bool _scheduled;

	public Throttle(TimeSpan interval, IUiDispatcher ui)
	{
		_interval = interval;
		_ui = ui;
	}

	public void Request(Action action)
	{
		if (_interval <= TimeSpan.Zero)
		{
			action();
			return;
		}

		if (_scheduled)
			return;

		var wait = _lastRun + _interval - DateTime.UtcNow;
		if (wait <= TimeSpan.Zero)
		{
			_lastRun = DateTime.UtcNow;
			action();
			return;
		}

		_scheduled = true;
		_ = Task.Delay(wait).ContinueWith(_ => _ui.Post(() =>
		{
			_scheduled = false;
			_lastRun = DateTime.UtcNow;
			action();
		}), TaskScheduler.Default);
	}
}
