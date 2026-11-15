namespace TessynDesktop.Models;

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
	double CostUsd = 0,

	/// <summary>True when the turn was refused before it ran (e.g. its pinned profile does not exist): nothing was sent to the model (ISSUE.013).</summary>
	bool Refused = false,

	/// <summary>Something the person reading the result should know, e.g. the session ran on Default because its recorded profile no longer exists (ISSUE.013).</summary>
	string? Note = null);
