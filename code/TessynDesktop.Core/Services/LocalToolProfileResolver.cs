using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Single source of truth for translating a directory's Local Tool Profile (FR.12.21) into the
/// concrete --tools / --mcp-config selection for a claude CLI spawn. Shared by the headless
/// (<see cref="SessionTurnService"/>) and interactive (<c>TurnController</c>) turn paths so both
/// agree on the same resolved profile for a given directory + model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class LocalToolProfileResolver
{
	/// <summary>
	/// Returns null when the effective model is not a tool-capable local Ollama model — Anthropic
	/// sessions keep the unrestricted default (no --tools flag, full --mcp-config), and Ollama
	/// models with SupportsTools=false already get zero tools/MCP via the pre-existing disableTools
	/// path, so they don't need a further restriction here. Otherwise resolves
	/// <paramref name="directoryModel"/>'s <see cref="LocalToolProfileModel"/>, falling back to the
	/// curated default lean set (<see cref="Constants.LocalTools.DefaultNativeTools"/>) with no MCP
	/// servers when the directory has no explicit profile.
	/// </summary>
	public static LocalToolProfileResolution? Resolve(ClaudeModelInfo? modelInfo, DirectoryNodeModel? directoryModel)
	{
		if (modelInfo is not { Provider: ModelProvider.Ollama, SupportsTools: true })
			return null;

		var profile           = directoryModel?.LocalToolProfile;
		var nativeTools        = profile?.NativeTools ?? new List<string>(Constants.LocalTools.DefaultNativeTools);
		var includeCmx         = profile?.IncludeCmxMcp ?? false;
		var externalServerIds  = profile?.IncludedExternalMcpServerIds ?? new List<string>();

		return new LocalToolProfileResolution(new LocalToolSpec(nativeTools), includeCmx, externalServerIds);
	}
}

/// <summary>Resolved outcome of <see cref="LocalToolProfileResolver.Resolve"/> for one turn.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record LocalToolProfileResolution(
	LocalToolSpec ToolSpec,
	bool IncludeCmxMcp,
	IReadOnlyList<string> IncludedExternalMcpServerIds);
