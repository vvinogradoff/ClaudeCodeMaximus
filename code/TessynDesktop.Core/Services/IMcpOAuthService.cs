using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Tessyn's own OAuth 2.1 (PKCE) client role for External MCP Server Registry entries whose
/// <see cref="ExternalMcpServerModel.AuthType"/> is <see cref="McpServerAuthType.OAuth2"/>
/// (FR.20.3–FR.20.6). Independent of claude.ai's connector authentication.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IMcpOAuthService
{
	/// <summary>
	/// Returns a currently-valid access token for the server, refreshing it first if it's expired
	/// or near-expiry. Returns null if the server was never authorized, or if refresh fails (in
	/// which case the server's <see cref="McpOAuthConfig.NeedsReauthorization"/> flag is set).
	/// </summary>
	Task<string?> GetValidAccessTokenAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs the full interactive authorization flow: endpoint discovery, optional Dynamic Client
	/// Registration, PKCE authorization via a loopback listener and the system browser, and token
	/// exchange. Persists the resulting tokens and updates the server's OAuth state on success.
	/// </summary>
	Task<McpAuthorizeResult> AuthorizeInteractiveAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default);

	/// <summary>
	/// Best-effort discovery of authorization/token endpoints for a server URL (FR.20.3), without
	/// running the rest of the interactive authorization flow. Probes
	/// <c>.well-known/oauth-protected-resource</c> then <c>.well-known/oauth-authorization-server</c>,
	/// filling only whichever of <paramref name="oauth"/>'s <see cref="McpOAuthConfig.AuthorizationEndpoint"/>/
	/// <see cref="McpOAuthConfig.TokenEndpoint"/> is currently empty (never overwrites a value the
	/// user already entered), and always refreshing <see cref="McpOAuthConfig.RegistrationEndpoint"/>
	/// if the metadata document reports one. Any failure is swallowed — this is an auto-fill
	/// convenience, not a hard requirement — but <see cref="McpOAuthConfig.DiscoveryCompleted"/> is
	/// always set on the passed-in <paramref name="oauth"/> so a later Authorize doesn't re-probe it.
	/// Used by the Settings UI (FR.20.7) Add/Edit dialog as soon as a server URL is entered.
	/// </summary>
	Task DiscoverAsync(string baseUrl, McpOAuthConfig oauth, CancellationToken cancellationToken = default);
}
