using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeAppSettingsServiceForOAuth : IAppSettingsService
{
	public AppSettingsModel Settings { get; } = new();
	public int SaveCallCount { get; private set; }

	public void Load() { }
	public void Save() => SaveCallCount++;
}

/// <summary>Returns a canned response (or throws) for every outgoing request, recording the last request made.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
	public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
	public HttpRequestMessage? LastRequest { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		LastRequest = request;
		return Task.FromResult(Responder(request));
	}
}

/// <remarks>Created by Claude</remarks>
public sealed class McpOAuthServiceTests : IDisposable
{
	private readonly string _tempDir =
		Path.Combine(Path.GetTempPath(), "TessynMcpOAuthServiceTests_" + Guid.NewGuid().ToString("N"));
	private readonly McpTokenStore _tokenStore;
	private readonly FakeAppSettingsServiceForOAuth _appSettings = new();
	private readonly FakeHttpMessageHandler _handler = new();

	public McpOAuthServiceTests()
	{
		_tokenStore = new McpTokenStore(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
	}

	private McpOAuthService MakeService() => new(_appSettings, _tokenStore, new HttpClient(_handler));

	private static ExternalMcpServerModel MakeOAuthServer(string id = "srv") => new()
	{
		Id       = id,
		Enabled  = true,
		Transport = McpServerTransport.Http,
		Url      = "https://mcp.example.com",
		AuthType = McpServerAuthType.OAuth2,
		OAuth = new McpOAuthConfig
		{
			AuthorizationEndpoint = "https://mcp.example.com/authorize",
			TokenEndpoint         = "https://mcp.example.com/token",
			ClientId              = "client-abc",
		},
	};

	[Fact]
	public async Task GetValidAccessTokenAsync_NotOAuthEntry_ReturnsNull()
	{
		var server = MakeOAuthServer();
		server.AuthType = McpServerAuthType.StaticHeader;
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Null(token);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_NeverAuthorized_ReturnsNull()
	{
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(MakeOAuthServer());

		Assert.Null(token);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_ValidNonExpiringToken_ReturnsWithoutHttpCall()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord { AccessToken = "at_valid", ExpiresAtUtc = null });
		_handler.Responder = _ => throw new InvalidOperationException("Should not make an HTTP call for a non-expiring token.");
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Equal("at_valid", token);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_FarFromExpiry_ReturnsWithoutRefresh()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_fresh",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
		});
		_handler.Responder = _ => throw new InvalidOperationException("Should not refresh a far-from-expiry token.");
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Equal("at_fresh", token);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_NearExpiry_TriggersRefresh()
	{
		// Constants.ExternalMcp.TokenExpiryMarginSeconds = 60 — 30s out counts as near-expiry.
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_stale",
			RefreshToken = "rt_1",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30),
		});
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""{"access_token":"at_refreshed","expires_in":3600}"""),
		};
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Equal("at_refreshed", token);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_ExpiredWithRefreshToken_SuccessfulRefresh_ReturnsNewAccessTokenAndPersistsIt()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_expired",
			RefreshToken = "rt_1",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
		});
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""{"access_token":"at_new","refresh_token":"rt_2","expires_in":3600}"""),
		};
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Equal("at_new", token);
		var persisted = _tokenStore.Load(server.Id);
		Assert.Equal("at_new", persisted!.AccessToken);
		Assert.Equal("rt_2", persisted.RefreshToken);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_RefreshResponseOmitsRefreshToken_KeepsOldRefreshToken()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_expired",
			RefreshToken = "rt_original",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
		});
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""{"access_token":"at_new","expires_in":3600}"""),
		};
		var service = MakeService();

		await service.GetValidAccessTokenAsync(server);

		Assert.Equal("rt_original", _tokenStore.Load(server.Id)!.RefreshToken);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_ExpiredNoRefreshToken_ReturnsNullAndMarksNeedsReauthorization()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_expired",
			RefreshToken = null,
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
		});
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Null(token);
		Assert.True(server.OAuth!.NeedsReauthorization);
		Assert.Equal(1, _appSettings.SaveCallCount);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_RefreshEndpointReturnsError_ReturnsNullAndMarksNeedsReauthorization()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_expired",
			RefreshToken = "rt_revoked",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
		});
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Null(token);
		Assert.True(server.OAuth!.NeedsReauthorization);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_RefreshEndpointThrows_ReturnsNullAndMarksNeedsReauthorization()
	{
		var server = MakeOAuthServer();
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_expired",
			RefreshToken = "rt_1",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
		});
		_handler.Responder = _ => throw new HttpRequestException("network unreachable");
		var service = MakeService();

		var token = await service.GetValidAccessTokenAsync(server);

		Assert.Null(token);
		Assert.True(server.OAuth!.NeedsReauthorization);
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_SuccessfulRefreshAfterPriorReauthFlag_ClearsNeedsReauthorization()
	{
		var server = MakeOAuthServer();
		server.OAuth!.NeedsReauthorization = true;
		_tokenStore.Save(server.Id, new McpTokenRecord
		{
			AccessToken  = "at_expired",
			RefreshToken = "rt_1",
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
		});
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""{"access_token":"at_new","expires_in":3600}"""),
		};
		var service = MakeService();

		await service.GetValidAccessTokenAsync(server);

		Assert.False(server.OAuth.NeedsReauthorization);
	}
}
