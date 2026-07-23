namespace ClaudeMaximus.Models;

/// <summary>
/// Token usage and cost information from a completed run.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynRunUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int DurationMs { get; init; }
    public double CostUsd { get; init; }
}
