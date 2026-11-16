using System;
using System.Threading;

namespace TessynDesktop.Services;

/// <summary>How a visible start dealt with a daemon host that already runs.</summary>
/// <remarks>Created by Claude</remarks>
public enum TakeoverOutcome
{
	/// <summary>The guard is now held by this process: it is the daemon host.</summary>
	Acquired,

	/// <summary>The holder does not listen for an exit request: a visible window (never taken over) or an older background host. Nothing was asked of it.</summary>
	NotABackgroundHost,

	/// <summary>A background host was asked to exit and still holds the guard after the wait.</summary>
	DidNotExit,
}

/// <summary>
/// A visible Avalonia window takes over from a <b>background</b> host (side-by-side, WPF first). A background host listens on the named exit event; a visible window
/// does not create it, so "the event exists" is exactly "this holder may be asked to leave". The window signals it, waits for the guard to become free and takes it.
/// A holder without the event is never signalled, never waited for and never disturbed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class BackgroundHostTakeover
{
	/// <summary>
	/// Tries to become the host although <paramref name="guard"/> is held elsewhere (or, when it is not held any more, simply takes it).
	/// Retries the signal for <paramref name="signalWindow"/> first (a background host may still be creating its event, or may just have left), then waits up to
	/// <paramref name="exitTimeout"/> for the guard after a signal.
	/// </summary>
	public static TakeoverOutcome TryTakeOver(DaemonHostGuard guard, string exitEventName, TimeSpan signalWindow, TimeSpan exitTimeout, TimeSpan poll)
	{
		var signalled = false;
		var deadline = DateTime.UtcNow + signalWindow;
		while (true)
		{
			if (guard.TryAcquire())
				return TakeoverOutcome.Acquired;

			if (DaemonHostExitListener.Signal(exitEventName))
			{
				signalled = true;
				break;
			}

			if (DateTime.UtcNow >= deadline)
				break;

			Thread.Sleep(poll);
		}

		if (!signalled)
			return TakeoverOutcome.NotABackgroundHost;

		deadline = DateTime.UtcNow + exitTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (guard.TryAcquire())
				return TakeoverOutcome.Acquired;

			Thread.Sleep(poll);
		}

		return guard.TryAcquire() ? TakeoverOutcome.Acquired : TakeoverOutcome.DidNotExit;
	}
}
