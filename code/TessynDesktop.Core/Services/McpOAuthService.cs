using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class McpOAuthService : IMcpOAuthService
{
	private static readonly ILogger _log = Log.ForContext<McpOAuthService>();

	private readonly IAppSettingsService _appSettings;
	private readonly IMcpTokenStore      _tokenStore;
	private readonly HttpClient          _http;

	public McpOAuthService(IAppSettingsService appSettings, IMcpTokenStore tokenStore, HttpClient httpClient)
	{
		_appSettings = appSettings;
		_tokenStore  = tokenStore;
		_http        = httpClient;
	}

	// ── Token resolution + refresh (FR.20.6) ─────────────────────────────────

	public async Task<string?> GetValidAccessTokenAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default)
	{
		if (server.AuthType != McpServerAuthType.OAuth2 || server.OAuth == null)
			return null;

		var record = _tokenStore.Load(server.Id);
		if (record == null)
			return null; // never authorized

		if (!IsExpiredOrNearExpiry(record))
			return record.AccessToken;

		if (string.IsNullOrEmpty(record.RefreshToken))
		{
			MarkNeedsReauthorization(server);
			return null;
		}

		var refreshed = await TryRefreshAsync(server.OAuth, record, cancellationToken);
		if (refreshed == null)
		{
			MarkNeedsReauthorization(server);
			return null;
		}

		_tokenStore.Save(server.Id, refreshed);
		if (server.OAuth.NeedsReauthorization)
		{
			server.OAuth.NeedsReauthorization = false;
			_appSettings.Save();
		}
		return refreshed.AccessToken;
	}

	private static bool IsExpiredOrNearExpiry(McpTokenRecord record) =>
		record.ExpiresAtUtc.HasValue &&
		record.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow.AddSeconds(Constants.ExternalMcp.TokenExpiryMarginSeconds);

	private void MarkNeedsReauthorization(ExternalMcpServerModel server)
	{
		if (server.OAuth == null || server.OAuth.NeedsReauthorization)
			return;
		server.OAuth.NeedsReauthorization = true;
		_appSettings.Save();
	}

	private async Task<McpTokenRecord?> TryRefreshAsync(McpOAuthConfig oauth, McpTokenRecord record, CancellationToken ct)
	{
		try
		{
			var form = new Dictionary<string, string>
			{
				["grant_type"]    = "refresh_token",
				["refresh_token"] = record.RefreshToken!,
				["client_id"]     = oauth.ClientId,
			};
			if (!string.IsNullOrEmpty(oauth.ClientSecret))
				form["client_secret"] = oauth.ClientSecret;

			using var response = await _http.PostAsync(oauth.TokenEndpoint, new FormUrlEncodedContent(form), ct);
			if (!response.IsSuccessStatusCode)
			{
				_log.Warning("McpOAuthService: refresh failed: {Status}", response.StatusCode);
				return null;
			}

			var json = await response.Content.ReadAsStringAsync(ct);
			return ParseTokenResponse(json, record.RefreshToken);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "McpOAuthService: refresh threw");
			return null;
		}
	}

	private static McpTokenRecord? ParseTokenResponse(string json, string? fallbackRefreshToken)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
			if (string.IsNullOrEmpty(accessToken))
				return null;

			var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : fallbackRefreshToken;
			var tokenType    = root.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "Bearer" : "Bearer";
			DateTimeOffset? expiresAt = root.TryGetProperty("expires_in", out var ei)
				? DateTimeOffset.UtcNow.AddSeconds(ei.GetDouble())
				: null;

			return new McpTokenRecord
			{
				AccessToken  = accessToken,
				RefreshToken = refreshToken,
				TokenType    = tokenType,
				ExpiresAtUtc = expiresAt,
			};
		}
		catch (Exception)
		{
			return null;
		}
	}

	// ── Interactive authorization (FR.20.3, FR.20.4) ─────────────────────────

	public async Task<McpAuthorizeResult> AuthorizeInteractiveAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default)
	{
		if (server.Transport != McpServerTransport.Http || server.AuthType != McpServerAuthType.OAuth2)
			return new McpAuthorizeResult(false, "This entry is not an OAuth-based HTTP server.");

		server.OAuth ??= new McpOAuthConfig();
		var oauth = server.OAuth;

		if (!oauth.DiscoveryCompleted)
			await DiscoverAsync(server.Url, oauth, cancellationToken);

		if (string.IsNullOrEmpty(oauth.AuthorizationEndpoint) || string.IsNullOrEmpty(oauth.TokenEndpoint))
			return new McpAuthorizeResult(false,
				"No authorization/token endpoint known for this server — discovery failed and none were entered manually.");

		var port        = FindFreeLoopbackPort();
		var redirectUri = $"http://127.0.0.1:{port}{Constants.ExternalMcp.RedirectPath}";

		if (!string.IsNullOrEmpty(oauth.RegistrationEndpoint) && string.IsNullOrEmpty(oauth.ClientId))
		{
			var registered = await TryRegisterClientAsync(oauth, redirectUri, cancellationToken);
			if (!registered)
				return new McpAuthorizeResult(false, "Dynamic Client Registration failed and no client ID is configured.");
		}

		if (string.IsNullOrEmpty(oauth.ClientId))
			return new McpAuthorizeResult(false, "No OAuth client ID configured and this server has no registration endpoint.");

		var codeVerifier  = GenerateCodeVerifier();
		var codeChallenge = ComputeCodeChallenge(codeVerifier);
		var state         = Guid.NewGuid().ToString("N");
		var authUrl       = BuildAuthorizationUrl(oauth, redirectUri, codeChallenge, state);

		using var listener = new HttpListener();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		try
		{
			listener.Start();
		}
		catch (Exception ex)
		{
			return new McpAuthorizeResult(false, $"Could not start loopback listener: {ex.Message}");
		}

		try
		{
			OpenBrowser(authUrl);
		}
		catch (Exception ex)
		{
			try { listener.Stop(); } catch { /* best effort */ }
			return new McpAuthorizeResult(false, $"Could not open browser: {ex.Message}");
		}

		string? code;
		try
		{
			using var timeoutCts = new CancellationTokenSource(Constants.ExternalMcp.AuthorizeTimeoutMs);
			using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
			code = await WaitForRedirectAsync(listener, state, linkedCts.Token);
		}
		catch (OperationCanceledException)
		{
			return new McpAuthorizeResult(false, "Timed out waiting for the browser authorization redirect.");
		}
		finally
		{
			try { listener.Stop(); } catch { /* best effort */ }
		}

		if (code == null)
			return new McpAuthorizeResult(false, "Authorization was cancelled, denied, or the redirect could not be verified.");

		var record = await ExchangeCodeAsync(oauth, code, redirectUri, codeVerifier, cancellationToken);
		if (record == null)
			return new McpAuthorizeResult(false, "Token exchange failed.");

		_tokenStore.Save(server.Id, record);
		oauth.NeedsReauthorization = false;
		_appSettings.Save();

		return new McpAuthorizeResult(true, "Authorized.");
	}

	/// <summary>
	/// Probes the MCP Authorization spec's well-known authorization-server metadata document.
	/// Best-effort: on any failure, endpoints simply stay whatever was entered manually (possibly
	/// empty), and DiscoveryCompleted is still set so a manual retry doesn't loop forever.
	/// </summary>
	public async Task DiscoverAsync(string baseUrl, McpOAuthConfig oauth, CancellationToken cancellationToken = default)
	{
		using var timeoutCts = new CancellationTokenSource(Constants.ExternalMcp.DiscoveryTimeoutMs);
		using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
		var ct = linkedCts.Token;
		var trimmedBase = baseUrl.TrimEnd('/');
		try
		{
			var authServerBase = await TryResolveAuthorizationServerBaseAsync(trimmedBase, ct) ?? trimmedBase;
			var json = await _http.GetStringAsync(authServerBase + Constants.ExternalMcp.WellKnownAuthorizationServerPath, ct);
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (string.IsNullOrEmpty(oauth.AuthorizationEndpoint) &&
			    root.TryGetProperty("authorization_endpoint", out var a))
				oauth.AuthorizationEndpoint = a.GetString() ?? "";

			if (string.IsNullOrEmpty(oauth.TokenEndpoint) &&
			    root.TryGetProperty("token_endpoint", out var t))
				oauth.TokenEndpoint = t.GetString() ?? "";

			if (root.TryGetProperty("registration_endpoint", out var r))
				oauth.RegistrationEndpoint = r.GetString();
		}
		catch (Exception ex)
		{
			_log.Debug("McpOAuthService: authorization-server discovery failed for {BaseUrl}: {Error}", trimmedBase, ex.Message);
		}
		finally
		{
			oauth.DiscoveryCompleted = true;
		}
	}

	/// <summary>
	/// Probes the protected-resource metadata document (RFC 9728) for an <c>authorization_servers</c>
	/// list. Some MCP servers separate the resource from its authorization server, in which case the
	/// authorization-server metadata document lives at the first listed entry rather than at the
	/// resource's own base URL. Best-effort: any failure here just falls back to probing the
	/// resource's own base URL directly.
	/// </summary>
	private async Task<string?> TryResolveAuthorizationServerBaseAsync(string trimmedBase, CancellationToken ct)
	{
		try
		{
			var json = await _http.GetStringAsync(trimmedBase + Constants.ExternalMcp.WellKnownProtectedResourcePath, ct);
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("authorization_servers", out var servers) &&
			    servers.ValueKind == JsonValueKind.Array &&
			    servers.GetArrayLength() > 0)
			{
				var first = servers[0].GetString();
				if (!string.IsNullOrEmpty(first))
					return first.TrimEnd('/');
			}
		}
		catch (Exception ex)
		{
			_log.Debug("McpOAuthService: protected-resource discovery failed for {BaseUrl}: {Error}", trimmedBase, ex.Message);
		}
		return null;
	}

	/// <summary>Dynamic Client Registration (RFC 7591) with an exact, freshly-chosen loopback redirect URI.</summary>
	private async Task<bool> TryRegisterClientAsync(McpOAuthConfig oauth, string redirectUri, CancellationToken ct)
	{
		try
		{
			var payload = JsonSerializer.Serialize(new
			{
				redirect_uris              = new[] { redirectUri },
				token_endpoint_auth_method = "none",
				grant_types                = new[] { "authorization_code", "refresh_token" },
				response_types             = new[] { "code" },
			});
			using var content  = new StringContent(payload, Encoding.UTF8, "application/json");
			using var response = await _http.PostAsync(oauth.RegistrationEndpoint, content, ct);
			if (!response.IsSuccessStatusCode)
				return false;

			var json = await response.Content.ReadAsStringAsync(ct);
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			var clientId = root.TryGetProperty("client_id", out var c) ? c.GetString() : null;
			if (string.IsNullOrEmpty(clientId))
				return false;

			oauth.ClientId     = clientId;
			oauth.ClientSecret = root.TryGetProperty("client_secret", out var s) ? s.GetString() : null;
			return true;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "McpOAuthService: Dynamic Client Registration failed");
			return false;
		}
	}

	private static string BuildAuthorizationUrl(McpOAuthConfig oauth, string redirectUri, string codeChallenge, string state)
	{
		var query = new List<string>
		{
			"response_type=code",
			$"client_id={Uri.EscapeDataString(oauth.ClientId)}",
			$"redirect_uri={Uri.EscapeDataString(redirectUri)}",
			$"code_challenge={Uri.EscapeDataString(codeChallenge)}",
			"code_challenge_method=S256",
			$"state={Uri.EscapeDataString(state)}",
		};
		if (!string.IsNullOrEmpty(oauth.Scopes))
			query.Add($"scope={Uri.EscapeDataString(oauth.Scopes)}");

		var separator = oauth.AuthorizationEndpoint.Contains('?') ? "&" : "?";
		return oauth.AuthorizationEndpoint + separator + string.Join("&", query);
	}

	private static void OpenBrowser(string url) =>
		Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

	/// <summary>
	/// Blocks until the redirect arrives (or cancellation), serving a minimal confirmation page.
	/// Registers a cancellation callback that stops the listener so a pending GetContextAsync()
	/// unblocks — HttpListener has no CancellationToken-aware overload.
	/// </summary>
	private static async Task<string?> WaitForRedirectAsync(HttpListener listener, string expectedState, CancellationToken ct)
	{
		using var registration = ct.Register(() =>
		{
			try { listener.Stop(); } catch { /* unblocks GetContextAsync */ }
		});

		HttpListenerContext ctx;
		try
		{
			ctx = await listener.GetContextAsync();
		}
		catch (Exception) when (ct.IsCancellationRequested)
		{
			ct.ThrowIfCancellationRequested();
			throw; // unreachable — ThrowIfCancellationRequested always throws here
		}

		var query = ParseQuery(ctx.Request.Url?.Query);
		var hasCode = query.ContainsKey("code");
		var html = hasCode
			? "<html><body>Authorized — you can close this window and return to TessynDesktop.</body></html>"
			: "<html><body>Authorization failed or was denied — you can close this window.</body></html>";
		var bytes = Encoding.UTF8.GetBytes(html);
		ctx.Response.ContentType     = "text/html";
		ctx.Response.ContentLength64 = bytes.Length;
		await ctx.Response.OutputStream.WriteAsync(bytes);
		ctx.Response.Close();

		if (!query.TryGetValue("state", out var returnedState) || returnedState != expectedState)
			return null; // missing/mismatched state — reject rather than risk a CSRF'd code

		return query.TryGetValue("code", out var code) ? code : null;
	}

	private static Dictionary<string, string> ParseQuery(string? query)
	{
		var result = new Dictionary<string, string>();
		if (string.IsNullOrEmpty(query))
			return result;

		foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var idx = pair.IndexOf('=');
			if (idx < 0)
				continue;
			result[Uri.UnescapeDataString(pair[..idx])] = Uri.UnescapeDataString(pair[(idx + 1)..]);
		}
		return result;
	}

	private async Task<McpTokenRecord?> ExchangeCodeAsync(
		McpOAuthConfig oauth, string code, string redirectUri, string codeVerifier, CancellationToken ct)
	{
		try
		{
			var form = new Dictionary<string, string>
			{
				["grant_type"]    = "authorization_code",
				["code"]          = code,
				["redirect_uri"]  = redirectUri,
				["client_id"]     = oauth.ClientId,
				["code_verifier"] = codeVerifier,
			};
			if (!string.IsNullOrEmpty(oauth.ClientSecret))
				form["client_secret"] = oauth.ClientSecret;

			using var response = await _http.PostAsync(oauth.TokenEndpoint, new FormUrlEncodedContent(form), ct);
			if (!response.IsSuccessStatusCode)
			{
				_log.Warning("McpOAuthService: token exchange failed: {Status}", response.StatusCode);
				return null;
			}

			var json = await response.Content.ReadAsStringAsync(ct);
			return ParseTokenResponse(json, fallbackRefreshToken: null);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "McpOAuthService: token exchange threw");
			return null;
		}
	}

	private static string GenerateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

	private static string ComputeCodeChallenge(string codeVerifier) =>
		Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

	private static string Base64UrlEncode(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static int FindFreeLoopbackPort()
	{
		var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
