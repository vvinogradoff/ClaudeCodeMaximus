namespace TessynDesktop.Models.Remote;

/// <summary>Why <c>tags.search</c> returned no session groups; serialized camelCase.</summary>
/// <remarks>Created by Claude</remarks>
public enum TagSessionsEmptyReason
{
	/// <summary>No tag is selected.</summary>
	SelectTags,

	/// <summary>The selected tags carry no session in scope.</summary>
	NoSessions,
}
