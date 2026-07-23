using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// Intermediate organisational node that exists only in appsettings.json.
/// Has a user-defined name, no session, and no working directory of its own.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class GroupNodeModel
{
	public required string Name { get; set; }
	public bool IsExpanded { get; set; }
	/// <summary>Inherited from the ancestor DirectoryNode. Used when creating child sessions.</summary>
	public string WorkingDirectory { get; set; } = string.Empty;
	public List<GroupNodeModel> Groups { get; init; } = [];
	public List<SessionNodeModel> Sessions { get; init; } = [];
}
