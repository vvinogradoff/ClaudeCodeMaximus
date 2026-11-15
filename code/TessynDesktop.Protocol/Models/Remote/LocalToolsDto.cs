using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>The Local Tool Profile of a directory (FR.12.21) and what can be selected for it; the answer of <c>localTools.get</c> and <c>localTools.set</c>.</summary>
/// <param name="Enabled">The master switch: false = local-model turns get the full tools and MCP.</param>
/// <param name="NativeTools">The selected native tools (the default lean set when the directory has no stored profile).</param>
/// <param name="IncludeCmxMcp">The cmx orchestration MCP server is included.</param>
/// <param name="IncludedMcpServerIds">The selected external MCP server ids that still exist.</param>
/// <param name="AvailableNativeTools">The native tool catalog.</param>
/// <param name="AvailableMcpServers">The registered external MCP servers.</param>
/// <remarks>Created by Claude</remarks>
public sealed record LocalToolsDto(
	bool Enabled,
	IReadOnlyList<string> NativeTools,
	bool IncludeCmxMcp,
	IReadOnlyList<string> IncludedMcpServerIds,
	IReadOnlyList<string> AvailableNativeTools,
	IReadOnlyList<LocalToolsMcpServerDto> AvailableMcpServers);
