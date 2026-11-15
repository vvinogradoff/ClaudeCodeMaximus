using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// Local notification presence (package J2). A client that shows toasts or plays sounds itself says so; the desktop host then stays silent for those
/// channels while this client's stream is open, and the daemon publishes <c>notification.raised</c>. Announced by the <c>notifications.local</c>
/// capability; loopback only.
/// </summary>
public partial interface ITessynClient
{
	/// <summary>
	/// <c>client.register</c>: sets the channels (<c>"toast"</c>, <c>"sound"</c>) this client presents, replacing its earlier registration; an empty
	/// list unregisters. Needs <see cref="TessynClientOptions.ClientId"/>. The registration counts only while the notification stream is open
	/// (it is re-sent with every hello, so a reconnect keeps it); a client that disappears stops silencing the host without unregistering.
	/// </summary>
	Task<ClientRegisterResult> ClientRegisterAsync(IReadOnlyList<string> presents, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>notification.ack { notificationId }</c>: tells the daemon this client showed the toast of a <c>notification.raised</c>. Send it right after showing it:
	/// the host waits about 3 s and shows its own toast when no ack came. A client drops a replayed notification older than 30 s
	/// (<see cref="NotificationRaisedPayload.IsReplay"/>) without showing or acknowledging it.
	/// </summary>
	Task<NotificationAckResult> NotificationAckAsync(string notificationId, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>notification.ack { nodeId }</c>: tells the daemon this client played the completion sound of that session (it reacts to <c>turn.completed</c>). The host
	/// plays the sound itself when no ack comes within about 3 s.
	/// </summary>
	Task<NotificationAckResult> NotificationSoundAckAsync(string nodeId, CancellationToken cancellationToken = default);
}
