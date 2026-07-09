namespace ClaudeMaximus.Models;

/// <summary>Identifies what triggered a session turn.</summary>
/// <remarks>Created by Claude</remarks>
public enum TurnSource
{
	/// <summary>Turn was submitted by the user through the UI.</summary>
	User,

	/// <summary>Turn was triggered by the scheduler (FR.14).</summary>
	Scheduled,

	/// <summary>Turn was triggered by a supervisor session via orchestration tools (FR.15).</summary>
	Orchestrated,
}
