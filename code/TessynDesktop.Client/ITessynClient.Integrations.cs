using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// Package J3 part 2: the MCP registry, the Telegram token, the relay link and the model refresh. All are loopback only; the daemon announces them with
/// the <c>mcp.registry</c>, <c>telegram.settings</c>, <c>relay.settings</c> and <c>models.admin</c> capabilities. <c>mcp.changed</c> and
/// <c>catalog.changed</c> arrive as notifications (<see cref="IntegrationsNotificationExtensions"/> reads them). Secrets are write-only: no result carries one.
/// </summary>
public partial interface ITessynClient
{
	/// <summary><c>mcp.list</c>: the registered MCP servers.</summary>
	Task<McpListResult> McpListAsync(CancellationToken cancellationToken = default);

	/// <summary><c>mcp.upsert</c>: creates a server or updates one; a member left out keeps its value, the secret members are write-only.</summary>
	Task<McpServerDto> McpUpsertAsync(McpServerUpsertRequest request, CancellationToken cancellationToken = default);

	/// <summary><c>mcp.remove</c>: removes a server and its token; <c>Removed</c> is false when it was not there.</summary>
	Task<McpRemoveResult> McpRemoveAsync(string id, CancellationToken cancellationToken = default);

	/// <summary><c>mcp.setEnabled</c>.</summary>
	Task<McpServerDto> McpSetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default);

	/// <summary><c>mcp.discover</c>: probes a server's OAuth metadata (the daemon stops after 15 s and answers <c>TimedOut</c>); <paramref name="known"/> endpoints are kept.</summary>
	Task<McpDiscoverResult> McpDiscoverAsync(string url, McpDiscoverKnownEndpoints? known = null, CancellationToken cancellationToken = default);

	/// <summary><c>mcp.authorize</c>: runs the OAuth flow (the browser opens on the daemon's machine) and answers when it ends, which can take minutes.</summary>
	Task<McpAuthorizeResultDto> McpAuthorizeAsync(string id, CancellationToken cancellationToken = default);

	/// <summary><c>telegram.status</c>.</summary>
	Task<TelegramStatusDto> TelegramStatusAsync(CancellationToken cancellationToken = default);

	/// <summary><c>telegram.setToken</c>: stores the bot token (write-only) and starts the bridge when it is enabled.</summary>
	Task<TelegramStatusDto> TelegramSetTokenAsync(string token, CancellationToken cancellationToken = default);

	/// <summary><c>telegram.clearToken</c>: removes the bot token and stops the bridge.</summary>
	Task<TelegramStatusDto> TelegramClearTokenAsync(CancellationToken cancellationToken = default);

	/// <summary><c>relay.status</c>.</summary>
	Task<RelayStatusDto> RelayStatusAsync(CancellationToken cancellationToken = default);

	/// <summary><c>relay.configure</c>: stores the link (the token is write-only; send it again when the URL moves to another host) and restarts the connector.</summary>
	Task<RelayStatusDto> RelayConfigureAsync(RelayConfigureRequest request, CancellationToken cancellationToken = default);

	/// <summary><c>models.refresh</c>: asks the local Ollama again; <c>catalog.changed</c> follows.</summary>
	Task<ModelsRefreshResult> ModelsRefreshAsync(CancellationToken cancellationToken = default);
}
