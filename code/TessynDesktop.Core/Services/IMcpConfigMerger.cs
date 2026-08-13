using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the <c>mcpServers</c> map contributed by the External MCP Server Registry (FR.20.2),
/// independent of the always-present <c>cmx</c> entry that <c>AgentMcpServer</c> owns.
/// Extracted so the registry-merge shape (Stdio vs Http, auth header resolution) is unit-testable
/// without instantiating <c>AgentMcpServer</c>'s full dependency graph.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IMcpConfigMerger
{
	/// <summary>
	/// Returns one <c>mcpServers</c> entry per enabled registry entry, keyed by <see cref="ExternalMcpServerModel.Id"/>.
	/// An <see cref="McpServerAuthType.OAuth2"/> entry whose access token can't be resolved (never
	/// authorized, or refresh failed) is omitted entirely rather than sent with a stale/invalid token (FR.20.6).
	/// </summary>
	Task<Dictionary<string, object>> BuildRegistryServersAsync(
		IReadOnlyList<ExternalMcpServerModel> entries, CancellationToken cancellationToken = default);
}
