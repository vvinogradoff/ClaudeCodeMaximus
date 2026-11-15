using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<McpListResult> McpListAsync(CancellationToken cancellationToken = default) =>
		CallAsync<McpListResult>(RemoteProtocol.McpApi.ListMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<McpServerDto> McpUpsertAsync(McpServerUpsertRequest request, CancellationToken cancellationToken = default) =>
		CallAsync<McpServerDto>(RemoteProtocol.McpApi.UpsertMethod, (JsonObject)JsonSerializer.SerializeToNode(request, TdcpJsonOptions.Default)!, cancellationToken);

	/// <inheritdoc />
	public Task<McpRemoveResult> McpRemoveAsync(string id, CancellationToken cancellationToken = default) =>
		CallAsync<McpRemoveResult>(RemoteProtocol.McpApi.RemoveMethod, Params(("id", Node(id))), cancellationToken);

	/// <inheritdoc />
	public Task<McpServerDto> McpSetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default) =>
		CallAsync<McpServerDto>(RemoteProtocol.McpApi.SetEnabledMethod, Params(("id", Node(id)), ("enabled", Node(enabled))), cancellationToken);

	/// <inheritdoc />
	public Task<McpDiscoverResult> McpDiscoverAsync(string url, McpDiscoverKnownEndpoints? known = null, CancellationToken cancellationToken = default) =>
		CallAsync<McpDiscoverResult>(
			RemoteProtocol.McpApi.DiscoverMethod,
			Params(("url", Node(url)), ("oauth", known == null ? null : JsonSerializer.SerializeToNode(known, TdcpJsonOptions.Default))),
			cancellationToken);

	/// <inheritdoc />
	public Task<McpAuthorizeResultDto> McpAuthorizeAsync(string id, CancellationToken cancellationToken = default) =>
		CallAsync<McpAuthorizeResultDto>(RemoteProtocol.McpApi.AuthorizeMethod, Params(("id", Node(id))), cancellationToken, _options.InteractiveRequestTimeout);

	/// <inheritdoc />
	public Task<TelegramStatusDto> TelegramStatusAsync(CancellationToken cancellationToken = default) =>
		CallAsync<TelegramStatusDto>(RemoteProtocol.TelegramApi.StatusMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<TelegramStatusDto> TelegramSetTokenAsync(string token, CancellationToken cancellationToken = default) =>
		CallAsync<TelegramStatusDto>(RemoteProtocol.TelegramApi.SetTokenMethod, Params(("token", Node(token))), cancellationToken);

	/// <inheritdoc />
	public Task<TelegramStatusDto> TelegramClearTokenAsync(CancellationToken cancellationToken = default) =>
		CallAsync<TelegramStatusDto>(RemoteProtocol.TelegramApi.ClearTokenMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<RelayStatusDto> RelayStatusAsync(CancellationToken cancellationToken = default) =>
		CallAsync<RelayStatusDto>(RemoteProtocol.RelayApi.StatusMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<RelayStatusDto> RelayConfigureAsync(RelayConfigureRequest request, CancellationToken cancellationToken = default) =>
		CallAsync<RelayStatusDto>(RemoteProtocol.RelayApi.ConfigureMethod, (JsonObject)JsonSerializer.SerializeToNode(request, TdcpJsonOptions.Default)!, cancellationToken);

	/// <inheritdoc />
	public Task<ModelsRefreshResult> ModelsRefreshAsync(CancellationToken cancellationToken = default) =>
		CallAsync<ModelsRefreshResult>(RemoteProtocol.ModelsApi.RefreshMethod, null, cancellationToken, _options.InteractiveRequestTimeout);
}
