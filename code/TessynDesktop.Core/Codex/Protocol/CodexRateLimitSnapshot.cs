namespace TessynDesktop.Codex.Protocol;

/// <summary>A rate-limit snapshot (FR.33.10). In an update, a null member does not clear a known value.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexRateLimitSnapshot
{
	public string? LimitId { get; set; }
	public string? LimitName { get; set; }
	public CodexRateLimitWindow? Primary { get; set; }
	public CodexRateLimitWindow? Secondary { get; set; }
	public CodexCreditsSnapshot? Credits { get; set; }
	public string? PlanType { get; set; }
	public string? RateLimitReachedType { get; set; }
	public bool? SpendControlReached { get; set; }
}
