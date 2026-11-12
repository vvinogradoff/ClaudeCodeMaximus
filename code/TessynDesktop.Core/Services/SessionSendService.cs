using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionSendService" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionSendService : ISessionSendService
{
	private static readonly ILogger _log = Log.ForContext<SessionSendService>();

	private readonly ISessionTurnService _turns;
	private readonly ISessionRegistry _registry;

	// Web turns accepted but not finished, per node: a second send that arrives before the first has taken the
	// turn lock (and set the running flag) must still be reported as queued.
	private readonly ConcurrentDictionary<string, int> _pending = new();

	public SessionSendService(ISessionTurnService turns, ISessionRegistry registry)
	{
		_turns    = turns;
		_registry = registry;
	}

	/// <inheritdoc />
	public bool Send(SessionNodeModel node, string text, TurnOptionsModel? options)
	{
		var nodeId = node.NodeId;
		var queued = _registry.IsSessionRunning(nodeId) || _pending.TryGetValue(nodeId, out var waiting) && waiting > 0;

		_pending.AddOrUpdate(nodeId, 1, (_, count) => count + 1);
		_ = Task.Run(() => RunAsync(node, text, options));

		return queued;
	}

	private async Task RunAsync(SessionNodeModel node, string text, TurnOptionsModel? options)
	{
		try
		{
			await _turns.RunTurnAsync(node, text, TurnSource.Web, options);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Web turn for node {NodeId} failed", node.NodeId);
		}
		finally
		{
			_pending.AddOrUpdate(node.NodeId, 0, (_, count) => Math.Max(0, count - 1));
		}
	}
}
