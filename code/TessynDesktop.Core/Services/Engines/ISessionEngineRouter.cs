using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Picks the session engine (FR.33.1). Today only the Claude engine is registered; the rule that derives the engine from the turn's model
/// (FR.33.4) comes with the Codex engine. The per-node operations ask every engine, because a node has at most one live process.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEngineRouter
{
	/// <summary>The registered engines, in registration order.</summary>
	IReadOnlyList<ISessionEngine> Engines { get; }

	/// <summary>The engine with this id; null and <see cref="Constants.SessionEngines.Claude"/> both mean Claude. Throws for an unknown id.</summary>
	ISessionEngine Get(string? engineId);

	/// <summary>The engine the session runs on (<see cref="SessionNodeModel.EngineId"/>).</summary>
	ISessionEngine For(SessionNodeModel node);

	/// <summary>The session has a live persistent process on any engine.</summary>
	bool IsAlive(string nodeId);

	/// <summary>Stops the session's persistent process on every engine.</summary>
	void Stop(string nodeId);

	/// <summary>Kills every process of every engine.</summary>
	void TerminateAll();
}
