using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>session.setOptions</c> and <c>session.resetConversation</c> (P1.J1), loopback only. The options are the instruction toggles of FR.11 that
/// are stored on the session node; the new Auto-Compact and New Branch flags are per-run and travel with <c>turn.send</c>, not here.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionOptionsRpcModule : IRemoteRpcModule
{
	private readonly ISessionOptionsService _options;

	public SessionOptionsRpcModule(ISessionOptionsService options)
	{
		_options = options;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add    (RemoteProtocol.SessionOptions.SetMethod,   (p, _) => SetAsync(p), remoteCallable: false);
		registry.AddSync(RemoteProtocol.SessionOptions.ResetMethod, Reset,                 remoteCallable: false);
	}

	private async Task<object?> SetAsync(JsonNode? @params)
	{
		var nodeId = TdcpParams.RequireString(@params, "nodeId");

		// Present with JSON null means "No preference"; absent means "leave it". TdcpParams.OptionalBool cannot tell them apart.
		var changeAutoCommit = @params is JsonObject obj && obj.ContainsKey("autoCommit");
		var autoCommit       = changeAutoCommit ? TdcpParams.OptionalBool(@params, "autoCommit") : null;

		var patch = new SessionOptionsPatch(
			changeAutoCommit,
			autoCommit,
			TdcpParams.OptionalBool(@params, "autoDocument"),
			TdcpParams.OptionalBool(@params, "playCompletionSound"));

		return await _options.SetAsync(nodeId, patch);
	}

	private object? Reset(JsonNode? @params) =>
		_options.ResetConversation(TdcpParams.RequireString(@params, "nodeId"));
}
