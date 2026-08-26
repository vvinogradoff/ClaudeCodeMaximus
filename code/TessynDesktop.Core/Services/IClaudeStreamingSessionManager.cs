using System;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Owns persistent, per-session <c>claude</c> processes for Streaming Session Mode (FR.19): one
/// process spawned with <c>--input-format stream-json</c> is kept alive across a session's turns
/// instead of being respawned per turn (contrast <see cref="IClaudeProcessManager"/>, which owns
/// One-shot Mode). At most one process is alive per <c>NodeId</c> at a time (FR.19.3).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeStreamingSessionManager
{
	/// <summary>
	/// Ensures a streaming process is running for <paramref name="nodeId"/>, spawning one with
	/// <paramref name="spawnParams"/> if none is currently alive. <paramref name="onEvent"/> is
	/// (re)bound as the event callback for the process's lifetime — every call updates it, so a
	/// caller whose instance changed (e.g. a new <c>TurnController</c> after a session reload)
	/// still receives events from an already-running process. Callers are expected to hold the
	/// per-node turn lock before calling this, so concurrent spawns for the same node are not
	/// expected. Returns <c>true</c> if a new process was spawned, <c>false</c> if one was already alive.
	/// </summary>
	bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent);

	/// <summary>
	/// Writes one turn to the open stdin of the streaming process for <paramref name="nodeId"/> and
	/// waits for the next <c>result</c> event to arrive as the turn boundary. Throws
	/// <see cref="InvalidOperationException"/> if no process is alive for this node — callers must
	/// call <see cref="EnsureStarted"/> first. An unprompted <c>result</c> event (e.g. from a
	/// background-task notification firing between turns) arriving before this turn's own result
	/// is a known race in this v1 implementation; it is not otherwise guarded against.
	/// </summary>
	/// <param name="onTurnEvent">
	/// Optional, additive observer scoped to just this turn — every event delivered to it is also
	/// delivered to the process's primary <c>onEvent</c> callback (bound via <see cref="EnsureStarted"/>)
	/// unchanged; this does not rebind or replace it. Lets a second caller (e.g. a headless turn
	/// routed here because a process happens to already be alive — see FR.19.13) observe the turn's
	/// outcome (content, tokens, errors) to build its own return value, without disturbing whichever
	/// UI is already bound as the primary callback and already owns file/UI writes for this process.
	/// </param>
	Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null);

	/// <summary>True when a streaming process is currently alive for this node.</summary>
	bool IsAlive(string nodeId);

	/// <summary>Kills the streaming process for this node, if one is alive (FR.19.5). No-op otherwise.</summary>
	void Stop(string nodeId);

	/// <summary>Kills every currently-alive streaming process. Called on app shutdown.</summary>
	void TerminateAll();

	/// <summary>
	/// Raised when a streaming process for the given <c>nodeId</c> has exited, whether because
	/// <see cref="Stop"/> killed it or it exited/crashed on its own. Lets a subscribed
	/// <c>TurnController</c> update <c>CanStop</c> and unlock the Model/Profile/Effort selectors
	/// (FR.19.8) even when the process died without an explicit Stop.
	/// </summary>
	event Action<string>? ProcessExited;
}
