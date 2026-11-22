namespace TessynDesktop.Services;

/// <summary>The retry flags of one Claude turn (formerly on <c>TurnRun</c> and in <c>SessionTurnService</c>): set by the event translation, consumed by the engine.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ClaudeTurnState
{
	/// <summary>"No conversation found": the engine retries once with the history as context. Reset when the retry is launched.</summary>
	public bool NeedsContextRetry { get; set; }

	/// <summary>ISSUE.010: the transient OAuth refresh race; the engine retries once, silently. Reset when the retry is launched.</summary>
	public bool NeedsAuthRaceRetry { get; set; }
}
