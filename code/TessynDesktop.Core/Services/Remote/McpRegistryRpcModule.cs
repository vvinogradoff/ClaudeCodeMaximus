using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>mcp.list</c>, <c>mcp.upsert</c>, <c>mcp.remove</c>, <c>mcp.setEnabled</c>, <c>mcp.discover</c> and <c>mcp.authorize</c> (package J3 part 2) over
/// <see cref="IMcpRegistryService"/>. Loopback only: a stdio server is a command the Claude process starts, the OAuth flow opens a browser on this machine,
/// and the write methods take header values and client secrets.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpRegistryRpcModule : IRemoteRpcModule
{
	private readonly IMcpRegistryService _registry;

	public McpRegistryRpcModule(IMcpRegistryService registry)
	{
		_registry = registry;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry methods)
	{
		methods.AddSync(RemoteProtocol.McpApi.ListMethod, _ => new McpListResult(_registry.List()), remoteCallable: false);
		methods.AddSync(RemoteProtocol.McpApi.UpsertMethod, Upsert, remoteCallable: false);
		methods.AddSync(RemoteProtocol.McpApi.RemoveMethod, Remove, remoteCallable: false);
		methods.AddSync(RemoteProtocol.McpApi.SetEnabledMethod, SetEnabled, remoteCallable: false);
		methods.Add(RemoteProtocol.McpApi.DiscoverMethod, DiscoverAsync, remoteCallable: false);
		methods.Add(RemoteProtocol.McpApi.AuthorizeMethod, AuthorizeAsync, remoteCallable: false);
	}

	private object? Upsert(JsonNode? @params) =>
		_registry.Upsert(SettingsPatchReader.Read<McpServerUpsertRequest>(@params ?? new JsonObject(), "params"));

	private object? Remove(JsonNode? @params) => new McpRemoveResult(_registry.Remove(TdcpParams.RequireString(@params, "id")));

	private object? SetEnabled(JsonNode? @params) =>
		_registry.SetEnabled(TdcpParams.RequireString(@params, "id"), TdcpParams.RequireBool(@params, "enabled"));

	private async Task<object?> DiscoverAsync(JsonNode? @params, CancellationToken cancellationToken)
	{
		var url   = TdcpParams.RequireString(@params, "url");
		var known = TdcpParams.Find(@params, "oauth") is { } node ? SettingsPatchReader.Read<McpDiscoverKnownEndpoints>(node, "oauth") : null;

		return await _registry.DiscoverAsync(url, known, cancellationToken);
	}

	private async Task<object?> AuthorizeAsync(JsonNode? @params, CancellationToken cancellationToken) =>
		await _registry.AuthorizeAsync(TdcpParams.RequireString(@params, "id"), cancellationToken);
}
