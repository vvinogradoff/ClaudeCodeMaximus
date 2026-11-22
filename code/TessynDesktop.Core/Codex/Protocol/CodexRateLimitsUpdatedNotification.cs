namespace TessynDesktop.Codex.Protocol;

/// <summary><c>account/rateLimits/updated</c>: a sparse update, sent after every model response (FR.33.10).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexRateLimitsUpdatedNotification
{
	public CodexRateLimitSnapshot RateLimits { get; set; } = null!;
}
