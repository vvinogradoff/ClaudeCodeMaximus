namespace TessynDesktop.Codex.Events;

/// <summary>A <c>turn.tool</c> progress line that is not a tool call; nothing goes into the transcript.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexProgressEvent(CodexProgressKind Kind, string Text) : CodexTurnEvent;
