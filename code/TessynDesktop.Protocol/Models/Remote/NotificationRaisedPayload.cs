namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>notification.raised</c>: a notification the host would show as a toast.</summary>
/// <param name="NotificationId">Unique id; a client that sees one twice (a replay after a reconnect) shows it once.</param>
/// <param name="NodeId">The session it belongs to, when there is one (clicking the toast selects it).</param>
/// <param name="Title">Toast title.</param>
/// <param name="Message">Message text, not truncated.</param>
/// <param name="Kind">See <see cref="RemoteProtocol.Notifications.UserNotificationKind"/>.</param>
/// <param name="RaisedUtc">When the daemon raised it: a client drops a notification that is much older than the moment it receives it (a replay).</param>
/// <remarks>Created by Claude</remarks>
public sealed record NotificationRaisedPayload(string NotificationId, string? NodeId, string Title, string Message, string Kind, DateTimeOffset RaisedUtc);
