using System.IO;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DirectoryLabelService : IDirectoryLabelService
{
	/// <summary>
	/// Returns the display label for a directory path.
	/// Walks up the directory tree to find the nearest .git root.
	/// - If .git is at the given path itself: returns just the directory name.
	/// - If .git is in an ancestor: returns "gitRootName\relative\path\to\dir".
	/// - If no .git found anywhere: returns the full absolute path.
	/// </summary>
	public string GetLabel(string directoryPath)
	{
		var normalised = Path.GetFullPath(directoryPath);
		var gitRoot = FindGitRoot(normalised);

		if (gitRoot == null)
			return normalised;

		if (string.Equals(gitRoot, normalised, System.StringComparison.OrdinalIgnoreCase))
			return Path.GetFileName(normalised);

		var gitRootName = Path.GetFileName(gitRoot);
		var relative = Path.GetRelativePath(gitRoot, normalised);
		return Path.Combine(gitRootName, relative);
	}

	private static string? FindGitRoot(string startPath)
	{
		var current = startPath;
		while (!string.IsNullOrEmpty(current))
		{
			if (Directory.Exists(Path.Combine(current, ".git")))
				return current;

			var parent = Path.GetDirectoryName(current);
			if (parent == current)
				break;
			current = parent;
		}
		return null;
	}
}
