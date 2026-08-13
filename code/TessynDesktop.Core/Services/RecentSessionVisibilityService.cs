namespace TessynDesktop.Services;

/// <summary>
/// Determines whether a session belongs in the top-bar recent-sessions dropdown (FR.9.6, FR.9.7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class RecentSessionVisibilityService
{
	/// <summary>
	/// True when a session belongs in the dropdown: either it's a favorite (always included,
	/// regardless of recency or hidden state), or it's active within the last 24 hours and not hidden.
	/// </summary>
	public static bool ShouldShowInRecentDropdown(bool isFavorite, bool isRecentlyActive, bool isHiddenFromRecent) =>
		isFavorite || (isRecentlyActive && !isHiddenFromRecent);
}
