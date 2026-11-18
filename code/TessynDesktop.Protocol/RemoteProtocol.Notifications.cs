namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Local notifications of package J2: a client that can present toasts and sounds registers for them (<c>client.register</c>, or the optional
	/// <c>clientId</c> / <c>presents</c> params of <c>client.hello</c>); while one is registered and connected the desktop host stays silent for that
	/// channel and the daemon publishes each notification as <c>notification.raised</c> on the local stream. Loopback only: the relay cannot register.
	/// </summary>
	public static class Notifications
	{
		/// <summary>The daemon knows presenting clients: <c>client.register</c> and <c>notification.raised</c>.</summary>
		public const string Capability = "notifications.local";

		/// <summary><c>client.register { clientId, presents[] }</c> answers <c>{ ok }</c>. An empty <c>presents</c> unregisters. Loopback only.</summary>
		public const string RegisterMethod = "client.register";

		/// <summary>
		/// Notification <c>{ notificationId, nodeId?, title, message, kind, raisedUtc }</c>, published beside every desktop notification the host would show. Local
		/// stream only. A client that presents <c>toast</c> shows it, then acknowledges it (<see cref="AckMethod"/>); it drops a replayed one older than
		/// <see cref="MaxReplayAge"/>.
		/// </summary>
		public const string RaisedEvent = "notification.raised";

		/// <summary>
		/// <c>notification.ack { notificationId }</c> or <c>{ nodeId }</c> answers <c>{ acknowledged }</c>. A client that presents <c>toast</c> sends the first after
		/// it showed a <c>notification.raised</c>; a client that presents <c>sound</c> sends the second after it handled a <c>focus.itemCreated</c> of that session (played the Focus item sound, or stayed silent by its own rule).
		/// The host waits about 3 s for it and shows the toast or plays the sound itself when none came, so an unresponsive client never loses a notification.
		/// Loopback only.
		/// </summary>
		public const string AckMethod = "notification.ack";

		/// <summary>
		/// The replay rule: the event log replays <c>notification.raised</c> after a reconnect, so a client drops one whose <c>raisedUtc</c> is older than this
		/// when it receives it (and shows each <c>notificationId</c> once). Use <see cref="NotificationRaisedPayload.IsReplay"/>.
		/// </summary>
		public static readonly TimeSpan MaxReplayAge = TimeSpan.FromSeconds(30);

		/// <summary>Query parameter of the SSE request that names the stream's client; a registration is effective only while a stream with its <c>clientId</c> is open.</summary>
		public const string ClientIdQuery = "clientId";

		/// <summary>Channel: the client shows the toast (the host does not).</summary>
		public const string ToastChannel = "toast";

		/// <summary>Channel: the client plays the sounds (the host does not): the Focus item sound (FR.22).</summary>
		public const string SoundChannel = "sound";

		/// <summary>
		/// Notification <c>{ nodeId, itemId, kind, raisedUtc }</c> (FR.22.6): a new Focus item was created in a session whose "Focus item sound" switch is on, and the
		/// daemon's global throttle let it through. Local stream only. A client that presents <c>sound</c> plays the sound and then acknowledges it with
		/// <c>notification.ack { nodeId }</c>; the host plays it itself when none came. It drops a replayed event older than <see cref="MaxReplayAge"/>.
		/// </summary>
		public const string FocusItemCreatedEvent = "focus.itemCreated";

		/// <summary>The Focus item sound throttle of a fresh install, seconds (FR.22.3): one sound per burst.</summary>
		public const int DefaultSoundThrottleSeconds = 3;

		/// <summary>The default of the per-session completion sound that came before: a stored value of it is read as <see cref="DefaultSoundThrottleSeconds"/> (FR.22.3).</summary>
		public const int LegacySoundThrottleSeconds = 180;

		/// <summary>The throttle seconds a stored setting stands for: the old 3-minute default reads as the new default, any other value as itself.</summary>
		public static int EffectiveSoundThrottleSeconds(int stored) =>
			stored == LegacySoundThrottleSeconds ? DefaultSoundThrottleSeconds : Math.Max(0, stored);

		/// <summary>Kind of the toast of a <c>notify_user</c> message (FR.21.5).</summary>
		public const string UserNotificationKind = "userNotification";

		/// <summary>The channels a client may register for.</summary>
		public static readonly IReadOnlyList<string> Channels = [ToastChannel, SoundChannel];

		/// <summary>The notifications that only the local stream carries (the relay filters them out).</summary>
		public static readonly IReadOnlyList<string> LocalOnlyEvents = [RaisedEvent, FocusItemCreatedEvent];
	}
}
