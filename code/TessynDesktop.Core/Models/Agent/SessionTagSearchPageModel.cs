using System.Collections.Generic;

namespace TessynDesktop.Models.Agent;

/// <summary>One bounded page of a <c>search_tags</c> result (FR.24.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionTagSearchPageModel
{
	public required SessionListScope Scope { get; init; }

	/// <summary>The calling session, for the <c>You:</c> line; null when it is not in the tree.</summary>
	public SessionListCallerModel? Caller { get; init; }

	/// <summary>Label and path of the caller's Directory node (<see cref="SessionListScope.Project"/> scope only).</summary>
	public string? DirectoryLabel { get; init; }

	public string? DirectoryPath { get; init; }

	/// <summary>The trimmed query text; null when blank.</summary>
	public string? Query { get; init; }

	public required IReadOnlyList<SessionTagSearchRowModel> Rows { get; init; }

	/// <summary>Tags matching the query in scope, before paging.</summary>
	public required int TotalRows { get; init; }

	/// <summary>Distinct tags in scope whatever the query — tells "no tags at all" from "no matching tags".</summary>
	public required int TagsInScope { get; init; }

	public required int Offset { get; init; }

	/// <summary>Effective limit, after clamping.</summary>
	public required int Limit { get; init; }
}
