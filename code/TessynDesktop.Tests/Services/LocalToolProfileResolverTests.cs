using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.LC.01 — Local Tool Profile (FR.12.21): unit tests for <see cref="LocalToolProfileResolver.Resolve"/>,
/// the single source of truth shared by the headless (SessionTurnService) and interactive
/// (TurnController) turn paths.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LocalToolProfileResolverTests
{
	private static ClaudeModelInfo AnthropicModel() =>
		new("claude-opus-4-8", "opus", "Opus 4.8", ModelProvider.Anthropic, SupportsTools: true);

	private static ClaudeModelInfo OllamaModel(bool supportsTools = true) =>
		new("qwen2.5-coder:32b", "", "qwen2.5-coder:32b", ModelProvider.Ollama, SupportsTools: supportsTools);

	private static DirectoryNodeModel Directory(LocalToolProfileModel? profile = null) =>
		new() { Path = @"C:\proj", LocalToolProfile = profile };

	[Fact]
	public void Resolve_AnthropicModel_ReturnsNull()
	{
		var result = LocalToolProfileResolver.Resolve(AnthropicModel(), Directory());

		Assert.Null(result);
	}

	[Fact]
	public void Resolve_OllamaModel_SupportsToolsFalse_ReturnsNull()
	{
		// TC.LC.01.4 — tool-incapable local models never consult the Local Tool Profile at all.
		var profile = new LocalToolProfileModel
		{
			NativeTools = ["Read", "Bash"],
			IncludeCmxMcp = true,
		};

		var result = LocalToolProfileResolver.Resolve(OllamaModel(supportsTools: false), Directory(profile));

		Assert.Null(result);
	}

	[Fact]
	public void Resolve_NullModelInfo_ReturnsNull()
	{
		var result = LocalToolProfileResolver.Resolve(null, Directory());

		Assert.Null(result);
	}

	[Fact]
	public void Resolve_ToolCapableOllamaModel_NoSavedProfile_UsesDefaultLeanProfile()
	{
		// TC.LC.01.1 — LocalToolProfile is null on the directory.
		var result = LocalToolProfileResolver.Resolve(OllamaModel(), Directory());

		Assert.NotNull(result);
		Assert.Equal(Constants.LocalTools.DefaultNativeTools, result!.ToolSpec.NativeToolNames);
		Assert.False(result.IncludeCmxMcp);
		Assert.Empty(result.IncludedExternalMcpServerIds);
	}

	[Fact]
	public void Resolve_ToolCapableOllamaModel_NullDirectory_UsesDefaultLeanProfile()
	{
		var result = LocalToolProfileResolver.Resolve(OllamaModel(), null);

		Assert.NotNull(result);
		Assert.Equal(Constants.LocalTools.DefaultNativeTools, result!.ToolSpec.NativeToolNames);
		Assert.False(result.IncludeCmxMcp);
		Assert.Empty(result.IncludedExternalMcpServerIds);
	}

	[Fact]
	public void Resolve_ToolCapableOllamaModel_CustomProfile_EchoesExactValues()
	{
		// TC.LC.01.2 — a saved profile with a non-default native tool set plus MCP selections.
		var profile = new LocalToolProfileModel
		{
			NativeTools = ["Read", "Bash", "WebFetch", "TodoWrite"],
			IncludeCmxMcp = true,
			IncludedExternalMcpServerIds = ["server-a", "server-b"],
		};

		var result = LocalToolProfileResolver.Resolve(OllamaModel(), Directory(profile));

		Assert.NotNull(result);
		Assert.Equal(profile.NativeTools, result!.ToolSpec.NativeToolNames);
		Assert.True(result.IncludeCmxMcp);
		Assert.Equal(profile.IncludedExternalMcpServerIds, result.IncludedExternalMcpServerIds);
	}

	[Fact]
	public void Resolve_ToolCapableOllamaModel_EmptyNativeToolsSelection_ResolvesToEmptyList()
	{
		// TC.LC.01.2 — unchecking every native tool is valid (⇒ --tools "").
		var profile = new LocalToolProfileModel { NativeTools = [] };

		var result = LocalToolProfileResolver.Resolve(OllamaModel(), Directory(profile));

		Assert.NotNull(result);
		Assert.Empty(result!.ToolSpec.NativeToolNames);
	}

	[Fact]
	public void Resolve_ToolCapableOllamaModel_ProfileDisabled_ReturnsNull_EvenWithCustomSelection()
	{
		// Master on/off switch: a saved, non-default selection must NOT be applied while
		// Enabled=false — the bypass wins over any saved customization, not just the default-lean case.
		var profile = new LocalToolProfileModel
		{
			Enabled = false,
			NativeTools = ["Read", "Bash", "WebFetch", "TodoWrite"],
			IncludeCmxMcp = true,
			IncludedExternalMcpServerIds = ["server-a"],
		};

		var result = LocalToolProfileResolver.Resolve(OllamaModel(), Directory(profile));

		Assert.Null(result);
	}
}
