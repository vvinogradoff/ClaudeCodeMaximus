using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>account/rateLimits/read</c>. The bucket used is <c>rateLimitsByLimitId["codex"]</c>, else <c>rateLimits</c> (FR.33.10).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexRateLimitsResult
{
	public CodexRateLimitSnapshot RateLimits { get; set; } = null!;
	public Dictionary<string, CodexRateLimitSnapshot>? RateLimitsByLimitId { get; set; }
	public bool? OrdinaryUsageAllowed { get; set; }
	public string? AccountId { get; set; }
}
