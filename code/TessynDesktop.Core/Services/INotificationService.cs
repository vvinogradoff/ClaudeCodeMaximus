using System;

namespace TessynDesktop.Services;

/// <summary>
/// Shows OS-level desktop notifications (FR.16) — the desktop copy of a <c>notify_user</c> message (FR.21.5).
/// Turn completion is never announced.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface INotificationService
{
	/// <summary>
	/// Shows a toast for a <c>notify_user</c> message (FR.21.5). Gated on <c>NotificationsEnabled</c>;
	/// swallows any toast-system errors to remain non-fatal.
	/// </summary>
	/// <param name="nodeId">NodeId of the session — embedded in the toast for click routing.</param>
	/// <param name="title">Toast title — the session's topic name (<c>{directory label} / {session name}</c>).</param>
	/// <param name="message">Message text; truncated to <see cref="Constants.Notifications.MaxBodyChars"/> chars.</param>
	/// <returns>True when a toast was shown; false when notifications are disabled or the toast system failed.</returns>
	bool ShowUserNotification(string nodeId, string title, string message);

	/// <summary>
	/// Registers a callback that is invoked when the user clicks a toast notification.
	/// The callback receives the <paramref name="onNodeIdActivated"/> NodeId from the toast arguments.
	/// Must be called once before any notifications are shown (typically in App startup).
	/// </summary>
	void RegisterActivationHandler(Action<string> onNodeIdActivated);
}
