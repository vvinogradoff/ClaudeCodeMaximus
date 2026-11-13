namespace TessynDesktop.Models.Agent;

/// <summary>Arguments of one <c>search_tags</c> call, already validated by the MCP layer (FR.24.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionTagSearchQueryModel
{
	/// <summary>FR.24.11 matching text; null or blank matches every tag.</summary>
	public string? Query { get; init; }

	/// <summary>Only <see cref="SessionListScope.Project"/> and <see cref="SessionListScope.All"/> are valid.</summary>
	public SessionListScope Scope { get; init; } = SessionListScope.Project;

	public int Limit { get; init; } = Constants.Agent.ListSessionsDefaultLimit;

	public int Offset { get; init; }
}
