namespace TessynDesktop.Services;

/// <summary>
/// The daemon's cache of directory git origins (P9.1). The desktop keeps its own per-view-model copy; the daemon needs one for the git-origin
/// move rule and for <c>tree.nodeInfo</c>. A path is read on first use and then served from memory until a refresh replaces the cache.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDirectoryGitOrigins
{
	/// <summary>The cached git origin of <paramref name="directoryPath"/>, resolved on first use; null when it is not in a repository or has no origin.</summary>
	string? GetOrigin(string directoryPath);

	/// <summary>
	/// Re-reads the origin of every path in the background and calls <paramref name="completed"/> when the cache holds the new values.
	/// Returns null, doing nothing, while a refresh is still running; otherwise the task that ends when it is done.
	/// </summary>
	Task? StartRefresh(IReadOnlyList<string> directoryPaths, Action completed);
}
