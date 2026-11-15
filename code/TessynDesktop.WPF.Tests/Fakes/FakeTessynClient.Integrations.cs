using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Package J3 part 2 members of <see cref="FakeTessynClient"/>: the MCP registry, Telegram, relay and model refresh. Each is scriptable and records its call; no result carries a secret, as on the wire.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	/// <summary>The servers <see cref="McpListAsync"/> answers; <see cref="McpUpsertAsync"/>, remove and enable change it.</summary>
	public List<McpServerDto> McpServers { get; set; } = [];

	/// <summary>Every upsert request the client sent (the secret members are in it; they are not in any answer).</summary>
	public List<McpServerUpsertRequest> McpUpserts { get; } = [];

	/// <summary>When set, the MCP methods throw it.</summary>
	public Exception? McpException { get; set; }

	/// <summary>What <see cref="McpDiscoverAsync"/> answers.</summary>
	public McpDiscoverResult McpDiscoverAnswer { get; set; } = new(new McpDiscoveredConfigDto(string.Empty, string.Empty, null, true), TimedOut: false);

	/// <summary>What <see cref="McpAuthorizeAsync"/> answers.</summary>
	public McpAuthorizeResultDto McpAuthorizeAnswer { get; set; } = new(true);

	/// <summary>What the Telegram methods answer; <see cref="TelegramSetTokenAsync"/> and clear change <c>HasToken</c>.</summary>
	public TelegramStatusDto TelegramStatus { get; set; } = new(Enabled: false, Running: false, HasToken: false);

	/// <summary>What the relay methods answer; <see cref="RelayConfigureAsync"/> applies its request to it.</summary>
	public RelayStatusDto RelayStatus { get; set; } = new(Enabled: false, RelayStateDto.Disabled, null, HasToken: false, DateTimeOffset.UnixEpoch);

	/// <summary>The relay link tokens <see cref="RelayConfigureAsync"/> received (write-only on the wire).</summary>
	public List<string> RelayTokensReceived { get; } = [];

	/// <summary>What <see cref="ModelsRefreshAsync"/> answers.</summary>
	public int ModelsRefreshCount { get; set; }

	public Task<McpListResult> McpListAsync(CancellationToken cancellationToken = default)
	{
		Record("mcp.list");
		ThrowIfMcpFails();
		return Task.FromResult(new McpListResult([.. McpServers]));
	}

	public Task<McpServerDto> McpUpsertAsync(McpServerUpsertRequest request, CancellationToken cancellationToken = default)
	{
		Record($"mcp.upsert:{request.Id ?? "new"}");
		ThrowIfMcpFails();
		lock (McpUpserts)
			McpUpserts.Add(request);

		var id = request.Id ?? $"mcp_{McpServers.Count + 1}";
		var index = McpServers.FindIndex(s => s.Id == id);
		var current = index >= 0 ? McpServers[index] : null;
		var server = new McpServerDto(
			id,
			request.DisplayName ?? current?.DisplayName ?? id,
			request.Enabled ?? current?.Enabled ?? true,
			request.Transport ?? current?.Transport ?? McpTransportDto.Http,
			request.Url ?? current?.Url,
			current?.UrlHasQuery ?? false,
			request.Command ?? current?.Command,
			!string.IsNullOrEmpty(request.Arguments) || (current?.HasArguments ?? false),
			request.WorkingDirectory ?? current?.WorkingDirectory,
			request.AuthType ?? current?.AuthType ?? McpAuthTypeDto.None,
			request.StaticHeaderName ?? current?.StaticHeaderName,
			!string.IsNullOrEmpty(request.StaticHeaderValue) || (current?.HasStaticHeaderValue ?? false),
			current?.Oauth,
			current?.Authorized ?? false,
			current?.Status ?? McpServerStatusDto.NotApplicable,
			current?.ConnectedIdentity);

		if (index >= 0)
			McpServers[index] = server;
		else
			McpServers.Add(server);

		return Task.FromResult(server);
	}

	public Task<McpRemoveResult> McpRemoveAsync(string id, CancellationToken cancellationToken = default)
	{
		Record($"mcp.remove:{id}");
		ThrowIfMcpFails();
		return Task.FromResult(new McpRemoveResult(McpServers.RemoveAll(s => s.Id == id) > 0));
	}

	public Task<McpServerDto> McpSetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
	{
		Record($"mcp.setEnabled:{id}:{enabled}");
		ThrowIfMcpFails();
		var index = McpServers.FindIndex(s => s.Id == id);
		if (index < 0)
			throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, $"There is no MCP server '{id}'.");

		return Task.FromResult(McpServers[index] = McpServers[index] with { Enabled = enabled });
	}

	public Task<McpDiscoverResult> McpDiscoverAsync(string url, McpDiscoverKnownEndpoints? known = null, CancellationToken cancellationToken = default)
	{
		Record($"mcp.discover:{url}");
		ThrowIfMcpFails();
		return Task.FromResult(McpDiscoverAnswer);
	}

	public Task<McpAuthorizeResultDto> McpAuthorizeAsync(string id, CancellationToken cancellationToken = default)
	{
		Record($"mcp.authorize:{id}");
		ThrowIfMcpFails();
		return Task.FromResult(McpAuthorizeAnswer);
	}

	public Task<TelegramStatusDto> TelegramStatusAsync(CancellationToken cancellationToken = default)
	{
		Record("telegram.status");
		return Task.FromResult(TelegramStatus);
	}

	public Task<TelegramStatusDto> TelegramSetTokenAsync(string token, CancellationToken cancellationToken = default)
	{
		Record("telegram.setToken");
		return Task.FromResult(TelegramStatus = TelegramStatus with { HasToken = true, Running = TelegramStatus.Enabled });
	}

	public Task<TelegramStatusDto> TelegramClearTokenAsync(CancellationToken cancellationToken = default)
	{
		Record("telegram.clearToken");
		return Task.FromResult(TelegramStatus = TelegramStatus with { HasToken = false, Running = false });
	}

	public Task<RelayStatusDto> RelayStatusAsync(CancellationToken cancellationToken = default)
	{
		Record("relay.status");
		return Task.FromResult(RelayStatus);
	}

	public Task<RelayStatusDto> RelayConfigureAsync(RelayConfigureRequest request, CancellationToken cancellationToken = default)
	{
		Record("relay.configure");
		if (request.LinkToken != null)
			RelayTokensReceived.Add(request.LinkToken);

		var enabled = request.Enabled ?? RelayStatus.Enabled;
		return Task.FromResult(RelayStatus = RelayStatus with
		{
			Enabled  = enabled,
			Url      = request.Url ?? RelayStatus.Url,
			HasToken = request.LinkToken != null ? request.LinkToken.Length > 0 : RelayStatus.HasToken,
			State    = enabled ? RelayStateDto.Connecting : RelayStateDto.Disabled,
		});
	}

	public Task<ModelsRefreshResult> ModelsRefreshAsync(CancellationToken cancellationToken = default)
	{
		Record("models.refresh");
		return Task.FromResult(new ModelsRefreshResult(ModelsRefreshCount));
	}

	private void ThrowIfMcpFails()
	{
		if (McpException != null)
			throw McpException;
	}
}
