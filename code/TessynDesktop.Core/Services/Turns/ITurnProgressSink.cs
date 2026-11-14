using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Receives the progress of every turn the daemon runs (the interactive engine and the headless turn service). Called on the
/// thread that produced the progress, possibly many times a second: an implementation hands the item over and returns, and never
/// throws into the turn.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITurnProgressSink
{
	void Publish(TurnProgress progress);
}
