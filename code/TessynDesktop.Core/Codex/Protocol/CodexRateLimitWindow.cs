namespace TessynDesktop.Codex.Protocol;

/// <summary>One plan window (FR.33.10). <c>resetsAt</c> is Unix seconds; the duration is read, never assumed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexRateLimitWindow
{
	public double UsedPercent { get; set; }
	public int? WindowDurationMins { get; set; }
	public long? ResetsAt { get; set; }
}
