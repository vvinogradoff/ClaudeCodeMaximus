using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The interactive turn methods of a local client (P1.I): <c>turn.send</c> starts an engine turn and answers with its id, <c>turn.stop</c>
/// stops the session's turn. Both are loopback only (<c>remoteCallable: false</c>); the 21 methods of the web contract are untouched, and
/// <c>session.send</c> stays as it is. The progress of the turn arrives as <c>turn.*</c> notifications on the local stream.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnRpcModule : IRemoteRpcModule
{
	private readonly ISessionRegistry _registry;
	private readonly ITurnOptionsResolver _options;
	private readonly ITurnSettingsResolver _settings;
	private readonly ITurnSendService _send;
	private readonly ITurnEngine _engine;
	private readonly ISessionTurnService _turns;
	private readonly ISessionEngineRouter _engines;
	private readonly ISessionModelSettings _modelSettings;

	public TurnRpcModule(
		ISessionRegistry registry,
		ITurnOptionsResolver options,
		ITurnSettingsResolver settings,
		ITurnSendService send,
		ITurnEngine engine,
		ISessionTurnService turns,
		ISessionEngineRouter engines,
		ISessionModelSettings modelSettings)
	{
		_registry  = registry;
		_options   = options;
		_settings  = settings;
		_send      = send;
		_engine    = engine;
		_turns     = turns;
		_engines = engines;
		_modelSettings = modelSettings;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(RemoteProtocol.TurnStream.SendMethod, async (p, _) => await SendAsync(p), remoteCallable: false);
		registry.AddSync(RemoteProtocol.TurnStream.StopMethod, Stop, remoteCallable: false);
	}

	private async Task<object?> SendAsync(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var autoCompact = TdcpParams.OptionalBool(@params, "autoCompact") ?? false;
		var newBranch   = TdcpParams.OptionalBool(@params, "newBranch") ?? false;

		// An empty text is a compaction request when autoCompact is set (FR.11.6); otherwise it is as blank as ever.
		var text = autoCompact ? (TdcpParams.OptionalString(@params, "text") ?? string.Empty).Trim() : TdcpParams.RequireString(@params, "text").Trim();
		if (text.Length == 0 && !autoCompact)
			throw new TdcpInvalidParamsException("'text' must not be blank");

		var clientRequestId = TdcpParams.OptionalString(@params, "clientRequestId");
		var options         = await _options.ResolveAsync(@params);
		var settings        = await _settings.ResolveAsync(node, options);
		var ticket          = _send.Send(node, text, settings, clientRequestId, newBranch, autoCompact);

		// FR.12.35: a model or effort the user picked that differs from the session's setting is kept on the session.
		_modelSettings.RecordPicks(node, options);

		return new TurnSendResult(ticket.TurnId, Accepted: true, ticket.Queued);
	}

	/// <summary>Cancels the session's engine and headless turns first (so a streaming turn ends as stopped), then kills a live streaming process.</summary>
	private object? Stop(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));

		var cancelled = _engine.CancelRuns(node.NodeId);
		cancelled    |= _turns.CancelTurn(node.NodeId);

		var processAlive = _engines.IsAlive(node.NodeId);
		if (processAlive)
			_engines.Stop(node.NodeId);

		return new SessionStopResult(processAlive || cancelled);
	}
}
