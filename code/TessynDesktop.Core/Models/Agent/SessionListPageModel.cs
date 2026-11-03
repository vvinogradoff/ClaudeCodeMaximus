using System.Collections.Generic;

namespace TessynDesktop.Models.Agent;

/// <summary>One bounded page of a <c>list_sessions</c> result (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionListPageModel
{
	public required SessionListScope Scope { get; init; }

	public SessionListCallerModel? Caller { get; init; }

	/// <summary>Sections referenced by <see cref="Rows"/>, display order. On an empty page the scope's own section if any, else empty.</summary>
	public required IReadOnlyList<SessionListSectionModel> Sections { get; init; }

	/// <summary>The page, display order.</summary>
	public required IReadOnlyList<SessionListRowModel> Rows { get; init; }

	/// <summary>Row count before paging.</summary>
	public required int TotalRows { get; init; }

	public required int Offset { get; init; }

	/// <summary>Effective limit, after clamping.</summary>
	public required int Limit { get; init; }

	/// <summary>Short id of the first listed row whose workers are not all listed.</summary>
	public string? WorkersHintShortId { get; init; }
}
