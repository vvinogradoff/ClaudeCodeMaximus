namespace TessynDesktop.Models;

/// <summary>Result of reindex RPC.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynReindexResult
{
    public int Indexed { get; init; }
    public int Total { get; init; }
}
