using System.Collections.Generic;

namespace TessynDesktop.Models.Agent;

/// <summary>Arguments of one <c>list_sessions</c> call, already validated by the MCP layer (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionListQueryModel
{
	public SessionListScope Scope { get; init; } = SessionListScope.Project;

	/// <summary>Full NodeId of the root for children/subtree, already resolved; null = the caller.</summary>
	public string? RootNodeId { get; init; }

	public string? Query { get; init; }

	public bool? Running { get; init; }

	public int? ActiveWithinHours { get; init; }

	public bool IncludeWorkers { get; init; }

	/// <summary>
	/// NodeIds of the top-level sessions that carry any of the requested tags (FR.24.15); only candidates whose
	/// top-level session is in the set are rows (the page builder adds the workers of the carriers). Null = no tag filter; an empty set matches nothing.
	/// </summary>
	public IReadOnlySet<string>? TagCarrierNodeIds { get; init; }

	public int Limit { get; init; } = Constants.Agent.ListSessionsDefaultLimit;

	public int Offset { get; init; }
}
