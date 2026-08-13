using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeMcpOAuthServiceForMerge : IMcpOAuthService
{
	public Dictionary<string, string?> TokensByServerId { get; } = new();

	public Task<string?> GetValidAccessTokenAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default)
		=> Task.FromResult(TokensByServerId.TryGetValue(server.Id, out var token) ? token : null);

	public Task<McpAuthorizeResult> AuthorizeInteractiveAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default)
		=> Task.FromResult(new McpAuthorizeResult(true, "n/a"));
}

/// <remarks>Created by Claude</remarks>
public sealed class McpConfigMergerTests
{
	private static ExternalMcpServerModel MakeStdio(string id, bool enabled = true) => new()
	{
		Id          = id,
		DisplayName = id,
		Enabled     = enabled,
		Transport   = McpServerTransport.Stdio,
		Command     = "roslyn-mcp",
		Arguments   = "--stdio --verbose",
	};

	private static ExternalMcpServerModel MakeHttp(
		string id, McpServerAuthType authType, bool enabled = true) => new()
	{
		Id          = id,
		DisplayName = id,
		Enabled     = enabled,
		Transport   = McpServerTransport.Http,
		Url         = "https://mcp.example.com/",
		AuthType    = authType,
	};

	private static JsonElement ToElement(object value) =>
		JsonSerializer.SerializeToElement(value);

	[Fact]
	public async Task BuildRegistryServersAsync_DisabledEntry_Omitted()
	{
		var merger = new McpConfigMerger(new FakeMcpOAuthServiceForMerge());
		var entries = new[] { MakeStdio("s1", enabled: false) };

		var result = await merger.BuildRegistryServersAsync(entries);

		Assert.Empty(result);
	}

	[Fact]
	public async Task BuildRegistryServersAsync_StdioEntry_EmitsCommandAndSplitArgs()
	{
		var merger = new McpConfigMerger(new FakeMcpOAuthServiceForMerge());
		var entries = new[] { MakeStdio("roslyn") };

		var result = await merger.BuildRegistryServersAsync(entries);

		Assert.True(result.ContainsKey("roslyn"));
		var element = ToElement(result["roslyn"]);
		Assert.Equal("stdio", element.GetProperty("type").GetString());
		Assert.Equal("roslyn-mcp", element.GetProperty("command").GetString());
		var args = element.GetProperty("args").EnumerateArray();
		Assert.Equal(new[] { "--stdio", "--verbose" }, args.Select(a => a.GetString()));
	}

	[Fact]
	public async Task BuildRegistryServersAsync_HttpNone_EmitsUrlWithEmptyHeaders()
	{
		var merger = new McpConfigMerger(new FakeMcpOAuthServiceForMerge());
		var entries = new[] { MakeHttp("srv", McpServerAuthType.None) };

		var result = await merger.BuildRegistryServersAsync(entries);

		var element = ToElement(result["srv"]);
		Assert.Equal("http", element.GetProperty("type").GetString());
		Assert.Equal("https://mcp.example.com/", element.GetProperty("url").GetString());
		Assert.Equal(0, element.GetProperty("headers").EnumerateObject().Count());
	}

	[Fact]
	public async Task BuildRegistryServersAsync_HttpStaticHeader_EmitsConfiguredHeader()
	{
		var merger = new McpConfigMerger(new FakeMcpOAuthServiceForMerge());
		var entry = MakeHttp("srv", McpServerAuthType.StaticHeader);
		entry.StaticHeaderName  = "X-Api-Key";
		entry.StaticHeaderValue = "secret-123";

		var result = await merger.BuildRegistryServersAsync(new[] { entry });

		var headers = ToElement(result["srv"]).GetProperty("headers");
		Assert.Equal("secret-123", headers.GetProperty("X-Api-Key").GetString());
	}

	[Fact]
	public async Task BuildRegistryServersAsync_HttpStaticHeader_EmptyHeaderName_OmitsHeader()
	{
		var merger = new McpConfigMerger(new FakeMcpOAuthServiceForMerge());
		var entry = MakeHttp("srv", McpServerAuthType.StaticHeader);
		// StaticHeaderName left empty (misconfigured entry).

		var result = await merger.BuildRegistryServersAsync(new[] { entry });

		var headers = ToElement(result["srv"]).GetProperty("headers");
		Assert.Equal(0, headers.EnumerateObject().Count());
	}

	[Fact]
	public async Task BuildRegistryServersAsync_HttpOAuth2_ValidToken_EmitsBearerHeader()
	{
		var oauth = new FakeMcpOAuthServiceForMerge();
		oauth.TokensByServerId["srv"] = "at_abc123";
		var merger = new McpConfigMerger(oauth);

		var result = await merger.BuildRegistryServersAsync(new[] { MakeHttp("srv", McpServerAuthType.OAuth2) });

		var headers = ToElement(result["srv"]).GetProperty("headers");
		Assert.Equal("Bearer at_abc123", headers.GetProperty("Authorization").GetString());
	}

	[Fact]
	public async Task BuildRegistryServersAsync_HttpOAuth2_NoValidToken_EntryOmittedEntirely()
	{
		// FR.20.6: unauthorized/expired-with-failed-refresh OAuth entries are skipped, not sent
		// with a stale/invalid token.
		var merger = new McpConfigMerger(new FakeMcpOAuthServiceForMerge());

		var result = await merger.BuildRegistryServersAsync(new[] { MakeHttp("srv", McpServerAuthType.OAuth2) });

		Assert.Empty(result);
	}

	[Fact]
	public async Task BuildRegistryServersAsync_MixedEntries_OnlyEnabledAndAuthorizedIncluded()
	{
		var oauth = new FakeMcpOAuthServiceForMerge();
		oauth.TokensByServerId["authorized"] = "token";
		var merger = new McpConfigMerger(oauth);

		var entries = new[]
		{
			MakeStdio("stdio-enabled"),
			MakeStdio("stdio-disabled", enabled: false),
			MakeHttp("authorized", McpServerAuthType.OAuth2),
			MakeHttp("unauthorized", McpServerAuthType.OAuth2),
			MakeHttp("static", McpServerAuthType.None),
		};

		var result = await merger.BuildRegistryServersAsync(entries);

		Assert.Equal(new HashSet<string> { "stdio-enabled", "authorized", "static" }, result.Keys.ToHashSet());
	}
}
