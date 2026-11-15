using System;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The <see cref="INotificationService"/> of the daemon (package J2): publishes every notification the host would toast as the local-stream event
/// <c>notification.raised</c>. While a connected client presents toasts it waits up to <see cref="Constants.Composer.NotificationAckTimeout"/> for that
/// client's <c>notification.ack</c>; an acknowledged notification gets no host toast (the user never gets two), an unacknowledged one is shown by the
/// host after the wait, so a hung or just-killed client never loses it (V3). The enable switch (<c>NotificationsEnabled</c>) gates all of it. The
/// returned flag means "shown": by the host, or acknowledged by a client; so <c>notify_user</c> reports the desktop channel only then.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotificationFanOut : INotificationService
{
	private readonly INotificationService _host;
	private readonly IClientPresenceRegistry _presence;
	private readonly IRemoteEventBus _bus;
	private readonly IAppSettingsService _appSettings;
	private readonly INotificationAckBook _acks;
	private readonly TimeProvider _time;
	private readonly TimeSpan _ackTimeout;

	public NotificationFanOut(
		INotificationService host, IClientPresenceRegistry presence, IRemoteEventBus bus, IAppSettingsService appSettings, INotificationAckBook acks,
		TimeProvider? time = null, TimeSpan? ackTimeout = null)
	{
		_host        = host;
		_presence    = presence;
		_bus         = bus;
		_appSettings = appSettings;
		_acks        = acks;
		_time        = time ?? TimeProvider.System;
		_ackTimeout  = ackTimeout ?? Constants.Composer.NotificationAckTimeout;
	}

	/// <inheritdoc />
	public bool ShowUserNotification(string nodeId, string title, string message)
	{
		if (!_appSettings.Settings.NotificationsEnabled)
			return false;

		var notificationId = Guid.NewGuid().ToString("N");

		// Only a connected client that presents toasts is waited for; the wait starts before the event is out so that an instant ack counts.
		using var pending = _presence.IsPresented(RemoteProtocol.Notifications.ToastChannel) ? _acks.Expect(notificationId) : null;

		_bus.Publish(
			RemoteProtocol.Notifications.RaisedEvent,
			new NotificationRaisedPayload(
				notificationId, string.IsNullOrEmpty(nodeId) ? null : nodeId, title, message,
				RemoteProtocol.Notifications.UserNotificationKind, _time.GetUtcNow()));

		if (pending != null && pending.Wait(_ackTimeout))
			return true; // a client showed it

		return _host.ShowUserNotification(nodeId, title, message);
	}

	/// <inheritdoc />
	public void RegisterActivationHandler(Action<string> onNodeIdActivated) => _host.RegisterActivationHandler(onNodeIdActivated);
}
