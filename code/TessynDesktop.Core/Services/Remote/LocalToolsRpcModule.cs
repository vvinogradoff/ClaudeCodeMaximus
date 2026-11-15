using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary><c>localTools.get</c> and <c>localTools.set</c> (P1.J1): the Local Tool Profile of a directory (FR.12.21). Loopback only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LocalToolsRpcModule : IRemoteRpcModule
{
	private readonly ILocalToolsService _localTools;

	public LocalToolsRpcModule(ILocalToolsService localTools)
	{
		_localTools = localTools;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.LocalTools.GetMethod, Get, remoteCallable: false);
		registry.AddSync(RemoteProtocol.LocalTools.SetMethod, Set, remoteCallable: false);
	}

	private object? Get(JsonNode? @params) =>
		_localTools.Get(TdcpParams.RequireString(@params, "directoryId"));

	private object? Set(JsonNode? @params) =>
		_localTools.Set(
			TdcpParams.RequireString(@params, "directoryId"),
			TdcpParams.RequireBool(@params, "enabled"),
			TdcpListParams.RequireStrings(@params, "nativeTools"),
			TdcpParams.RequireBool(@params, "includeCmxMcp"),
			TdcpListParams.RequireStrings(@params, "includedMcpServerIds"));
}
