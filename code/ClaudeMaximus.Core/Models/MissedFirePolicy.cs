namespace ClaudeMaximus.Models;

/// <summary>
/// Controls what happens to a scheduled turn when the application was closed at the fire time.
/// </summary>
/// <remarks>Created by Claude</remarks>
public enum MissedFirePolicy
{
	/// <summary>Fire once on next app launch regardless of how many fires were missed.</summary>
	FireOnce,

	/// <summary>Skip all missed fires; wait for the next scheduled time.</summary>
	Skip,
}
