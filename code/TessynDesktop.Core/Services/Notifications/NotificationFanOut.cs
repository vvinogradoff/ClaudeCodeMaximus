using System;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The <see cref="INotificationService"/> of the daemon (package J2): publishes every notification the host would toast as the local-stream event
/// <c>notification.raised</c>, and shows the host's own toast only when no connected client has registered the <c>toast</c> channel, so the user
/// never gets two. The enable switch (<c>NotificationsEnabled</c>) gates both. The returned flag still means "delivered on the desktop", by the
/// host or by a client.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotificationFanOut : INotificationService
{
	private readonly INotificationService _host;
	private readonly IClientPresenceRegistry _presence;
	private readonly IRemoteEventBus _bus;
	private readonly IAppSettingsService _appSettings;
	private readonly TimeProvider _time;

	public NotificationFanOut(INotificationService host, IClientPresenceRegistry presence, IRemoteEventBus bus, IAppSettingsService appSettings, TimeProvider? time = null)
	{
		_host        = host;
		_presence    = presence;
		_bus         = bus;
		_appSettings = appSettings;
		_time        = time ?? TimeProvider.System;
	}

	/// <inheritdoc />
	public bool ShowUserNotification(string nodeId, string title, string message)
	{
		if (!_appSettings.Settings.NotificationsEnabled)
			return false;

		_bus.Publish(
			RemoteProtocol.Notifications.RaisedEvent,
			new NotificationRaisedPayload(
				Guid.NewGuid().ToString("N"), string.IsNullOrEmpty(nodeId) ? null : nodeId, title, message,
				RemoteProtocol.Notifications.UserNotificationKind, _time.GetUtcNow()));

		// A registered client shows it: the host stays silent.
		if (_presence.IsPresented(RemoteProtocol.Notifications.ToastChannel))
			return true;

		return _host.ShowUserNotification(nodeId, title, message);
	}

	/// <inheritdoc />
	public void RegisterActivationHandler(Action<string> onNodeIdActivated) => _host.RegisterActivationHandler(onNodeIdActivated);
}
