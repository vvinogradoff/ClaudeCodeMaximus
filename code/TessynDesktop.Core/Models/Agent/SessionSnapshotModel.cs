using System.Collections.Generic;

namespace TessynDesktop.Models.Agent;

/// <summary>Lock-free copy of the session fields a listing needs, taken under the settings lock (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSnapshotModel
{
	public required string NodeId { get; init; }

	public required string Name { get; init; }

	public required string FileName { get; init; }

	public required string WorkingDirectory { get; init; }

	public string? ClaudeSessionId { get; init; }

	public bool IsFavorite { get; init; }

	public int OrchestrationDepth { get; init; }

	public string? SupervisorNodeId { get; init; }

	public bool IsRunning { get; init; }

	public required string DirectoryPath { get; init; }

	public required string DirectoryLabel { get; init; }

	/// <summary>Group names from the Directory Node down, outermost first; empty if directly under the directory.</summary>
	public required IReadOnlyList<string> GroupPath { get; init; }
}
