using System;
using System.Threading;

namespace TessynDesktop.Services;

/// <summary>
/// One daemon host per Windows user session. The daemon host process (<c>TessynDesktop.exe --background</c>) hosts the daemon (MCP server, scheduler, relay, Telegram, sessions); a second host would
/// fight the first over the listener port and overwrite <c>daemon.json</c>. The first process holds a named mutex for its whole life; a later one finds it
/// taken and must exit. Whichever host started first is the daemon, and a later start (the WPF client's, or an old shortcut) leaves it alone.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostGuard : IDisposable
{
	/// <summary>The mutex name of the real daemon host (session-local: one per logged-on user).</summary>
	public const string DefaultName = TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostMutexName;

	private readonly string _name;
	private Mutex? _mutex;

	public DaemonHostGuard() : this(DefaultName)
	{
	}

	/// <summary>A guard with its own name (tests use a unique one so they never meet a real host).</summary>
	public DaemonHostGuard(string name)
	{
		_name = name;
	}

	/// <summary>True when this process is now the daemon host; false when another host already holds the guard.</summary>
	public bool TryAcquire()
	{
		if (_mutex != null)
			return true;

		try
		{
			var mutex = new Mutex(initiallyOwned: true, _name, out var createdNew);
			if (!createdNew)
			{
				mutex.Dispose();
				return false;
			}

			_mutex = mutex;
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			// The name belongs to a host of another integrity level (elevated): treat it as running.
			return false;
		}
	}

	/// <summary>True when some process (not this one) holds the guard. Does not take it.</summary>
	public static bool IsHeldElsewhere(string name)
	{
		try
		{
			using var existing = Mutex.OpenExisting(name);
			return true;
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}

	public void Dispose()
	{
		var mutex = _mutex;
		_mutex = null;
		if (mutex == null)
			return;

		try
		{
			mutex.ReleaseMutex();
		}
		catch (ApplicationException)
		{
			// Released from another thread than the one that acquired it: the OS frees the mutex when the process ends.
		}

		mutex.Dispose();
	}
}
