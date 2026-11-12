namespace TessynDesktop.Services;

/// <summary>
/// Per session file, how many times the file was replaced as a whole during this run (rewrite, write from entries, compaction
/// repair). A client that holds entries of epoch N must reload when the epoch is higher. In memory only: every run starts at 0.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEpochTracker
{
	/// <summary>The epoch of <paramref name="fileName"/>; 0 until it is first rewritten.</summary>
	int GetEpoch(string fileName);
}
