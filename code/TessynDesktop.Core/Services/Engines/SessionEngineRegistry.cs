using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionEngineRouter" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionEngineRegistry : ISessionEngineRouter
{
	private readonly IReadOnlyList<ISessionEngine> _engines;
	private readonly Dictionary<string, ISessionEngine> _byId;
	private readonly ILogger _log;

	// Nodes whose unknown engine id was already reported: one Warning per node, not one per turn.
	private readonly ConcurrentDictionary<string, byte> _warnedNodes = new(StringComparer.Ordinal);

	public SessionEngineRegistry(IEnumerable<ISessionEngine> engines, ILogger? log = null)
	{
		_log     = log ?? Log.ForContext<SessionEngineRegistry>();
		_engines = engines.ToArray();
		_byId    = new Dictionary<string, ISessionEngine>(StringComparer.OrdinalIgnoreCase);

		foreach (var engine in _engines)
		{
			if (!_byId.TryAdd(engine.EngineId, engine))
				throw new ArgumentException($"Session engine '{engine.EngineId}' is registered twice", nameof(engines));
		}

		if (!_byId.ContainsKey(Constants.SessionEngines.Claude))
			throw new ArgumentException($"The '{Constants.SessionEngines.Claude}' session engine must be registered", nameof(engines));
	}

	/// <inheritdoc />
	public IReadOnlyList<ISessionEngine> Engines => _engines;

	/// <inheritdoc />
	public ISessionEngine Get(string? engineId)
	{
		var id = string.IsNullOrEmpty(engineId) ? Constants.SessionEngines.Claude : engineId;
		return _byId.TryGetValue(id, out var engine)
			? engine
			: throw new InvalidOperationException($"Unknown session engine '{engineId}'");
	}

	/// <summary>
	/// The engine the session runs on. <see cref="SessionNodeModel.EngineId"/> is only a stored cache of the engine of the resolved model
	/// (FR.33.4), so an id this build does not know (written by a newer build, or edited by hand) never fails a turn: the session runs on
	/// Claude, and the router logs one Warning per node.
	/// </summary>
	public ISessionEngine For(SessionNodeModel node)
	{
		var id = string.IsNullOrEmpty(node.EngineId) ? Constants.SessionEngines.Claude : node.EngineId;
		if (_byId.TryGetValue(id, out var engine))
			return engine;

		if (_warnedNodes.TryAdd(node.NodeId, 0))
			_log.Warning("Unknown engine '{EngineId:l}' on session {NodeId:l}; using Claude.", node.EngineId, node.NodeId);

		return _byId[Constants.SessionEngines.Claude];
	}

	/// <inheritdoc />
	public bool IsAlive(string nodeId) => _engines.Any(e => e.IsAlive(nodeId));

	/// <inheritdoc />
	public void Stop(string nodeId)
	{
		foreach (var engine in _engines)
		{
			if (engine.IsAlive(nodeId))
				engine.Stop(nodeId);
		}
	}

	/// <inheritdoc />
	public void TerminateAll()
	{
		foreach (var engine in _engines)
			engine.TerminateAll();
	}
}
