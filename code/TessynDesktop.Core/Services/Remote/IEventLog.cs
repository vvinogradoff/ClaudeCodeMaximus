using System;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Stamps every <see cref="IRemoteEventBus"/> notification with a monotonic <c>seq</c> (per daemon run) and keeps a bounded
/// ring of the latest ones, so a reconnecting client can resume instead of re-reading everything. Transports read stamped
/// events from here, never from the bus directly.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IEventLog
{
	/// <summary>Seq of the newest event; 0 before the first one.</summary>
	long HeadSeq { get; }

	/// <summary>Raised for every stamped notification after it entered the ring, in seq order. Same rules as <see cref="IRemoteEventBus.Published"/>: subscribers must return quickly.</summary>
	event Action<RemoteNotification>? Appended;

	/// <summary>
	/// The events after <paramref name="lastSeq"/>. When the ring no longer holds the first of them, or <paramref name="lastSeq"/> is
	/// ahead of the head (a client of an earlier run), the result carries a resync reason instead of events.
	/// </summary>
	EventReplay ReadSince(long lastSeq);
}
