using System.Windows.Threading;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary><see cref="IUiDispatcher"/> over the WPF <see cref="Dispatcher"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfUiDispatcher : IUiDispatcher
{
	private readonly Dispatcher _dispatcher;

	public WpfUiDispatcher(Dispatcher dispatcher)
	{
		_dispatcher = dispatcher;
	}

	public bool CheckAccess() => _dispatcher.CheckAccess();

	public void Post(Action action)
	{
		if (_dispatcher.CheckAccess())
			action();
		else
			_dispatcher.BeginInvoke(action);
	}
}
