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

	/// <summary>True when this is the first instance. Otherwise the first instance has been asked to activate itself (or could not be reached: it runs elevated).</summary>
	public bool TryAcquire()
	{
		bool createdNew;
		try
		{
			_mutex = new Mutex(initiallyOwned: true, _name, out createdNew);
		}
		catch (UnauthorizedAccessException)
		{
			// The name belongs to an instance of another integrity level (elevated): treat it as running.
			return false;
		}

		try
		{
			_activate = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, _name + ActivateSuffix);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
		{
			_activate = null;
			if (createdNew)
				return true; // first instance, only without the activation channel

			_mutex.Dispose();
			_mutex = null;
			return false;
		}

		if (createdNew)
			return true;

		// The second process owns the foreground right; hand it to the first so it can raise its window.
		AllowSetForegroundWindow(AllowAnyProcess);
		_activate.Set();
		_mutex.Dispose();
		_mutex = null;
		_activate.Dispose();
		_activate = null;
		return false;
	}

	private const uint AllowAnyProcess = uint.MaxValue;

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
	private static extern bool AllowSetForegroundWindow(uint processId);

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
