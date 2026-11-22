namespace TessynDesktop.Codex.Protocol;

/// <summary>One token breakdown of <c>thread/tokenUsage/updated</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTokenUsageBreakdown
{
	public long TotalTokens { get; set; }
	public long InputTokens { get; set; }
	public long CachedInputTokens { get; set; }
	public long OutputTokens { get; set; }
	public long ReasoningOutputTokens { get; set; }
}
