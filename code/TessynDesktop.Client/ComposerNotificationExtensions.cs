using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

/// <summary>Typed readers of the composer notifications of package J2 (<c>draft.changed</c>).</summary>
public static class ComposerNotificationExtensions
{
	/// <summary>A session's draft changed (any writer, this client included): compare <see cref="DraftChangedPayload.Revision"/> with the one you last read or wrote.</summary>
	public static bool TryGetDraftChanged(this RemoteNotification notification, [NotNullWhen(true)] out DraftChangedPayload? payload)
	{
		payload = null;
		if (notification.Method != RemoteProtocol.Composer.DraftChangedEvent)
			return false;

		try
		{
			payload = notification.Params.Deserialize<DraftChangedPayload>(TdcpJsonOptions.Default);
			return payload != null;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// A notification the host would have shown as a toast (package J2, <c>notifications.local</c>). Show it once per <see cref="NotificationRaisedPayload.NotificationId"/>
	/// and drop it when <see cref="NotificationRaisedPayload.RaisedUtc"/> is much older than now (a replay after a reconnect).
	/// </summary>
	/// <summary>The Focus item sound cue (FR.22.6).</summary>
	public static bool TryGetFocusItemCreated(this RemoteNotification notification, [NotNullWhen(true)] out FocusItemCreatedPayload? payload) =>
		TurnNotificationExtensions.TryRead(notification, RemoteProtocol.Notifications.FocusItemCreatedEvent, out payload);

	public static bool TryGetNotificationRaised(this RemoteNotification notification, [NotNullWhen(true)] out NotificationRaisedPayload? payload)
	{
		payload = null;
		if (notification.Method != RemoteProtocol.Notifications.RaisedEvent)
			return false;

		try
		{
			payload = notification.Params.Deserialize<NotificationRaisedPayload>(TdcpJsonOptions.Default);
			return payload != null;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
