namespace TessynDesktop.Models;

/// <summary>
/// A session record as returned by the Tessyn daemon's sessions.list and sessions.get RPCs.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynSessionModel
{
    /// <summary>Daemon-internal autoincrement ID (unstable across reindex).</summary>
    public int Id { get; init; }

    /// <summary>Provider name, always "claude" for now.</summary>
    public string Provider { get; init; } = "claude";

    /// <summary>Stable session identifier (UUID from JSONL filename or session_id). Use this for all references.</summary>
    public required string ExternalId { get; init; }

    /// <summary>Project slug derived from working directory path.</summary>
    public string? ProjectSlug { get; init; }

    /// <summary>Auto-generated or user-set title.</summary>
    public string? Title { get; init; }

    /// <summary>First user prompt text (truncated).</summary>
    public string? FirstPrompt { get; init; }

    /// <summary>Unix timestamp (ms) when the session was created.</summary>
    public long CreatedAt { get; init; }

    /// <summary>Unix timestamp (ms) when the session was last updated.</summary>
    public long UpdatedAt { get; init; }

    /// <summary>Number of messages in the session.</summary>
    public int MessageCount { get; init; }

    /// <summary>Session state: "active", "hidden", "archived".</summary>
    public string State { get; init; } = "active";
}
