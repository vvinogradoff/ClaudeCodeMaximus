using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// A typed run event deserialized from Tessyn daemon run.* notifications.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynRunEvent
{
    /// <summary>
    /// Event type: "started", "system", "delta", "block_start", "block_stop",
    /// "message", "completed", "failed", "cancelled", "rate_limit", "auth_required".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Unique identifier for this run, returned by run.send.</summary>
    public required string RunId { get; init; }

    /// <summary>Stable session identifier. Present on "system" and "completed" events.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Model used for this run. Present on "system" events.</summary>
    public string? Model { get; init; }

    /// <summary>Available tools. Present on "system" events.</summary>
    public List<string>? Tools { get; init; }

    /// <summary>Content block type: "text", "thinking", "tool_use", "tool_result". Present on "delta", "block_start".</summary>
    public string? BlockType { get; init; }

    /// <summary>Incremental text content. Present on "delta" events.</summary>
    public string? Delta { get; init; }

    /// <summary>Content block index within the message. Present on "delta", "block_start", "block_stop".</summary>
    public int? BlockIndex { get; init; }

    /// <summary>Tool name for tool_use blocks. Present on "block_start" when BlockType is "tool_use".</summary>
    public string? ToolName { get; init; }

    /// <summary>Tool input as JSON string. Present on "block_start" when BlockType is "tool_use".</summary>
    public string? ToolInput { get; init; }

    /// <summary>Tool output text. Present on "block_stop" for tool_use blocks (daemon v0.4.3+).</summary>
    public string? ToolResult { get; init; }

    /// <summary>Whether the tool call failed. Present on "block_stop" for tool_use blocks.</summary>
    public bool? IsError { get; init; }

    /// <summary>Raw content blocks from run.message events. Used to extract tool inputs.</summary>
    public string? RawContent { get; init; }

    /// <summary>Message role. Present on "message" events.</summary>
    public string? Role { get; init; }

    /// <summary>Stop reason. Present on "completed" events.</summary>
    public string? StopReason { get; init; }

    /// <summary>Error message. Present on "failed" events.</summary>
    public string? Error { get; init; }

    /// <summary>Retry delay in milliseconds. Present on "rate_limit" events.</summary>
    public int? RetryAfterMs { get; init; }

    /// <summary>Token usage stats. Present on "completed" events.</summary>
    public TessynRunUsage? Usage { get; init; }
}
