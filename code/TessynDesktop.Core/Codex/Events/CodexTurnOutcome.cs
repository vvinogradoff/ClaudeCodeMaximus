namespace TessynDesktop.Codex.Events;

/// <summary>How a Codex turn ended.</summary>
/// <remarks>Created by Claude</remarks>
public enum CodexTurnOutcome
{
	/// <summary><c>completed</c>: the turn ends successfully.</summary>
	Completed,

	/// <summary><c>interrupted</c>: the turn ends as cancelled (FR.33.8).</summary>
	Interrupted,

	/// <summary><c>failed</c>: the turn ends with an error.</summary>
	Failed,
}
