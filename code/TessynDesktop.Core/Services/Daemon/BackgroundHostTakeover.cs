using System;
using System.Threading;

namespace TessynDesktop.Services;

/// <summary>How a visible start dealt with a daemon host that already runs.</summary>
/// <remarks>Created by Claude</remarks>
public enum TakeoverOutcome
{
	/// <summary>The guard is now held by this process: it is the daemon host.</summary>
	Acquired,

	/// <summary>The holder does not listen for an exit request: a visible window (never taken over) or an older background host; or the holder became a visible window while we waited. Nothing was asked of it.</summary>
	NotABackgroundHost,

	/// <summary>A background host was asked to exit and still holds the guard after the wait.</summary>
	DidNotExit,

	/// <summary>The user said no to the takeover; the host was not signalled.</summary>
	Declined,
}

/// <summary>
/// A visible Avalonia window takes over from a <b>background</b> host (side-by-side, WPF first). A background host listens on the named exit event; a visible window
/// does not create it, so "the event exists" is exactly "this holder may be asked to leave". The window looks for the event, asks the user, signals it, waits for the
/// guard to become free and takes it. A holder without the event is never signalled, never waited for and never disturbed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class BackgroundHostTakeover
{
	/// <summary>How long the exit event may stay gone, with the guard still held, before the holder counts as a visible window that started in the meantime.</summary>
	public static readonly TimeSpan HolderChangedAfter = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Tries to become the host although <paramref name="guard"/> is held elsewhere (or, when it is not held any more, simply takes it).
	/// Looks for the exit event for <paramref name="signalWindow"/> (a background host may still be starting), then asks <paramref name="confirm"/> (null = yes);
	/// "no" ends with <see cref="TakeoverOutcome.Declined"/> and nothing is signalled. After the signal it waits up to <paramref name="exitTimeout"/> for the guard,
	/// calling <paramref name="onWaiting"/> before and <paramref name="onDone"/> after the wait (a progress window).
	/// </summary>
	public static TakeoverOutcome TryTakeOver(DaemonHostGuard guard, string exitEventName, TimeSpan signalWindow, TimeSpan exitTimeout, TimeSpan poll,
		Func<bool>? confirm = null, Action? onWaiting = null, Action? onDone = null, TimeSpan? holderChangedAfter = null)
	{
		var deadline = DateTime.UtcNow + signalWindow;
		var confirmed = false;
		while (true)
		{
			if (guard.TryAcquire())
				return TakeoverOutcome.Acquired;

			if (DaemonHostExitListener.IsListening(exitEventName))
			{
				// Ask once, before the first signal. The user may take a while; the host may leave on its own meanwhile (the loop then acquires).
				if (!confirmed)
				{
					if (confirm != null && !confirm())
						return TakeoverOutcome.Declined;

					confirmed = true;
				}

				if (DaemonHostExitListener.Signal(exitEventName))
					break;
			}
			else if (!confirmed && DateTime.UtcNow >= deadline)
			{
				return TakeoverOutcome.NotABackgroundHost;
			}

			Thread.Sleep(poll);
		}

		onWaiting?.Invoke();
		try
		{
			return WaitForGuard(guard, exitEventName, exitTimeout, poll, holderChangedAfter ?? HolderChangedAfter);
		}
		finally
		{
			onDone?.Invoke();
		}
	}

	private static TakeoverOutcome WaitForGuard(DaemonHostGuard guard, string exitEventName, TimeSpan exitTimeout, TimeSpan poll, TimeSpan holderChangedAfter)
	{
		var deadline = DateTime.UtcNow + exitTimeout;
		DateTime? eventGoneSince = null;
		while (DateTime.UtcNow < deadline)
		{
			if (guard.TryAcquire())
				return TakeoverOutcome.Acquired;

			// The host we signalled drops its event as it leaves. A guard that stays held with no event is someone else now: another window that won the race.
			if (DaemonHostExitListener.IsListening(exitEventName))
			{
				eventGoneSince = null;
			}
			else
			{
				eventGoneSince ??= DateTime.UtcNow;
				if (DateTime.UtcNow - eventGoneSince.Value >= holderChangedAfter)
					return TakeoverOutcome.NotABackgroundHost;
			}

			Thread.Sleep(poll);
		}

		return guard.TryAcquire() ? TakeoverOutcome.Acquired : TakeoverOutcome.DidNotExit;
	}
}
