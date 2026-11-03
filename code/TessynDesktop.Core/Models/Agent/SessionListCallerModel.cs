namespace TessynDesktop.Models.Agent;

/// <summary>The calling session, for the <c>You:</c> line of a listing (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionListCallerModel
{
	public required string ShortId { get; init; }

	public required string Name { get; init; }

	public required string DirectoryLabel { get; init; }

	public int OrchestrationDepth { get; init; }

	/// <summary>Null when the caller is top-level or its supervisor is no longer in the tree.</summary>
	public string? SupervisorShortId { get; init; }

	/// <summary>Null when the caller is top-level or its supervisor is no longer in the tree.</summary>
	public string? SupervisorName { get; init; }
}
