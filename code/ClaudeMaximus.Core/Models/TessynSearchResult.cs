namespace ClaudeMaximus.Models;

/// <summary>
/// A single search result from the Tessyn daemon's search RPC (FTS5).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynSearchResult
{
    public int SessionId { get; init; }
    public int MessageId { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public long Timestamp { get; init; }
    public string? SessionTitle { get; init; }
    public string? ProjectSlug { get; init; }
    public double Rank { get; init; }
}
