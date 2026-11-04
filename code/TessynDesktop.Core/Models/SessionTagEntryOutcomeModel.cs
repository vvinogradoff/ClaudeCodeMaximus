namespace TessynDesktop.Models;

/// <summary>
/// The outcome of one entry. <see cref="Entry"/> is the trimmed input; <see cref="Tag"/> is set for the
/// outcomes that name a tag (Tagged, AlreadyTagged, Removed); the team fields for NotTaggedUnknownTeam.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagEntryOutcomeModel
{
	public SessionTagEntryOutcomeKind Kind { get; init; }
	public string Entry { get; init; } = string.Empty;
	public SessionTagModel? Tag { get; init; }
	public string? UnknownTeam { get; init; }
	public string? UnknownIssueKey { get; init; }
}
