using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TessynDesktop.Services;

/// <summary>
/// The working-directory rule of an elevated run (FR.15.16): the folder is made absolute (a relative path against the caller's working directory), must exist,
/// and its real path (links, junctions and <c>..</c> resolved) must be the caller's own working directory or below it, a registered project directory or below
/// one (the match of FR.15.14), or a sibling worktree <c>&lt;project&gt;-wt-*</c> of a registered project (or a folder below one).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunWorkingDirRule
{
	/// <summary>
	/// Returns the absolute folder to store. Throws <see cref="McpToolException"/> with the tool texts when the folder does not exist or is not allowed.
	/// </summary>
	/// <param name="given">The <c>workingDir</c> argument.</param>
	/// <param name="callerDir">The caller's working directory (also the base of a relative <paramref name="given"/>).</param>
	/// <param name="registered">The registered project directories of the Tessyn tree.</param>
	public string Resolve(string given, string callerDir, IReadOnlyList<string> registered)
	{
		var full = MakeAbsolute(given.Trim(), callerDir);
		if (!Directory.Exists(full))
			throw new McpToolException(string.Format(Constants.ElevatedRuns.DirMissingFormat, full));

		var real = SafeFilePath.CanonicalDirectory(full) ?? full;
		if (IsAllowed(real, callerDir, registered))
			return full;

		throw new McpToolException(string.Format(Constants.ElevatedRuns.DirNotAllowedFormat, full, ListRegistered(registered)));
	}

	private static string MakeAbsolute(string path, string callerDir)
	{
		var absolute = !Path.IsPathRooted(path) && !string.IsNullOrEmpty(callerDir) ? Path.GetFullPath(path, callerDir) : Path.GetFullPath(path);
		return Path.TrimEndingDirectorySeparator(absolute);
	}

	private static bool IsAllowed(string real, string callerDir, IReadOnlyList<string> registered)
	{
		if (!string.IsNullOrEmpty(callerDir) && Forms(callerDir).Any(root => SessionRegistry.IsUnderDirectory(real, root)))
			return true;

		foreach (var project in registered)
		{
			var forms = Forms(project);
			if (forms.Any(root => SessionRegistry.IsUnderDirectory(real, root)) || forms.Any(root => IsInSiblingWorktree(real, root)))
				return true;
		}

		return false;
	}

	/// <summary>A directory as written and as it really is, so a project that lives behind a link still matches the real path of a folder in it.</summary>
	private static IReadOnlyList<string> Forms(string directory)
	{
		var written = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
		var real    = Directory.Exists(written) ? SafeFilePath.CanonicalDirectory(written) : null;
		return real == null || string.Equals(real, written, StringComparison.OrdinalIgnoreCase) ? [written] : [written, Path.TrimEndingDirectorySeparator(real)];
	}

	/// <summary>True when <paramref name="path"/> or an ancestor is a folder next to <paramref name="project"/> named <c>&lt;project name&gt;-wt-&lt;something&gt;</c>.</summary>
	private static bool IsInSiblingWorktree(string path, string project)
	{
		var parent = Path.GetDirectoryName(project);
		var prefix = Path.GetFileName(project) + Constants.ElevatedRuns.WorktreeInfix;
		if (string.IsNullOrEmpty(parent) || prefix.Length == Constants.ElevatedRuns.WorktreeInfix.Length)
			return false;

		for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
		{
			var name = Path.GetFileName(current);
			if (string.Equals(Path.GetDirectoryName(current), parent, StringComparison.OrdinalIgnoreCase)
			    && name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private static string ListRegistered(IReadOnlyList<string> registered)
	{
		if (registered.Count == 0)
			return Constants.ProfileSwitch.NoRegisteredDirectories;

		var listed = string.Join(", ", registered.Take(Constants.ElevatedRuns.MaxListedRegisteredDirs));
		return registered.Count > Constants.ElevatedRuns.MaxListedRegisteredDirs
			? listed + $" (+{registered.Count - Constants.ElevatedRuns.MaxListedRegisteredDirs} more)"
			: listed;
	}
}
