namespace ClaudeMaximus.Models;

/// <summary>Durable session metadata that survives reindex.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynSessionMeta
{
    public string? Title { get; init; }
    public bool? Hidden { get; init; }
    public bool? Archived { get; init; }
    public bool? AutoCommit { get; init; }
    public bool? AutoBranch { get; init; }
    public bool? AutoDocument { get; init; }
    public bool? AutoCompact { get; init; }
    public string? Draft { get; init; }
    public string? ModelOverride { get; init; }
    public string? CustomInstructions { get; init; }
}
