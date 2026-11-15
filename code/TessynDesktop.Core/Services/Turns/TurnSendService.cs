using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITurnSendService" />
/// <remarks>Created by Claude</remarks>
public sealed class TurnSendService : ITurnSendService
{
	private static readonly ILogger _log = Log.ForContext<TurnSendService>();

	private readonly ITurnEngine _engine;
	private readonly ISessionRegistry _registry;

	// Turns accepted but not finished, per node: a second send that arrives before the first has reached the engine (and set the running
	// flag) must still be reported as queued.
	private readonly ConcurrentDictionary<string, int> _pending = new();

	public TurnSendService(ITurnEngine engine, ISessionRegistry registry)
	{
		_engine   = engine;
		_registry = registry;
	}

	/// <inheritdoc />
	public TurnTicketModel Send(SessionNodeModel node, string text, TurnSettings settings, string? clientRequestId)
	{
		var nodeId = node.NodeId;
		var queued = _registry.IsSessionRunning(nodeId) || _engine.IsBusy(nodeId) || _pending.TryGetValue(nodeId, out var waiting) && waiting > 0;
		var turnId = Guid.NewGuid().ToString(Constants.Turns.TurnIdFormat);

		_pending.AddOrUpdate(nodeId, 1, (_, count) => count + 1);
		_ = Task.Run(() => RunAsync(new TurnRequest(node, text, settings, IsNewBranch: false, turnId, clientRequestId)));

		return new TurnTicketModel(turnId, queued);
	}

	private async Task RunAsync(TurnRequest request)
	{
		try
		{
			await _engine.RunAsync(request);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Turn {TurnId} for node {NodeId} failed", request.TurnId, request.Node.NodeId);
		}
		finally
		{
			_pending.AddOrUpdate(request.Node.NodeId, 0, (_, count) => Math.Max(0, count - 1));
		}
	}
}
