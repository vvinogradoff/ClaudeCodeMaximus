namespace ClaudeMaximus.Models;

/// <summary>Result of a headless session turn run by <c>ISessionTurnService</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TurnResultModel(
	/// <summary>Concatenated assistant text from the turn, empty if claude produced no text output.</summary>
	string AssistantText,

	/// <summary>The <c>ClaudeSessionId</c> captured from the result event, null if the turn failed before a result.</summary>
	string? SessionId,

	/// <summary>True when the turn ended with an error (claude exited non-zero or emitted an error result).</summary>
	bool IsError = false,

	/// <summary>Error description when <see cref="IsError"/> is true.</summary>
	string? ErrorMessage = null,

	/// <summary>Input token count from the result event (FR.17.2). 0 if not captured.</summary>
	int InputTokens = 0,

	/// <summary>Output token count from the result event (FR.17.2). 0 if not captured.</summary>
	int OutputTokens = 0,

	/// <summary>Total cost in USD from the result event (FR.17.2). 0 if not captured.</summary>
	double CostUsd = 0);
