namespace ClaudeMaximus.Services;

/// <summary>
/// Resolves the git remote "origin" URL for a directory path.
/// Used to determine whether sessions can be moved between directories
/// (same origin = same repo, possibly different worktrees).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IGitOriginService
{
	/// <summary>
	/// Returns the remote "origin" URL for the git repository at or above the given path,
	/// or null if the path is not inside a git repository or has no "origin" remote.
	/// Handles both regular repos and git worktrees (where .git is a file pointing to the main repo).
	/// </summary>
	string? GetOriginUrl(string directoryPath);
}
