namespace ClaudeMaximus.Models;

/// <summary>Result of toggles.get.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynTogglesResult
{
    public bool AutoCommit { get; init; }
    public bool AutoBranch { get; init; }
    public bool AutoDocument { get; init; }
    public bool AutoCompact { get; init; }
}
