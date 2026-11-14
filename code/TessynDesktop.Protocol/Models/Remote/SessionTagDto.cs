using System;

namespace TessynDesktop.Models.Remote;

/// <summary>A tag of a session (contract section 3.9).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionTagDto
{
	public SessionTagKind Kind { get; init; }
	public string Label { get; init; } = string.Empty;
	public string Url { get; init; } = string.Empty;

	/// <summary>"session" for a tag added to the session, "name" for one derived from the session name.</summary>
	public SessionTagSource Source { get; init; }

	/// <summary>When the tag was added; omitted for name-derived tags.</summary>
	public DateTimeOffset? AddedUtc { get; init; }
}
