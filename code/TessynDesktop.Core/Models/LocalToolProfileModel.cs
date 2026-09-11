using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// Curated tool/MCP profile applied to a directory's sessions when the effective model is a local
/// (Ollama) model that supports tool calling (FR.12.21). Anthropic sessions ignore this entirely —
/// they always get the unrestricted full tools + full MCP behavior. Null on
/// <see cref="DirectoryNodeModel.LocalToolProfile"/> means "use the default lean profile" — see
/// <see cref="TessynDesktop.Services.LocalToolProfileResolver"/> and
/// <see cref="TessynDesktop.Constants.LocalTools"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LocalToolProfileModel
{
	/// <summary>Native tool names exposed via --tools (e.g. "Read", "Bash"). Empty = no native tools.</summary>
	public List<string> NativeTools { get; set; } = [];

	/// <summary>Whether the always-present cmx MCP server (scheduling/orchestration) is included for local turns.</summary>
	public bool IncludeCmxMcp { get; set; }

	/// <summary>External MCP Server Registry entry IDs (FR.20.2) to include for local turns.</summary>
	public List<string> IncludedExternalMcpServerIds { get; set; } = [];
}
