using System;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// How the background host ends (A23 Q6). Two doors: the tray icon's Exit, which asks first when sessions are active, and a client's request through the
/// named exit event (the client has already asked the user). Both terminate the active sessions the way the window's close does, then shut down.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostExit
{
	private readonly Func<int> _activeSessions;
	private readonly Func<string, Task<bool>> _confirm;
	private readonly Action _terminateSessions;
	private readonly Action _shutdown;
	private readonly Action<Exception>? _onError;
	private int _exiting;
	private int _asking;

	public DaemonHostExit(Func<int> activeSessions, Func<string, Task<bool>> confirm, Action terminateSessions, Action shutdown, Action<Exception>? onError = null)
	{
		_onError = onError;
		_activeSessions = activeSessions;
		_confirm = confirm;
		_terminateSessions = terminateSessions;
		_shutdown = shutdown;
	}

	/// <summary>The question the tray asks when <paramref name="count"/> sessions are active.</summary>
	public static string ConfirmMessage(int count)
	{
		var noun = count == 1 ? "session is" : "sessions are";
		return $"There are {count} Claude Code {noun} currently active.\nAre you sure you want to terminate them and exit the Tessyn daemon?";
	}

	/// <summary>The tray's Exit: nothing active exits at once; otherwise the user must confirm, and a refusal changes nothing. True when the host is exiting.</summary>
	public async Task<bool> RequestFromTrayAsync()
	{
		// A second click while the question is open must not open a second window.
		if (Interlocked.Exchange(ref _asking, 1) == 1)
			return false;

		try
		{
			var count = _activeSessions();
			if (count > 0 && !await _confirm(ConfirmMessage(count)))
				return false;

			Exit();
			return true;
		}
		finally
		{
			Interlocked.Exchange(ref _asking, 0);
		}
	}

	/// <summary>Terminates the active sessions and shuts the host down, once, without asking (a client's request, or a confirmed tray Exit).</summary>
	public void Exit()
	{
		if (Interlocked.Exchange(ref _exiting, 1) == 1)
			return;

		try
		{
			_terminateSessions();
		}
		catch (Exception ex)
		{
			// A session in an odd state must never keep the host from ending: log it and shut down anyway.
			_onError?.Invoke(ex);
		}
		finally
		{
			_shutdown();
		}
	}
}

/// <summary>The host's side of the named exit event: waits for a client to set it, then runs a callback. The event is created by the host and lives as long as it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostExitListener : IDisposable
{
	private readonly string _name;
	private EventWaitHandle? _event;
	private RegisteredWaitHandle? _wait;

	public DaemonHostExitListener() : this(TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostExitEventName)
	{
	}

	public DaemonHostExitListener(string name)
	{
		_name = name;
	}

	/// <summary>Starts listening; <paramref name="onRequest"/> runs on a thread-pool thread each time a client signals.</summary>
	public void Start(Action onRequest)
	{
		_event = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, _name);
		_wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onRequest(), null, Timeout.Infinite, executeOnlyOnce: false);
	}

	/// <summary>Asks the host that listens on <paramref name="name"/> to exit. False when no host listens (an older host, or a visible Avalonia window).</summary>
	public static bool Signal(string name)
	{
		try
		{
			using var existing = EventWaitHandle.OpenExisting(name);
			return existing.Set();
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>True when a host listens on <paramref name="name"/>. Looks only: nothing is signalled.</summary>
	public static bool IsListening(string name)
	{
		try
		{
			using var existing = EventWaitHandle.OpenExisting(name);
			return true;
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	public void Dispose()
	{
		_wait?.Unregister(null);
		_wait = null;
		_event?.Dispose();
		_event = null;
	}
}
