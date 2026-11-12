namespace TessynDesktop.Services;

/// <summary>
/// Determines whether a session belongs in the top-bar recent-sessions dropdown (FR.9.6, FR.9.7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class RecentSessionVisibilityService
{
	/// <summary>A session is "recently active" (FR.9.6) when its Last Activity is at most this many hours old.</summary>
	public const int RecentWindowHours = 24;

	/// <summary>
	/// FR.9.6 — Last Activity within <see cref="RecentWindowHours"/> of <paramref name="now"/>. The same one-line rule as
	/// <c>SessionNodeViewModel.RefreshRecencyBrush</c> (kept there unchanged: the desktop view-model is not refactored for this).
	/// </summary>
	public static bool IsRecentlyActive(System.DateTimeOffset? lastActivity, System.DateTimeOffset now) =>
		lastActivity.HasValue && (now - lastActivity.Value).TotalHours <= RecentWindowHours;

	/// <summary>
	/// FR.9.6 — hidden by the user and no activity since: <paramref name="recentIgnoredAt"/> is set and not older than the Last Activity
	/// (or there is none). The same rule as <c>SessionNodeViewModel.IsHiddenFromRecent</c>.
	/// </summary>
	public static bool IsHiddenFromRecent(System.DateTimeOffset? recentIgnoredAt, System.DateTimeOffset? lastActivity) =>
		recentIgnoredAt.HasValue && (!lastActivity.HasValue || recentIgnoredAt.Value >= lastActivity.Value);

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

	/// <summary>
	/// FR.1.16 — on switching to the Recent tab a directory group is expanded iff no session is selected
	/// (<paramref name="selectedSessionDirectoryPath"/> == null) or it is the selected session's directory (OrdinalIgnoreCase).
	/// </summary>
	public static bool IsGroupExpandedOnRecentTabSwitch(string groupDirectoryPath, string? selectedSessionDirectoryPath) =>
		selectedSessionDirectoryPath == null
		|| string.Equals(groupDirectoryPath, selectedSessionDirectoryPath, System.StringComparison.OrdinalIgnoreCase);
}
