using System;

namespace TessynDesktop.Models;

/// <summary>
/// A parsed event from the claude --output-format stream-json stdout stream.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeStreamEvent
{
	/// <summary>"assistant", "user", "system", "result"; additionally "thinking", "tool_use" and "tool_result" for the activity blocks of a message (P1.I).</summary>
	public required string Type { get; init; }

	/// <summary>For "system" events: "init", "compact", etc. For "result": "success" or "error_during_execution".</summary>
	public string? Subtype { get; init; }

	/// <summary>Extracted human-readable content from the event (concatenated text blocks for assistant messages).</summary>
	public string? Content { get; init; }

	/// <summary>Session ID from "result" events; used for --resume on subsequent process launches.</summary>
	public string? SessionId { get; init; }

	public bool IsError { get; init; }

	/// <summary>Input token count from the result event usage object (FR.17.1). 0 if not present.</summary>
	public int InputTokens { get; init; }

	/// <summary>Output token count from the result event usage object (FR.17.1). 0 if not present.</summary>
	public int OutputTokens { get; init; }

	/// <summary>Total cost in USD from the result event (FR.17.1). 0 if not present.</summary>
	public double CostUsd { get; init; }

	/// <summary>For tool_use and tool_result events (P1.I): the id that pairs a call with its result.</summary>
	public string? ToolUseId { get; init; }

	/// <summary>For tool_use events: the tool's name.</summary>
	public string? ToolName { get; init; }

	/// <summary>For tool_use and tool_result events: a one-line, truncated summary (the command, file or pattern; the result's first line).</summary>
	public string? ToolSummary { get; init; }

	/// <summary>For tool_result events: the tool reported an error. Kept apart from <see cref="IsError"/>, which marks a failed turn.</summary>
	public bool ToolIsError { get; init; }

	/// <summary>
	/// For assistant events: the API error class the CLI stamps on a synthetic error message (<c>"rate_limit"</c>, <c>"authentication_failed"</c>, <c>"server_error"</c>, ...);
	/// null for a normal message. This is the structured signal of a limits error.
	/// </summary>
	public string? ApiError { get; init; }

	/// <summary>For assistant events: the CLI's <c>quotaLimits.status</c> says the request was rejected (a usage or rate limit).</summary>
	public bool QuotaRejected { get; init; }

	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
