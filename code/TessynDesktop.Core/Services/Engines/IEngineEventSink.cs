using System;
using TessynDesktop.Models.Engines;

namespace TessynDesktop.Services;

/// <summary>
/// The neutral events of one turn (FR.33.1, FR.33.11). A session engine turns its own output (Claude stream-json, Codex notifications) into these
/// calls; the turn pipelines write the transcript, tell the observers and publish <c>turn.*</c> progress from them, without knowing the engine.
/// Calls arrive on the engine's reader thread, in the order the engine produced them.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IEngineEventSink
{
	/// <summary>
	/// The engine's own state for the turn this sink stands for (for Claude: its retry flags). The sink only keeps it, so it lives exactly as long
	/// as the turn: a persistent process's sink hands out the state of the turn now holding the lock, and a background sink has none.
	/// </summary>
	object? EngineTurnState { get; set; }

	/// <summary>An assistant message: one ASSISTANT entry, and a <c>turn.delta</c> text.</summary>
	void AssistantText(EngineAssistantText message);

	/// <summary>Thinking text: a <c>turn.delta</c> thinking, never written to the transcript.</summary>
	void Thinking(string text);

	/// <summary>A tool call started (<c>turn.tool</c> started). <paramref name="name"/> may be null when the engine did not name it.</summary>
	void ToolStarted(string toolId, string? name, string? summary);

	/// <summary>A tool call finished (<c>turn.tool</c> result), paired with its start by <paramref name="toolId"/>.</summary>
	void ToolFinished(string toolId, string? summary, bool isError);

	/// <summary>A progress line of a background task or plan (shown live, not written).</summary>
	void Progress(string text, DateTimeOffset timestamp);

	/// <summary>The engine compacted its context: a COMPACTION separator (FR.3.4).</summary>
	void Compacted(DateTimeOffset timestamp);

	/// <summary>An error outside the turn's result (see <see cref="EngineError"/>).</summary>
	void Error(EngineError error);

	/// <summary>A transient failure was seen that the engine will retry; an error text recorded for this attempt no longer applies.</summary>
	void RetryPending(EngineRetryKind kind);

	/// <summary>The engine is about to retry (status Retrying). For <see cref="EngineRetryKind.LostConversation"/> the conversation is reseeded from the transcript.</summary>
	void Retrying(EngineRetryKind kind);

	/// <summary>The retry is running (status Running again).</summary>
	void RetryRunning();

	/// <summary>A persistent process was started for the session by this turn (FR.19).</summary>
	void PersistentProcessStarted();

	/// <summary>
	/// The token usage of the turn so far, while it runs (Codex reports it after every model call, FR.33.11): a <c>turn.usage</c> with the running
	/// totals, which a client shows in place of the last one. The final usage still comes with <see cref="TurnEnded"/>. Engines that only know it
	/// at the end (Claude) never call it.
	/// </summary>
	void Usage(int inputTokens, int outputTokens) { }

	/// <summary>The result of the turn (or of one attempt of it).</summary>
	void TurnEnded(EngineTurnEnd end);
}
