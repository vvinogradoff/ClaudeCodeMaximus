using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionEngineRouter" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionEngineRegistry : ISessionEngineRouter
{
	private readonly IReadOnlyList<ISessionEngine> _engines;
	private readonly Dictionary<string, ISessionEngine> _byId;

	public SessionEngineRegistry(IEnumerable<ISessionEngine> engines)
	{
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

	/// <inheritdoc />
	public ISessionEngine For(SessionNodeModel node) => Get(node.EngineId);

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
