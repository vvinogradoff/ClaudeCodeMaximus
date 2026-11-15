namespace TessynDesktop.Models.Remote;

/// <summary>What a schedule does when its time passed while the daemon was not running.</summary>
/// <remarks>Created by Claude</remarks>
public enum MissedFireDto
{
	/// <summary>Fire once at the next start.</summary>
	FireOnce,

	/// <summary>Skip the missed fires and wait for the next time.</summary>
	Skip,
}
