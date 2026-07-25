using System.IO;
using System.Text.RegularExpressions;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed partial class GitOriginService : IGitOriginService
{
	public string? GetOriginUrl(string directoryPath)
	{
		var gitRoot = FindGitRoot(Path.GetFullPath(directoryPath));
		if (gitRoot is null)
			return null;

		var gitDir = ResolveGitDir(gitRoot);
		if (gitDir is null)
			return null;

		var configPath = Path.Combine(gitDir, "config");
		if (!File.Exists(configPath))
			return null;

		return ParseOriginUrl(configPath);
	}

	/// <summary>
	/// Walks up the directory tree looking for a .git directory or file.
	/// </summary>
	private static string? FindGitRoot(string startPath)
	{
		var current = startPath;
		while (!string.IsNullOrEmpty(current))
		{
			var dotGit = Path.Combine(current, ".git");
			if (Directory.Exists(dotGit) || File.Exists(dotGit))
				return current;

			var parent = Path.GetDirectoryName(current);
			if (parent == current)
				break;
			current = parent;
		}
		return null;
	}

	/// <summary>
	/// Resolves the actual .git directory path.
	/// For regular repos, .git is a directory → returns it directly.
	/// For worktrees, .git is a file containing "gitdir: path/to/main/.git/worktrees/name"
	/// → follows the chain to find the common dir with the config file.
	/// </summary>
	private static string? ResolveGitDir(string gitRoot)
	{
		var dotGit = Path.Combine(gitRoot, ".git");

		if (Directory.Exists(dotGit))
			return dotGit;

		// Worktree: .git is a file with "gitdir: <path>"
		if (!File.Exists(dotGit))
			return null;

		var content = File.ReadAllText(dotGit).Trim();
		if (!content.StartsWith("gitdir:"))
			return null;

		var gitDirPath = content["gitdir:".Length..].Trim();
		if (!Path.IsPathRooted(gitDirPath))
			gitDirPath = Path.GetFullPath(Path.Combine(gitRoot, gitDirPath));

		if (!Directory.Exists(gitDirPath))
			return null;

		// For worktrees, the config is in the common dir (parent of worktrees/).
		// gitDirPath is like: /main/.git/worktrees/<name>
		// The common dir with config is: /main/.git
		var commonDirFile = Path.Combine(gitDirPath, "commondir");
		if (File.Exists(commonDirFile))
		{
			var commonDir = File.ReadAllText(commonDirFile).Trim();
			if (!Path.IsPathRooted(commonDir))
				commonDir = Path.GetFullPath(Path.Combine(gitDirPath, commonDir));
			if (Directory.Exists(commonDir))
				return commonDir;
		}

		return gitDirPath;
	}

	/// <summary>
	/// Parses .git/config to find [remote "origin"] url = ... value.
	/// </summary>
	private static string? ParseOriginUrl(string configPath)
	{
		var lines = File.ReadAllLines(configPath);
		var inOriginRemote = false;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			if (trimmed.StartsWith('['))
			{
				inOriginRemote = OriginSectionPattern().IsMatch(trimmed);
				continue;
			}

			if (inOriginRemote && trimmed.StartsWith("url"))
			{
				var match = UrlValuePattern().Match(trimmed);
				if (match.Success)
					return match.Groups[1].Value;
			}
		}

		return null;
	}

	[GeneratedRegex(@"^\[remote\s+""origin""\]$", RegexOptions.IgnoreCase)]
	private static partial Regex OriginSectionPattern();

	[GeneratedRegex(@"^url\s*=\s*(.+)$")]
	private static partial Regex UrlValuePattern();
}
