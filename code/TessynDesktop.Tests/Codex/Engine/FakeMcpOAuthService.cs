using System;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>Hands out a fixed bearer token for every OAuth registry entry (the refresh of FR.20.6 is not under test here).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeMcpOAuthService : IMcpOAuthService
{
	public string? Token { get; set; } = "OAUTH-SECRET-1";

	public Task<string?> GetValidAccessTokenAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default) => Task.FromResult(Token);

	public Task<McpAuthorizeResult> AuthorizeInteractiveAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task DiscoverAsync(string baseUrl, McpOAuthConfig oauth, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
