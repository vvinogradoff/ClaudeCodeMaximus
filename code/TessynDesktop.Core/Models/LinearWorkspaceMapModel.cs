using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>The persisted Linear team key to workspace slug map (FR.24.3), e.g. <c>HSE</c> to <c>hardstop</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LinearWorkspaceMapModel
{
	public Dictionary<string, string> Workspaces { get; set; } = new();
}
