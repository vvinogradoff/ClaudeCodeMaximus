namespace TessynDesktop.Models;

/// <summary>
/// A single entry in the Profile dropdown (Default, a stored profile, or "New...").
/// Carries the profile's identity alongside its display name so context-menu actions
/// (rename/delete) target the exact underlying profile rather than matching by display name,
/// which is not guaranteed unique.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileListItemModel(string displayName, string? profileId, bool isSystem = false)
{
	/// <summary>Text shown in the dropdown.</summary>
	public string DisplayName { get; } = displayName;

	/// <summary>Underlying profile ID, or null for the "Default" and "New..." entries.</summary>
	public string? ProfileId { get; } = profileId;

	/// <summary>True for the "Default" entry — the base, non-isolated claude auth identity. Styled distinctly and exempt from the rename/delete context menu.</summary>
	public bool IsSystem { get; } = isSystem;

	public override string ToString() => DisplayName;
}
