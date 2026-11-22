namespace TessynDesktop.Codex.Events;

/// <summary>
/// One ASSISTANT entry: a completed <c>agentMessage</c> with its text, or (<see cref="Interrupted"/>) the text an interrupted message got
/// through its deltas, ending with a line <c>[interrupted]</c> (FR.33.8). <see cref="Phase"/> is Codex's <c>commentary</c> or <c>final_answer</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexAssistantMessageEvent(string ItemId, string Text, string? Phase, bool Interrupted) : CodexTurnEvent;
