namespace TessynDesktop.Codex.Events;

/// <summary>Streamed reasoning summary (<c>item/reasoning/summaryTextDelta</c>): <c>turn.delta</c> thinking, live only, never written to the transcript.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexThinkingDeltaEvent(string ItemId, string Text) : CodexTurnEvent;
