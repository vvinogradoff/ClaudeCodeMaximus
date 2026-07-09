namespace ClaudeMaximus.Models;

/// <summary>Determines when a <see cref="ScheduleModel"/> fires.</summary>
/// <remarks>Created by Claude</remarks>
public enum ScheduleKind
{
	/// <summary>Fire once after a delay (stored as <see cref="ScheduleModel.FireAtUtc"/>).</summary>
	Delay,

	/// <summary>Fire once at a specific UTC time (stored as <see cref="ScheduleModel.FireAtUtc"/>).</summary>
	At,

	/// <summary>Fire repeatedly on a cron expression (stored as <see cref="ScheduleModel.CronExpression"/>).</summary>
	Cron,
}
