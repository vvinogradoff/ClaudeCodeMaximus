namespace ClaudeMaximus.Models;

/// <summary>
/// A single message from a session as returned by the Tessyn daemon's sessions.get RPC.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynMessageModel
{
    /// <summary>Daemon-internal message ID.</summary>
    public int Id { get; init; }

    /// <summary>Message role: "user", "assistant", or "system".</summary>
    public required string Role { get; init; }

    /// <summary>Human-readable message content.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Unix timestamp (ms) when this message was recorded.</summary>
    public long Timestamp { get; init; }
}
