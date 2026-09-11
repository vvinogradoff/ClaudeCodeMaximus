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
	/// <summary>
	/// Master on/off switch for applying this curated profile at all. <c>false</c> ⇒ the directory's
	/// local-model turns get the full unrestricted native-tool + MCP behavior (same as an Anthropic
	/// session), regardless of the saved <see cref="NativeTools"/>/<see cref="IncludeCmxMcp"/>/
	/// <see cref="IncludedExternalMcpServerIds"/> — those stay saved and reapply automatically when
	/// flipped back to <c>true</c>. Defaults to <c>true</c> so profiles persisted before this field
	/// existed simply deserialize with <c>Enabled = true</c> (missing JSON property ⇒ C# default).
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Native tool names exposed via --tools (e.g. "Read", "Bash"). Empty = no native tools.</summary>
	public List<string> NativeTools { get; set; } = [];

	/// <summary>Whether the always-present cmx MCP server (scheduling/orchestration) is included for local turns.</summary>
	public bool IncludeCmxMcp { get; set; }

	/// <summary>External MCP Server Registry entry IDs (FR.20.2) to include for local turns.</summary>
	public List<string> IncludedExternalMcpServerIds { get; set; } = [];
}
