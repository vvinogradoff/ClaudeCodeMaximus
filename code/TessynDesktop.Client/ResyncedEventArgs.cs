namespace TessynDesktop.Client;

/// <summary>
/// Raised when the consumer's picture of the daemon may be stale and must be reloaded (tree, open sessions, attention, usage):
/// the daemon sent a <c>resync</c> frame, or its run id changed across a reconnect.
/// </summary>
/// <param name="Reason">A <c>RemoteProtocol.ResyncReasons</c> value, or <see cref="RunChangedReason"/>.</param>
/// <param name="EventSeq">Seq of the newest event of the (new) run at the time; the stream continues after it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ResyncedEventArgs(string Reason, long EventSeq)
{
	/// <summary>The daemon restarted: its run id differs from the one this client saw before.</summary>
	public const string RunChangedReason = "run-changed";
}
