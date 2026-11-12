using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// Top-level tree node backed by a real filesystem directory.
/// Display name is always derived from the path — never user-assigned.
/// Defines the working directory for all sessions nested beneath it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DirectoryNodeModel
{
	/// <summary>Stable id ("N"-format GUID) used by the remote API; assigned on creation or, for older trees, on load.</summary>
	public string NodeId { get; set; } = string.Empty;

	public required string Path { get; init; }
	public bool IsExpanded { get; set; }
	public List<GroupNodeModel> Groups { get; init; } = [];
	public List<SessionNodeModel> Sessions { get; init; } = [];

	/// <summary>Whether the command bar is visible for sessions under this directory (FR.12.11).</summary>
	public bool IsCommandBarVisible { get; set; }

	/// <summary>Selected model ID for sessions under this directory (FR.12.4). Empty = Default.</summary>
	public string SelectedModelId { get; set; } = string.Empty;

	/// <summary>Selected profile index for sessions under this directory (FR.12.8). 0=Default.</summary>
	public int SelectedProfileIndex { get; set; }

	/// <summary>Selected effort level index for sessions under this directory. 0=Default.</summary>
	public int SelectedEffortIndex { get; set; }

	/// <summary>
	/// Curated tool/MCP profile applied to sessions under this directory when the effective model is
	/// a local (Ollama) model that supports tool calling (FR.12.21). Null = default lean profile
	/// (see <see cref="TessynDesktop.Services.LocalToolProfileResolver"/>). Ignored for Anthropic models.
	/// </summary>
	public LocalToolProfileModel? LocalToolProfile { get; set; }
}
