using System.Text.Json;
using TessynDesktop.Services;

namespace TessynDesktop.Models.Remote;

/// <summary>Typed access to the params of notifications that clients act on structurally (P1.F).</summary>
/// <remarks>Created by Claude</remarks>
public static class RemoteNotificationPayloads
{
	/// <summary>
	/// The typed params of an <c>entries.appended</c> notification; null when <paramref name="notification"/> is another event or
	/// its params are malformed.
	/// </summary>
	public static EntriesAppendedPayload? AsEntriesAppended(this RemoteNotification notification)
	{
		if (notification.Method != RemoteProtocol.Events.EntriesAppended)
			return null;

		try
		{
			return notification.Params.Deserialize<EntriesAppendedPayload>(TdcpJsonOptions.Default);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>The typed params of a <c>session.reset</c> notification; null when it is another event or malformed.</summary>
	public static SessionResetPayload? AsSessionReset(this RemoteNotification notification)
	{
		if (notification.Method != RemoteProtocol.Events.SessionReset)
			return null;

		try
		{
			return notification.Params.Deserialize<SessionResetPayload>(TdcpJsonOptions.Default);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
