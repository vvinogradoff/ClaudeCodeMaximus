using System.Collections.Generic;

namespace TessynDesktop.Models.Agent;

/// <summary>One tag of a <c>search_tags</c> page with the top-level sessions carrying it (FR.24.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionTagSearchRowModel
{
	public required string Label { get; init; }

	public required string Url { get; init; }

	/// <summary>Sessions carrying the tag in scope (top-level sessions, Worker Sessions rolled up).</summary>
	public required int SessionCount { get; init; }

	/// <summary>Short Node Ids of the newest carriers, newest Last Activity first, at most <see cref="Constants.SessionTags.SearchMaxCarriersPerRow"/>.</summary>
	public required IReadOnlyList<string> CarrierShortIds { get; init; }
}
