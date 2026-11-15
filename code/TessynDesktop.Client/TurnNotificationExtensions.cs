using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

/// <summary>
/// Reads the turn notifications (P1.I) of <see cref="ITessynClient.NotificationReceived"/> as typed payloads. Each method returns false
/// when the notification is another method or its params do not fit.
/// </summary>
public static class TurnNotificationExtensions
{
	public static bool TryGetTurnStarted(this RemoteNotification notification, [NotNullWhen(true)] out TurnStartedPayload? payload) =>
		TryRead(notification, RemoteProtocol.TurnStream.StartedEvent, out payload);

	public static bool TryGetTurnDelta(this RemoteNotification notification, [NotNullWhen(true)] out TurnDeltaPayload? payload) =>
		TryRead(notification, RemoteProtocol.TurnStream.DeltaEvent, out payload);

	public static bool TryGetTurnTool(this RemoteNotification notification, [NotNullWhen(true)] out TurnToolPayload? payload) =>
		TryRead(notification, RemoteProtocol.TurnStream.ToolEvent, out payload);

	public static bool TryGetTurnStatus(this RemoteNotification notification, [NotNullWhen(true)] out TurnStatusPayload? payload) =>
		TryRead(notification, RemoteProtocol.TurnStream.StatusEvent, out payload);

	public static bool TryGetTurnUsage(this RemoteNotification notification, [NotNullWhen(true)] out TurnUsagePayload? payload) =>
		TryRead(notification, RemoteProtocol.TurnStream.UsageEvent, out payload);

	/// <summary>The end of a turn; <see cref="TurnCompletedPayload.TurnId"/> pairs it with the others (null from a daemon that does not send it).</summary>
	public static bool TryGetTurnCompleted(this RemoteNotification notification, [NotNullWhen(true)] out TurnCompletedPayload? payload) =>
		TryRead(notification, RemoteProtocol.Events.TurnCompleted, out payload);

	/// <summary>Reads a notification's params as <typeparamref name="T"/> when it is <paramref name="method"/>; shared with the other typed notification readers.</summary>
	internal static bool TryRead<T>(this RemoteNotification notification, string method, [NotNullWhen(true)] out T? payload)
		where T : class
	{
		payload = null;
		if (notification.Method != method)
			return false;

		try
		{
			payload = notification.Params.Deserialize<T>(TdcpJsonOptions.Default);
			return payload != null;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
