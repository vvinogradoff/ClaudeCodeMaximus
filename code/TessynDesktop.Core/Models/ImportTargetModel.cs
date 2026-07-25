namespace TessynDesktop.Models;

/// <summary>
/// Represents a possible import target in the session tree (directory or group).
/// Used in the import picker's "Import into" selector.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportTargetModel
{
	public required string DisplayName { get; init; }

	/// <summary>Working directory path (for discovery). All targets under a directory share the same path.</summary>
	public required string WorkingDirectory { get; init; }

	/// <summary>Indentation level for visual hierarchy in the combo box (0 = directory, 1+ = nested groups).</summary>
	public int Depth { get; init; }

	/// <summary>True if this target is a directory node, false if it's a group node.</summary>
	public bool IsDirectory { get; init; }

	/// <summary>True for the special "New directory..." action item.</summary>
	public bool IsNewDirectoryAction { get; init; }

	/// <summary>Unique key for matching: directory path for directories, or "path|groupName" for groups.</summary>
	public required string Key { get; init; }

	public override string ToString()
	{
		var indent = new string(' ', Depth * 3);
		return $"{indent}{DisplayName}";
	}
}
