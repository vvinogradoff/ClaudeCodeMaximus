using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.LC.01 — Local Tool Profile (FR.12.21): asserts the exact <c>--tools</c> / <c>--strict-mcp-config</c>
/// flags <see cref="ClaudeProcessManager.BuildArguments"/> assembles for each combination. Reflects
/// the assembled arg STRING only (mirrors the existing ClaudeProcessLauncher/BuildArguments test
/// pattern) — no real claude process is spawned. <c>BuildArguments</c> is internal; visible here via
/// <c>[assembly: InternalsVisibleTo("TessynDesktop.Tests")]</c> in TessynDesktop.Core/AssemblyInfo.cs.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProcessManagerBuildArgumentsTests
{
	[Fact]
	public void BuildArguments_LocalToolCapable_EmitsToolsFlagAndStrictMcpConfig()
	{
		// TC.LC.01.1 — default lean profile's native tool set, space-separated, exact order.
		var spec = new LocalToolSpec(Constants.LocalTools.DefaultNativeTools);

		var args = ClaudeProcessManager.BuildArguments(
			sessionId: null, localToolSpec: spec, strictMcpConfig: true);

		Assert.Contains("--tools \"Read Write Edit Bash Glob Grep LS\"", args);
		Assert.Contains("--strict-mcp-config", args);
	}

	[Fact]
	public void BuildArguments_LocalToolCapable_CustomSelection_EmitsExactSelectedNames()
	{
		// TC.LC.01.2 — a saved profile with a non-default native tool set.
		var spec = new LocalToolSpec(["Read", "Bash", "WebFetch", "TodoWrite"]);

		var args = ClaudeProcessManager.BuildArguments(
			sessionId: null, localToolSpec: spec, strictMcpConfig: true);

		Assert.Contains("--tools \"Read Bash WebFetch TodoWrite\"", args);
	}

	[Fact]
	public void BuildArguments_LocalToolCapable_EmptyNativeToolSelection_EmitsEmptyToolsFlag()
	{
		// TC.LC.01.2 — unchecking every native tool is valid (⇒ --tools "").
		var spec = new LocalToolSpec(new List<string>());

		var args = ClaudeProcessManager.BuildArguments(
			sessionId: null, localToolSpec: spec, strictMcpConfig: true);

		Assert.Contains("--tools \"\"", args);
		Assert.Contains("--strict-mcp-config", args);
	}

	[Fact]
	public void BuildArguments_AnthropicModel_NoLocalToolSpec_OmitsToolsAndStrictMcpConfig()
	{
		// TC.LC.01.3 — Anthropic sessions pass neither localToolSpec nor strictMcpConfig; the CLI's
		// own default (full tools, full ambient/full merged MCP) applies unrestricted.
		var args = ClaudeProcessManager.BuildArguments(
			sessionId: null, mcpConfigPath: @"C:\mcp\node.json");

		Assert.DoesNotContain("--tools", args);
		Assert.DoesNotContain("--strict-mcp-config", args);
		Assert.Contains("--mcp-config \"C:\\mcp\\node.json\"", args);
	}

	[Fact]
	public void BuildArguments_ToolIncapableLocalModel_DisableToolsWins_RegardlessOfLocalToolSpec()
	{
		// TC.LC.01.4 — SupportsTools=false: disableTools always emits --tools "" even if a
		// (nonsensical, but defensively handled) localToolSpec were also passed.
		var spec = new LocalToolSpec(["Read", "Bash"]);

		var args = ClaudeProcessManager.BuildArguments(
			sessionId: null, disableTools: true, localToolSpec: spec, strictMcpConfig: true);

		Assert.Contains("--tools \"\"", args);
		Assert.DoesNotContain("--tools \"Read Bash\"", args);
	}

	[Fact]
	public void BuildArguments_ToolIncapableLocalModel_NoMcpConfigPath_OmitsMcpConfigFlag()
	{
		// TC.LC.01.4 — the caller passes no mcpConfigPath for tool-incapable models; confirm
		// BuildArguments itself never invents one.
		var args = ClaudeProcessManager.BuildArguments(sessionId: null, disableTools: true);

		Assert.DoesNotContain("--mcp-config", args);
	}
}
