namespace TessynDesktop.Services;

/// <summary>
/// Determines whether a session belongs in the top-bar recent-sessions dropdown (FR.9.6, FR.9.7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class RecentSessionVisibilityService
{
	/// <summary>
	/// True when a session belongs in the dropdown: it's pinned (see <see cref="IsPinnedInRecentDropdown"/>),
	/// or it's active within the last 24 hours and not hidden.
	/// </summary>
	public static bool ShouldShowInRecentDropdown(bool isFavorite, bool hasOpenAttention, bool hasSchedules,
		bool isRecentlyActive, bool isHiddenFromRecent) =>
		IsPinnedInRecentDropdown(isFavorite, hasOpenAttention, hasSchedules) || (isRecentlyActive && !isHiddenFromRecent);

	/// <summary>
	/// True when a session stays in the dropdown regardless of recency or hidden state: a favorite, or a session
	/// with open Focus items or schedules. Hiding it would have no visible effect, so its row offers no hide button.
	/// </summary>
	public static bool IsPinnedInRecentDropdown(bool isFavorite, bool hasOpenAttention, bool hasSchedules) =>
		isFavorite || hasOpenAttention || hasSchedules;
}
