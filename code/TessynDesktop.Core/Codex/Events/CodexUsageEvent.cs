namespace TessynDesktop.Codex.Events;

/// <summary>
/// <c>turn.usage</c> for one model call (FR.33.11): input = <c>last.inputTokens</c>, output = <c>last.outputTokens + last.reasoningOutputTokens</c>,
/// context window = <c>modelContextWindow</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexUsageEvent(long InputTokens, long OutputTokens, long? ContextWindow) : CodexTurnEvent;
