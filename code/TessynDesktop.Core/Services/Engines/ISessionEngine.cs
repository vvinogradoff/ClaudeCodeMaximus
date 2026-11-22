using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// A session engine (FR.33.1): the one seam through which the daemon runs a session's turns on a CLI. The turn lock, the USER entry, the
/// instruction block, the observers and <c>turn.*</c> progress stay with the callers (<c>TurnEngine</c>, <c>SessionTurnService</c>); the
/// engine owns the process, the protocol, its conversation id and the retries that are quirks of its CLI. It reports through
/// <see cref="IEngineEventSink"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEngine
{
	/// <summary>The engine's id (<see cref="Constants.SessionEngines"/>), as stored in <see cref="SessionNodeModel.EngineId"/>.</summary>
	string EngineId { get; }

	EngineCapabilities Capabilities { get; }

	/// <summary>The id of the session's conversation on this engine; null when there is none yet (a new or detached conversation).</summary>
	string? GetConversationId(SessionNodeModel node);

	/// <summary>Forgets the session's conversation (the next turn starts a new one, seeded from the transcript) and saves the settings.</summary>
	void ClearConversation(SessionNodeModel node);

	/// <summary>
	/// Makes the conversation resumable under the profile of the turn (Claude: the cross-profile adoption of FR.11.13). False when it can't be
	/// resumed there. Called under the turn lock.
	/// </summary>
	bool AdoptConversation(SessionNodeModel node, string conversationId, string? profileConfigDir);

	/// <summary>What the turn needs before it waits for the turn lock (Claude: the MCP config file and the Local Tool Profile, FR.12.21, FR.14.3).</summary>
	Task<EngineTurnPreparation?> PrepareTurnAsync(SessionNodeModel node, TurnSettings settings);

	/// <summary>
	/// Runs one turn and reports it to <paramref name="sink"/>; returns when the turn (with the engine's own retries) has ended. Cancelling
	/// <paramref name="cancellationToken"/> stops the turn and throws <see cref="System.OperationCanceledException"/>.
	/// </summary>
	Task RunTurnAsync(EngineTurnRequest request, IEngineEventSink sink, CancellationToken cancellationToken);

	/// <summary>
	/// Sends a headless turn into the session's live persistent process (FR.19.13) instead of starting a competing one. The persistent process's
	/// own sink still writes the transcript; <paramref name="sink"/> only observes the turn's events.
	/// </summary>
	Task RunTurnInLiveProcessAsync(SessionNodeModel node, string prompt, IEngineEventSink sink, CancellationToken cancellationToken);

	/// <summary>Sends a mid-run correction (fire and forget). Only the conversation id it returns is kept; nothing is written or shown.</summary>
	void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt);

	/// <summary>
	/// Interrupts the session's running turn in-band where the engine supports it (<see cref="EngineCapabilities.InBandInterrupt"/>); otherwise
	/// stops the session's persistent process. True when something was running.
	/// </summary>
	Task<bool> InterruptAsync(string nodeId);

	/// <summary>The session has a live persistent process on this engine.</summary>
	bool IsAlive(string nodeId);

	/// <summary>Stops the session's persistent process, if any.</summary>
	void Stop(string nodeId);

	/// <summary>Kills every process of this engine ("Terminate all", daemon exit).</summary>
	void TerminateAll();
}
