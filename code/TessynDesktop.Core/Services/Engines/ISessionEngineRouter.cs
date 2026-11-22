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

	/// <summary>The engine the session runs on (<see cref="SessionNodeModel.EngineId"/>). An unknown id falls back to Claude with a Warning, never an error.</summary>
	ISessionEngine For(SessionNodeModel node);

	/// <summary>
	/// The engine a turn on <paramref name="modelId"/> runs on (FR.33.4, the selector's answer, else the stored id). When the selector answers, the
	/// engine is stored in <see cref="SessionNodeModel.EngineId"/> (<c>codex</c>, or null for Claude) and saved.
	/// </summary>
	ISessionEngine ForTurn(SessionNodeModel node, string? modelId);

	/// <summary>The engine a turn on <paramref name="modelId"/> would run on, without storing anything.</summary>
	ISessionEngine Peek(SessionNodeModel node, string? modelId);

	/// <summary>
	/// The first turn on a new engine (FR.33.4): every other engine's conversation of the session is detached into its prior ids. Returns the
	/// SYSTEM text to write (<c>Engine changed: Claude -&gt; Codex (GPT). …</c>), or null when no other engine had a conversation. The new
	/// engine has none, so the turn seeds its conversation from the transcript (FR.11.11).
	/// </summary>
	string? ApplyEngineChange(SessionNodeModel node, ISessionEngine engine);

	/// <summary>The session has a live persistent process on any engine.</summary>
	bool IsAlive(string nodeId);

	/// <summary>Stops the session's persistent process on every engine.</summary>
	void Stop(string nodeId);

	/// <summary>Kills every process of every engine.</summary>
	void TerminateAll();
}
