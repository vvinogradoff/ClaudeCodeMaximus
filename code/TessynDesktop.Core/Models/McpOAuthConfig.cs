namespace TessynDesktop.Models;

/// <summary>
/// OAuth 2.1 (PKCE) client configuration for an <see cref="ExternalMcpServerModel"/> entry whose
/// <see cref="ExternalMcpServerModel.AuthType"/> is <see cref="McpServerAuthType.OAuth2"/> (FR.20.3).
/// Tessyn is its own OAuth client here — independent of claude.ai's connector authentication.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpOAuthConfig
{
	/// <summary>
	/// Authorization endpoint URL. Populated by discovery (FR.20.3's well-known probe) when the
	/// server supports it, otherwise entered manually.
	/// </summary>
	public string AuthorizationEndpoint { get; set; } = string.Empty;

	/// <summary>Token endpoint URL (code exchange and refresh).</summary>
	public string TokenEndpoint { get; set; } = string.Empty;

	/// <summary>
	/// Dynamic Client Registration endpoint (RFC 7591), if discovered. Null when the server has no
	/// registration endpoint and a manually-provisioned <see cref="ClientId"/> must be used instead.
	/// </summary>
	public string? RegistrationEndpoint { get; set; }

	/// <summary>
	/// OAuth client ID. Filled in automatically after a successful Dynamic Client Registration, or
	/// entered manually for servers that hand out a pre-provisioned client identity instead.
	/// </summary>
	public string ClientId { get; set; } = string.Empty;

	/// <summary>Client secret, only if the server's auth flow requires one (most PKCE public clients don't).</summary>
	public string? ClientSecret { get; set; }

	/// <summary>Space-delimited OAuth scopes requested during authorization.</summary>
	public string Scopes { get; set; } = string.Empty;

	/// <summary>
	/// True once the well-known discovery documents have been successfully probed for this server,
	/// so subsequent authorizations skip re-discovery unless the user clears this.
	/// </summary>
	public bool DiscoveryCompleted { get; set; }

	/// <summary>
	/// Set by <c>McpOAuthService</c> when a token refresh fails (revoked/expired refresh token).
	/// Drives the "Needs re-authorization" status in Settings (FR.20.7). Cleared by a successful
	/// interactive re-authorization.
	/// </summary>
	public bool NeedsReauthorization { get; set; }

	/// <summary>
	/// Best-effort human-readable identity resolved after a successful authorization (e.g. an
	/// account name/email, if the server exposes one). Purely informational for the Settings UI.
	/// </summary>
	public string? ConnectedIdentity { get; set; }
}
