namespace TessynDesktop.Models;

/// <summary>
/// The profile a session turn runs on (ISSUE.013). Either resolved (a config dir to launch with; null = the Default profile) or an
/// error that names the profile and what to do - a pinned profile that does not resolve is never replaced by Default.
/// </summary>
/// <param name="ProfileId"><c>"default"</c> or a stored profile's id.</param>
/// <param name="DisplayName">The profile's display name (<c>"default"</c> for the Default profile).</param>
/// <param name="ConfigDir">The <c>CLAUDE_CONFIG_DIR</c>; null for the Default profile.</param>
/// <param name="IsPinned">True when the session itself pins the profile (by id or by an old display name).</param>
/// <param name="Error">Why the pinned profile cannot be used; null when resolved.</param>
/// <param name="IsLoggedOut">True when the stored profile has no credentials (only computed when asked for).</param>
/// <param name="Note">Set when the turn runs on Default because a legacy (name-only) recorded profile matches no stored profile; the result must say so.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileResolution(
	string ProfileId,
	string DisplayName,
	string? ConfigDir,
	bool IsPinned,
	string? Error = null,
	bool IsLoggedOut = false,
	string? Note = null)
{
	/// <summary>True when the turn may run on <see cref="ConfigDir"/>.</summary>
	public bool IsResolved => Error == null;

	/// <summary>True for the Default (system) profile.</summary>
	public bool IsDefault => ProfileId == Constants.ProfileSwitch.DefaultProfileId;
}
