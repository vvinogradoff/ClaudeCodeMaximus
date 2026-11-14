using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>IEventLog.ReadSince</c>: either the events after a resume point, or the reason they cannot be served.</summary>
/// <param name="Events">The events with a seq greater than the resume point, in order; empty when <paramref name="ResyncReason"/> is set.</param>
/// <param name="ResyncReason">Null when the ring covers the resume point; else one of <see cref="Constants.Remote.EventLog.ResyncReasons"/>.</param>
/// <param name="HeadSeq">Seq of the newest event at the time of the read.</param>
/// <remarks>Created by Claude</remarks>
public sealed record EventReplay(IReadOnlyList<RemoteNotification> Events, string? ResyncReason, long HeadSeq);
