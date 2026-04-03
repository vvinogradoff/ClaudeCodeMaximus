using System;

namespace TessynDesktop.Models;

/// <summary>
/// A parsed event from the claude --output-format stream-json stdout stream.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeStreamEvent
{
	/// <summary>"assistant", "user", "system", "result"</summary>
	public required string Type { get; init; }

	/// <summary>For "system" events: "init", "compact", etc. For "result": "success" or "error_during_execution".</summary>
	public string? Subtype { get; init; }

	/// <summary>Extracted human-readable content from the event (concatenated text blocks for assistant messages).</summary>
	public string? Content { get; init; }

	/// <summary>Session ID from "result" events; used for --resume on subsequent process launches.</summary>
	public string? SessionId { get; init; }

	public bool IsError { get; init; }

	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
