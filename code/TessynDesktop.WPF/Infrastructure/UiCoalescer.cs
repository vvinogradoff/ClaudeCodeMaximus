using System.Windows.Threading;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>
/// Runs an action on the UI thread once however many times it was requested before it got its turn. A Ctrl+wheel spin raises one settings change per notch; the pages
/// only need the state of the last one, so the notches that arrive while the action is still queued fold into it. The action reads the current state when it runs.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiCoalescer
{
	private readonly Action _action;
	private readonly DispatcherPriority _priority;
	private int _queued;

	public UiCoalescer(Action action, DispatcherPriority priority = DispatcherPriority.Background)
	{
		_action = action;
		_priority = priority;
	}

	/// <summary>Queues the action on <paramref name="dispatcher"/> unless it is already queued.</summary>
	public void Request(Dispatcher? dispatcher)
	{
		if (dispatcher == null || Interlocked.Exchange(ref _queued, 1) == 1)
			return;

		dispatcher.BeginInvoke(_priority, new Action(() =>
		{
			Volatile.Write(ref _queued, 0);
			_action();
		}));
	}
}
