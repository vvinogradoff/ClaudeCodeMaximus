namespace ClaudeMaximus.Models.Agent;

/// <summary>
/// Specifies the timing for a <see cref="ScheduleWakeArgs"/>. Exactly one field should be set.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record ScheduleWhenArgs(
	/// <summary>Seconds from now to fire (delay schedule).</summary>
	double? InSeconds = null,

	/// <summary>ISO 8601 UTC datetime string at which to fire (at schedule).</summary>
	string? At = null,

	/// <summary>Cron expression (cron schedule, e.g. "0 * * * *" for hourly).</summary>
	string? Cron = null);
