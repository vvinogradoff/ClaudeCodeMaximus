namespace TessynDesktop.Models.Remote;

/// <summary>When a schedule fires.</summary>
/// <remarks>Created by Claude</remarks>
public enum ScheduleKindDto
{
	/// <summary>Once, after a delay (stored as a fire time).</summary>
	Delay,

	/// <summary>Once, at a UTC time.</summary>
	At,

	/// <summary>Repeatedly, on a cron expression.</summary>
	Cron,
}
