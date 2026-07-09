using System;

namespace ClaudeMaximus.Models;

/// <summary>
/// A persisted schedule entry. Stored in <c>AppSettingsModel.Schedules</c>.
/// The scheduler re-arms timers from these on startup.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleModel
{
	/// <summary>Unique identifier for this schedule (used by <c>cancel_schedule</c>).</summary>
	public required string ScheduleId { get; init; }

	/// <summary>NodeId of the target session (FR.14.2). Never null — uses the caller's NodeId for self-wake.</summary>
	public required string TargetNodeId { get; init; }

	/// <summary>Prompt to send when the schedule fires.</summary>
	public string Prompt { get; set; } = string.Empty;

	/// <summary>Human-readable note shown in <c>list_schedules</c> and the session SYSTEM message.</summary>
	public string Note { get; set; } = string.Empty;

	public ScheduleKind Kind { get; init; }

	/// <summary>UTC time to fire (used by <see cref="ScheduleKind.Delay"/> and <see cref="ScheduleKind.At"/>).</summary>
	public DateTimeOffset? FireAtUtc { get; set; }

	/// <summary>Cron expression (used by <see cref="ScheduleKind.Cron"/>).</summary>
	public string? CronExpression { get; set; }

	public MissedFirePolicy MissedFirePolicy { get; set; } = MissedFirePolicy.FireOnce;

	/// <summary>Total number of times this schedule has fired.</summary>
	public int FireCount { get; set; }

	/// <summary>Maximum number of times a cron schedule fires before auto-cancellation (0 = unlimited).</summary>
	public int MaxFires { get; set; }
}
