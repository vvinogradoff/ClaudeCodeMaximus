using System.Diagnostics.CodeAnalysis;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>Reads the package J4 notification of <see cref="ITessynClient.NotificationReceived"/> as a typed payload; false when it is another method or does not fit.</summary>
public static class ScheduleNotificationExtensions
{
	/// <summary><c>schedule.changed</c>: a schedule was added, removed or fired (reload <c>SchedulesListAsync</c>); <see cref="ScheduleChangedPayload.NodeId"/> names the session when exactly one is affected.</summary>
	public static bool TryGetScheduleChanged(this RemoteNotification notification, [NotNullWhen(true)] out ScheduleChangedPayload? payload) =>
		notification.TryRead(RemoteProtocol.Schedules.ChangedEvent, out payload);
}
