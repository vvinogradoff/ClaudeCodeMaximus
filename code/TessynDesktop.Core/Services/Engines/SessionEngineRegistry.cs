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
	private readonly ISessionEngineSelector? _selector;
	private readonly IAppSettingsService? _appSettings;

	// Nodes whose unknown engine id was already reported: one Warning per node, not one per turn.
	private readonly ConcurrentDictionary<string, byte> _warnedNodes = new(StringComparer.Ordinal);

	public SessionEngineRegistry(IEnumerable<ISessionEngine> engines, ILogger? log = null, ISessionEngineSelector? selector = null,
		IAppSettingsService? appSettings = null)
	{
		_log         = log ?? Log.ForContext<SessionEngineRegistry>();
		_selector    = selector;
		_appSettings = appSettings;
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
	public ISessionEngine For(SessionNodeModel node) => Resolve(node, node.EngineId);

	/// <inheritdoc />
	public ISessionEngine Peek(SessionNodeModel node, string? modelId) =>
		Resolve(node, _selector?.SelectEngineId(node, modelId) is { } selected && _byId.ContainsKey(selected) ? selected : node.EngineId);

	/// <inheritdoc />
	public ISessionEngine ForTurn(SessionNodeModel node, string? modelId)
	{
		var selected = _selector?.SelectEngineId(node, modelId);
		if (selected is null || !_byId.ContainsKey(selected))
			return For(node);

		var engine = _byId[selected];
		var stored = string.Equals(engine.EngineId, Constants.SessionEngines.Claude, StringComparison.OrdinalIgnoreCase) ? null : engine.EngineId;
		var known  = string.IsNullOrEmpty(node.EngineId) || _byId.ContainsKey(node.EngineId);
		if (known && !string.Equals(node.EngineId ?? Constants.SessionEngines.Claude, stored ?? Constants.SessionEngines.Claude, StringComparison.OrdinalIgnoreCase))
		{
			// FR.33.1: the engine is stored when a turn starts, so lists and clients show it without a catalog lookup.
			if (_appSettings is null)
				node.EngineId = stored;
			else
			{
				lock (_appSettings.SyncRoot)
				{
					node.EngineId = stored;
					_appSettings.Save();
				}
			}
		}

		return engine;
	}

	private ISessionEngine Resolve(SessionNodeModel node, string? engineId)
	{
		var id = string.IsNullOrEmpty(engineId) ? Constants.SessionEngines.Claude : engineId;
		if (_byId.TryGetValue(id, out var engine))
			return engine;

		if (_warnedNodes.TryAdd(node.NodeId, 0))
			_log.Warning("Unknown engine '{EngineId:l}' on session {NodeId:l}; using Claude.", engineId, node.NodeId);

		return _byId[Constants.SessionEngines.Claude];
	}

	/// <inheritdoc />
	public string? ApplyEngineChange(SessionNodeModel node, ISessionEngine engine)
	{
		ISessionEngine? previous = null;
		foreach (var other in _engines)
		{
			if (ReferenceEquals(other, engine) || other.GetConversationId(node) is null)
				continue;

			// A live persistent process of the old engine still holds the old conversation (FR.19); it would answer the next background event.
			if (other.IsAlive(node.NodeId))
				other.Stop(node.NodeId);

			if (other.DetachConversation(node))
				previous ??= other;
		}

		if (previous is null)
			return null;

		_log.Information("Session {NodeId:l} changed engine from {Old:l} to {New:l}; its conversation is detached and seeded from the transcript",
			node.NodeId, previous.EngineId, engine.EngineId);
		return string.Format(Constants.SessionEngines.EngineChangedFormat, previous.DisplayName, engine.DisplayName);
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
