using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>The E2E daemon has no MCP registry entries, so no OAuth token is ever needed by the Codex thread config.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class NoMcpOAuthService : IMcpOAuthService
{
	public Task<string?> GetValidAccessTokenAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

	public Task<McpAuthorizeResult> AuthorizeInteractiveAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task DiscoverAsync(string baseUrl, McpOAuthConfig oauth, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
