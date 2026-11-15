using System;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Receives the progress of one engine turn so that a client can render it (the Avalonia <c>TurnController</c> adapter).
/// Every member has an empty default, so a caller that only needs the outcome passes no observer at all. Members are called on
/// the thread that produced the progress: <see cref="UserEntryAppended"/> and <see cref="Interrupted"/> on the caller's
/// continuation, the stream members on the claude reader thread. Implementations marshal to their own thread and never throw.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITurnObserver
{
	/// <summary>The user's message was appended to the transcript. Called synchronously, before the engine first yields to its caller.</summary>
	void UserEntryAppended(DateTimeOffset timestamp, string? profileName) { }

	/// <summary>An assistant entry was appended. <paramref name="modelId"/> and <paramref name="effort"/> are set on the turn's first assistant entry only.</summary>
	void AssistantEntryAppended(string text, DateTimeOffset timestamp, string? modelId, string? effort) { }

	/// <summary>A compaction separator was appended.</summary>
	void CompactionAppended(DateTimeOffset timestamp) { }

	/// <summary>Claude reported task progress (<c>[tool] description</c>); shown in place of the "thinking" row. Nothing is persisted.</summary>
	void ProgressText(string text, DateTimeOffset timestamp) { }

	/// <summary>A system error line was appended to the transcript.</summary>
	void SystemError(string text, DateTimeOffset timestamp) { }

	/// <summary>The turn's error result was appended to the transcript; progress rows are to be dropped.</summary>
	void ResultError(string text, DateTimeOffset timestamp) { }

	/// <summary>A result event ended the response without an error line; progress rows are to be dropped.</summary>
	void ResultEnded() { }

	/// <summary>The session's Claude session id changed (stored from a successful result).</summary>
	void SessionIdCaptured() { }

	/// <summary>The profile's authentication failed (<c>true</c>) or a result succeeded again (<c>false</c>).</summary>
	void AuthErrorChanged(bool hasAuthError) { }

	/// <summary>A Streaming Session Mode process was started for this turn.</summary>
	void StreamingProcessStarted() { }

	/// <summary>The turn is being retried with the conversation history ("No conversation found").</summary>
	void ContextRetrying() { }

	/// <summary>The turn was stopped by the user; the interruption line was appended to the transcript.</summary>
	void Interrupted(DateTimeOffset timestamp) { }

	/// <summary>
	/// The node's last send has ended. Called before the running flag is released; a client finishes its bookkeeping here (the JSONL entry
	/// count) while its busy state still holds. Skipped for the idle transition when a new send started meanwhile.
	/// </summary>
	Task BeforeIdleAsync() => Task.CompletedTask;

	/// <summary>The node is idle now (called right before the running flag is released): clear the busy state, stop the timer, play the sound.</summary>
	void Idle() { }

	/// <summary>
	/// Called after the response, still holding the node's turn lock, so a client can run a post-response step of its own (Auto-Compact
	/// is the engine's, see <see cref="ShouldAutoCompact"/>). Runs before it. Cancellation of the turn interrupts it like the rest of the turn.
	/// </summary>
	Task AfterResponseWhileLockedAsync() => Task.CompletedTask;

	/// <summary>
	/// Whether the engine compacts after this turn (Auto-Compact, FR.11.6). <paramref name="requested"/> is the flag the send carried; a client
	/// whose toggle can still change while the turn runs (the Avalonia composer) answers with the live state instead. Asked once, after the response.
	/// </summary>
	bool ShouldAutoCompact(bool requested) => requested;

	/// <summary>A compaction is about to run (Auto-Compact): show progress.</summary>
	void CompactionStarted() { }

	/// <summary>The compaction ended, with whatever outcome; its system line and the detach are in <paramref name="result"/>. The session file is already rewritten on success.</summary>
	void CompactionFinished(SessionCompactionResultModel result) { }
}
