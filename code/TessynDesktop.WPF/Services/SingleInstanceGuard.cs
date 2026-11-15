namespace TessynDesktop.WPF.Services;

/// <summary>
/// One running app per data root: the first instance holds a named mutex; a second one signals the first (a named event) to come to the
/// front and exits. The names come from <c>StartupOptions.MutexName</c>, so a test run with its own data root never meets the user's app.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SingleInstanceGuard : IDisposable
{
	private const string ActivateSuffix = ".Activate";

	private readonly string _name;
	private Mutex? _mutex;
	private EventWaitHandle? _activate;
	private RegisteredWaitHandle? _wait;

	public SingleInstanceGuard(string name)
	{
		_name = name;
	}

	/// <summary>True when this is the first instance. Otherwise the first instance has been asked to activate itself.</summary>
	public bool TryAcquire()
	{
		_mutex = new Mutex(initiallyOwned: true, _name, out var createdNew);
		_activate = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, _name + ActivateSuffix);
		if (createdNew)
			return true;

		_activate.Set();
		_mutex.Dispose();
		_mutex = null;
		_activate.Dispose();
		_activate = null;
		return false;
	}

	/// <summary>Runs <paramref name="onActivate"/> (on a thread-pool thread) each time another instance was started.</summary>
	public void ListenForActivation(Action onActivate)
	{
		if (_activate == null)
			return;

		_wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
	}

	public void Dispose()
	{
		_wait?.Unregister(null);
		_activate?.Dispose();
		if (_mutex == null)
			return;

		try
		{
			_mutex.ReleaseMutex();
		}
		catch (ApplicationException)
		{
			// Released from another thread than the one that acquired it: the OS frees the mutex when the process ends.
		}

		_mutex.Dispose();
	}
}
