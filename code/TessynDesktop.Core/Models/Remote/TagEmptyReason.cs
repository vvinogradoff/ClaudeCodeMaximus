namespace TessynDesktop.Models.Remote;

/// <summary>Why <c>tags.search</c> returned no chips; serialized camelCase.</summary>
/// <remarks>Created by Claude</remarks>
public enum TagEmptyReason
{
	/// <summary>The scope has no tags at all.</summary>
	NoTagsScoped,

	/// <summary>No session has any tag.</summary>
	NoTagsUnscoped,

	/// <summary>Tags exist but none matches the text.</summary>
	NoMatchingTags,
}
