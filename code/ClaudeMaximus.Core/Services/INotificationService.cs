using System;

namespace ClaudeMaximus.Services;

/// <summary>
/// Shows OS-level desktop notifications for completed scheduled and orchestrated turns (FR.16).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface INotificationService
{
	/// <summary>
	/// Shows a toast notification for a completed turn result.
	/// Suppressed automatically when notifications are disabled in settings or when the
	/// assistant text is empty. Swallows any toast-system errors to remain non-fatal.
	/// </summary>
	/// <param name="nodeId">NodeId of the target session — embedded in toast for click routing.</param>
	/// <param name="sessionName">Displayed as the toast title.</param>
	/// <param name="assistantText">Response text; truncated to <see cref="Constants.Notifications.MaxBodyChars"/> chars.</param>
	/// <param name="note">Optional schedule note shown as attribution footer.</param>
	void ShowResult(string nodeId, string sessionName, string assistantText, string? note);

	/// <summary>
	/// Registers a callback that is invoked when the user clicks a toast notification.
	/// The callback receives the <paramref name="onNodeIdActivated"/> NodeId from the toast arguments.
	/// Must be called once before any notifications are shown (typically in App startup).
	/// </summary>
	void RegisterActivationHandler(Action<string> onNodeIdActivated);
}
