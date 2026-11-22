namespace TessynDesktop.Codex.Protocol;

/// <summary>The token usage of a thread: the running total and the last model call (FR.33.11).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadTokenUsage
{
	public CodexTokenUsageBreakdown Total { get; set; } = null!;
	public CodexTokenUsageBreakdown Last { get; set; } = null!;
	public long? ModelContextWindow { get; set; }
}
