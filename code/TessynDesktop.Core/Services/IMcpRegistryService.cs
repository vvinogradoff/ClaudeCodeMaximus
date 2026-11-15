using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The External MCP Server Registry as a daemon service (package J3 part 2): the servers of <c>AppSettingsModel.ExternalMcpServers</c> as DTOs that carry no
/// secret, validated writes, OAuth discovery with a time limit, and the interactive authorization. Every change publishes <c>mcp.changed</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IMcpRegistryService
{
	/// <summary>The registered servers in registry order.</summary>
	IReadOnlyList<McpServerDto> List();

	/// <summary>
	/// Creates a server (no id, or an unknown one) or updates one: a member the request leaves out keeps its value, and a secret member is never read back.
	/// A change of origin (the URL or the token endpoint) drops the secrets bound to the old one.
	/// </summary>
	/// <exception cref="TdcpInvalidParamsException">The request is invalid.</exception>
	McpServerDto Upsert(McpServerUpsertRequest request);

	/// <summary>Removes the server and its stored token; false when there was no such server.</summary>
	bool Remove(string id);

	/// <summary>Enables or disables a server.</summary>
	/// <exception cref="TdcpInvalidParamsException">There is no such server.</exception>
	McpServerDto SetEnabled(string id, bool enabled);

	/// <summary>Probes the server's OAuth metadata for at most <see cref="RemoteProtocol.McpApi.DiscoverTimeoutSeconds"/> seconds.</summary>
	/// <exception cref="TdcpInvalidParamsException">The URL or a known endpoint is invalid.</exception>
	Task<McpDiscoverResult> DiscoverAsync(string url, McpDiscoverKnownEndpoints? known, CancellationToken cancellationToken);

	/// <summary>Runs the interactive OAuth flow of a server; one flow per server at a time.</summary>
	/// <exception cref="TdcpInvalidParamsException">There is no such server.</exception>
	Task<McpAuthorizeResultDto> AuthorizeAsync(string id, CancellationToken cancellationToken);
}
