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

	/// <summary>The engine the session runs on (FR.33.1); null for Claude.</summary>
	public string? EngineId { get; init; }

	/// <summary>The Codex thread of a Codex session (FR.33.7).</summary>
	public string? CodexThreadId { get; init; }

	public bool IsFavorite { get; init; }

	public int OrchestrationDepth { get; init; }

	public string? SupervisorNodeId { get; init; }

	public bool IsRunning { get; init; }

	/// <summary>The profile the session runs on (ISSUE.013 R5): its pin's display name, <c>default</c> for the Default profile.</summary>
	public string ProfileLabel { get; init; } = Constants.ProfileSwitch.DefaultProfileName;

	/// <summary>The profile a queued switch will move the session to when its running turn ends; null when none is queued.</summary>
	public string? SwitchingToLabel { get; init; }

	public required string DirectoryPath { get; init; }

	public required string DirectoryLabel { get; init; }

	/// <summary>Group names from the Directory Node down, outermost first; empty if directly under the directory.</summary>
	public required IReadOnlyList<string> GroupPath { get; init; }
}
