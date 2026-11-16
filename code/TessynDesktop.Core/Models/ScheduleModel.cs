using System;

namespace TessynDesktop.Models;

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

	/// <summary>
	/// True for a Mailbox Delivery (FR.15.3, FR.15.12): the delay-0 schedule that hands async worker results to
	/// their supervisor. Such a schedule is merged into while pending and held while its target is busy.
	/// </summary>
	public bool IsMailboxDelivery { get; set; }

	/// <summary>The profile the user picked when the send was scheduled (A14, FR.12.27): <c>"default"</c> or a stored profile id; null = none picked, the session's own resolution applies. Absent in schedules stored before this field existed.</summary>
	public string? ProfileId { get; set; }

	/// <summary>The model picked when the send was scheduled (<c>"default"</c> = no --model flag); null = none picked.</summary>
	public string? ModelId { get; set; }

	/// <summary>The effort picked when the send was scheduled (<c>"default"</c> = no --effort flag); null = none picked.</summary>
	public string? Effort { get; set; }
}
