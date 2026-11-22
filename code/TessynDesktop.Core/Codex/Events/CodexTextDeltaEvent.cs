namespace TessynDesktop.Codex.Events;

/// <summary>Streamed assistant text (<c>item/agentMessage/delta</c>): <c>turn.delta</c> text, live only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexTextDeltaEvent(string ItemId, string Text) : CodexTurnEvent;
