namespace TessynDesktop.Codex.Events;

/// <summary>
/// The end of the turn (<c>turn/completed</c>). <see cref="AssistantText"/> is the worker result: the texts of the turn's agentMessage items in
/// order, separated by a blank line. The tokens are the sums of the turn's <c>last</c> breakdowns, output including reasoning (FR.33.11).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexTurnCompletedEvent(
	string TurnId,
	CodexTurnOutcome Outcome,
	string AssistantText,
	long InputTokens,
	long OutputTokens,
	long? ContextWindow,
	CodexTurnErrorEvent? Error) : CodexTurnEvent;
