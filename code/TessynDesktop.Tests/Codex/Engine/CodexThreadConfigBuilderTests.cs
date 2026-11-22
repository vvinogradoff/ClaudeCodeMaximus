using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>The per-thread config of FR.33.9 and FR.33.16: cmx, the registry entries in Codex form, the project-doc fallback and the apps switch.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadConfigBuilderTests : IAsyncLifetime
{
	private readonly CodexEngineFixture _f = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _f.DisposeAsync();

	private void AddRegistry(params ExternalMcpServerModel[] entries) => _f.Settings.Settings.ExternalMcpServers.AddRange(entries);

	[Fact]
	public async Task Registry_entries_are_passed_in_Codex_form_with_their_headers_and_the_approve_mode()
	{
		AddRegistry(
			new ExternalMcpServerModel { Id = "linear", Url = "https://mcp.linear.app/mcp", AuthType = McpServerAuthType.StaticHeader, StaticHeaderName = "X-Api-Key", StaticHeaderValue = "STATIC-1" },
			new ExternalMcpServerModel { Id = "github", Url = "https://api.githubcopilot.com/mcp", AuthType = McpServerAuthType.OAuth2, OAuth = new McpOAuthConfig() },
			new ExternalMcpServerModel { Id = "files", Transport = McpServerTransport.Stdio, Command = "npx", Arguments = "-y @scope/files --root C:\\x" },
			new ExternalMcpServerModel { Id = "off", Url = "https://off.example/mcp", Enabled = false });

		var config  = await _f.Config.BuildAsync(_f.Node());
		var servers = config["mcp_servers"]!.AsObject();

		Assert.Equal(["cmx", "linear", "github", "files"], servers.Select(s => s.Key));
		Assert.Equal("https://mcp.linear.app/mcp", servers["linear"]!["url"]!.GetValue<string>());
		Assert.Equal("STATIC-1", servers["linear"]!["http_headers"]!["X-Api-Key"]!.GetValue<string>());
		Assert.Equal("Bearer OAUTH-SECRET-1", servers["github"]!["http_headers"]!["Authorization"]!.GetValue<string>());
		Assert.Equal("npx", servers["files"]!["command"]!.GetValue<string>());
		Assert.Equal(["-y", "@scope/files", "--root", "C:\\x"], servers["files"]!["args"]!.AsArray().Select(a => a!.GetValue<string>()));
		Assert.All(servers, s => Assert.Equal("approve", s.Value!["default_tools_approval_mode"]!.GetValue<string>()));
		Assert.All(servers, s => Assert.Null(s.Value!["required"]));
		Assert.Null(servers["files"]!["url"]);
		Assert.Null(servers["linear"]!["type"]);
	}

	[Fact]
	public async Task An_OAuth_entry_without_a_valid_token_is_left_out()
	{
		_f.OAuth.Token = null;
		AddRegistry(new ExternalMcpServerModel { Id = "github", Url = "https://api.githubcopilot.com/mcp", AuthType = McpServerAuthType.OAuth2, OAuth = new McpOAuthConfig() });

		var config = await _f.Config.BuildAsync(_f.Node());

		Assert.Equal(["cmx"], config["mcp_servers"]!.AsObject().Select(s => s.Key));
	}

	[Fact]
	public async Task Without_agent_tools_or_a_listener_there_is_no_cmx_server_but_the_rest_stays()
	{
		_f.Settings.Settings.AgentToolsEnabled = false;
		var withoutTools = await _f.Config.BuildAsync(_f.Node());
		_f.Settings.Settings.AgentToolsEnabled = true;
		_f.Mcp.Port = 0;
		var withoutListener = await _f.Config.BuildAsync(_f.Node());

		foreach (var config in new[] { withoutTools, withoutListener })
		{
			Assert.Null(config["mcp_servers"]);
			Assert.Equal(["CLAUDE.md"], config["project_doc_fallback_filenames"]!.AsArray().Select(a => a!.GetValue<string>()));
			Assert.False(config["features.apps"]!.GetValue<bool>());
		}
	}

	[Fact]
	public async Task A_registry_entry_named_cmx_never_replaces_Tessyn_s_own_server()
	{
		AddRegistry(new ExternalMcpServerModel { Id = "cmx", Url = "https://evil.example/mcp" });
		var node = _f.Node();

		var config = await _f.Config.BuildAsync(node);

		var cmx = config["mcp_servers"]!["cmx"]!;
		Assert.Equal($"http://127.0.0.1:{_f.Mcp.Port}/mcp", cmx["url"]!.GetValue<string>());
		Assert.Equal(node.AgentToken, cmx["http_headers"]!["X-CMX-Token"]!.GetValue<string>());
	}

	[Fact]
	public async Task Every_call_builds_a_new_object()
	{
		var node = _f.Node();

		var first  = await _f.Config.BuildAsync(node);
		var second = await _f.Config.BuildAsync(node);

		Assert.NotSame(first, second);
		Assert.True(JsonNode.DeepEquals(first, second));
	}
}
