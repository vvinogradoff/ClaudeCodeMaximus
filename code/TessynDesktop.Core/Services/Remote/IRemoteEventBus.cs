using System;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The daemon's outbound notification channel for remote clients (contract section 4). Producers may call it from any
/// thread, including while holding the settings lock; it never blocks them and never throws into them.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRemoteEventBus
{
	/// <summary>
	/// Raised for every published notification, on the publisher's thread (or on a timer thread for coalesced ones).
	/// Subscribers must return quickly — hand the notification to a queue — and a throwing subscriber is ignored.
	/// </summary>
	event Action<RemoteNotification>? Published;

	/// <summary>Serializes <paramref name="payload"/> with the shared wire options and raises <see cref="Published"/> at once.</summary>
	void Publish(string method, object payload);

	/// <summary>
	/// Publishes <paramref name="method"/> once after <paramref name="window"/>, however often it is called in between for the
	/// same <paramref name="method"/> and <paramref name="key"/>. <paramref name="payloadFactory"/> runs once when the window
	/// ends, on a timer thread, so the payload reflects the latest state; a factory that returns null publishes nothing.
	/// </summary>
	void PublishCoalesced(string method, string key, Func<object?> payloadFactory, TimeSpan window);

	/// <summary>
	/// Publishes the pending coalesced notification of <paramref name="method"/> and <paramref name="key"/> now, on the calling
	/// thread, instead of waiting for its window. Does nothing when none is pending. Lets a later notification (such as
	/// <c>turn.completed</c>) go out after the ones it must not overtake.
	/// </summary>
	void FlushPending(string method, string key);
}
