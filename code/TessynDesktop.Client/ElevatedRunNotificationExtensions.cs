using System.Diagnostics.CodeAnalysis;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>Typed reader of the elevated-run notification (<c>elevated.changed</c>, tdcp-spec 10.A13).</summary>
public static class ElevatedRunNotificationExtensions
{
	/// <summary>A run changed status. Move the card between its states; the item list follows through <c>attention.changed</c>.</summary>
	public static bool TryGetElevatedChanged(this RemoteNotification notification, [NotNullWhen(true)] out ElevatedChangedPayload? payload) =>
		TurnNotificationExtensions.TryRead(notification, RemoteProtocol.ElevatedRuns.ChangedEvent, out payload);
}
