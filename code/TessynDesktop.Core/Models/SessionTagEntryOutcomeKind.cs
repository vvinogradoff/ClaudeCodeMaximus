namespace TessynDesktop.Models;

/// <summary>What happened to one <c>tag_session</c> entry (FR.24.4).</summary>
/// <remarks>Created by Claude</remarks>
public enum SessionTagEntryOutcomeKind
{
	Tagged,
	AlreadyTagged,
	Removed,
	NotTaggedInvalid,
	NotTaggedUnknownTeam,
	NotRemovedNotTagged,
	NotRemovedFromName,
	NotRemovedInvalid,
}
