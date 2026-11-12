namespace TessynDesktop.Services;

/// <summary>
/// The tree's structural version: bumped on every structural <c>TreeChanged</c> (not on running-state changes). Shared by the
/// <c>tree.changed</c> notification and <c>tree.snapshot</c>, so a client can tell whether its snapshot is current.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITreeVersionTracker
{
	/// <summary>The current version; starts at 0 on every run.</summary>
	long Current { get; }

	/// <summary>Moves to the next version and returns it. Thread-safe.</summary>
	long Increment();
}
