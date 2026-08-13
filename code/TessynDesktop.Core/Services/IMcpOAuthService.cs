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
}
