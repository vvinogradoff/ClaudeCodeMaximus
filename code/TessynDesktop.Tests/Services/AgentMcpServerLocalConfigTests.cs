using System.Collections.Generic;
using System.Text.Json;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.LC.01 — Local Tool Profile (FR.12.21): asserts the filtered <c>--mcp-config</c> content built by
/// <see cref="AgentMcpServer.BuildMcpConfigJson"/>, the pure merge/serialize step extracted from
/// <c>BuildMcpConfigJsonAsync</c> so it's testable without constructing a full <see cref="AgentMcpServer"/>
/// (HTTP listener, session registry, scheduler, etc.).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerLocalConfigTests
{
	private static Dictionary<string, object> SampleRegistryServers() => new()
	{
		["server-a"] = new { type = "http", url = "https://a.example.com/" },
		["server-b"] = new { type = "stdio", command = "roslyn-mcp" },
		["server-c"] = new { type = "http", url = "https://c.example.com/" },
	};

	private static JsonElement McpServers(string json) =>
		JsonDocument.Parse(json).RootElement.Clone().GetProperty("mcpServers");

	[Fact]
	public void BuildMcpConfigJson_IncludeCmxFalse_SubsetOfExternalIds_ContainsOnlySelectedServers()
	{
		// TC.LC.01.2 — IncludedExternalMcpServerIds naming a subset of registered external servers.
		var json = AgentMcpServer.BuildMcpConfigJson(
			includeCmx: false, port: 54321, agentToken: "tok",
			registryServers: SampleRegistryServers(),
			externalServerIds: ["server-a", "server-c"]);

		var servers = McpServers(json);

		Assert.True(servers.TryGetProperty("server-a", out _));
		Assert.True(servers.TryGetProperty("server-c", out _));
		Assert.False(servers.TryGetProperty("server-b", out _));
		Assert.False(servers.TryGetProperty(Constants.Agent.McpServerName, out _));
	}

	[Fact]
	public void BuildMcpConfigJson_IncludeCmxTrue_CmxServerPresentWithTokenHeader()
	{
		var json = AgentMcpServer.BuildMcpConfigJson(
			includeCmx: true, port: 54321, agentToken: "secret-token",
			registryServers: new Dictionary<string, object>(),
			externalServerIds: []);

		var servers = McpServers(json);

		Assert.True(servers.TryGetProperty(Constants.Agent.McpServerName, out var cmx));
		Assert.Equal("http", cmx.GetProperty("type").GetString());
		Assert.Contains("54321", cmx.GetProperty("url").GetString());
		Assert.Equal("secret-token", cmx.GetProperty("headers").GetProperty(Constants.Agent.TokenHeader).GetString());
	}

	[Fact]
	public void BuildMcpConfigJson_NoExternalIdsSelected_OnlyCmxPresent()
	{
		var json = AgentMcpServer.BuildMcpConfigJson(
			includeCmx: true, port: 1, agentToken: "tok",
			registryServers: SampleRegistryServers(),
			externalServerIds: []);

		var servers = McpServers(json);

		Assert.True(servers.TryGetProperty(Constants.Agent.McpServerName, out _));
		Assert.False(servers.TryGetProperty("server-a", out _));
		Assert.False(servers.TryGetProperty("server-b", out _));
		Assert.False(servers.TryGetProperty("server-c", out _));
	}

	[Fact]
	public void BuildMcpConfigJson_NullExternalIds_MeansEveryRegistryServerIncluded()
	{
		// null = "every enabled entry" (the full-config path used for Anthropic sessions).
		var json = AgentMcpServer.BuildMcpConfigJson(
			includeCmx: true, port: 1, agentToken: "tok",
			registryServers: SampleRegistryServers(),
			externalServerIds: null);

		var servers = McpServers(json);

		Assert.True(servers.TryGetProperty("server-a", out _));
		Assert.True(servers.TryGetProperty("server-b", out _));
		Assert.True(servers.TryGetProperty("server-c", out _));
		Assert.True(servers.TryGetProperty(Constants.Agent.McpServerName, out _));
	}

	[Fact]
	public void BuildMcpConfigJson_IncludeCmxFalseAndNoExternalIds_EmptyServersMap()
	{
		var json = AgentMcpServer.BuildMcpConfigJson(
			includeCmx: false, port: 1, agentToken: "tok",
			registryServers: SampleRegistryServers(),
			externalServerIds: []);

		var servers = McpServers(json);

		Assert.Equal(0, CountProperties(servers));
	}

	private static int CountProperties(JsonElement element)
	{
		var count = 0;
		foreach (var _ in element.EnumerateObject())
			count++;
		return count;
	}
}
