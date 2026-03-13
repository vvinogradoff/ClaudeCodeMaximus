using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// Top-level tree node backed by a real filesystem directory.
/// Display name is always derived from the path — never user-assigned.
/// Defines the working directory for all sessions nested beneath it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DirectoryNodeModel
{
	public required string Path { get; init; }
	public bool IsExpanded { get; set; }
	public List<GroupNodeModel> Groups { get; init; } = [];
	public List<SessionNodeModel> Sessions { get; init; } = [];
}
