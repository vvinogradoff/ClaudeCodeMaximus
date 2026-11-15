using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IClaudeSessionMigrator" />
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionMigrator : IClaudeSessionMigrator
{
	private static readonly ILogger _log = Log.ForContext<ClaudeSessionMigrator>();

	private readonly IClaudeProfileService _profileService;
	private readonly string _defaultClaudeHome;

	/// <param name="profileService">Names the profiles root whose sub folders are the other stores.</param>
	/// <param name="defaultClaudeHome">The Default profile's config dir; null = <c>~/.claude</c> (tests pass a temp dir).</param>
	public ClaudeSessionMigrator(IClaudeProfileService profileService, string? defaultClaudeHome = null)
	{
		_profileService    = profileService;
		_defaultClaudeHome = defaultClaudeHome ?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.ClaudeSessions.ClaudeHomeFolderName);
	}

	/// <inheritdoc />
	public SessionMigrationReport Migrate(string workingDirectory, string claudeSessionId, string? targetConfigDir)
	{
		if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(claudeSessionId))
			return SessionMigrationReport.NotFound;

		try
		{
			var slug       = Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);
			var targetRoot = string.IsNullOrEmpty(targetConfigDir) ? _defaultClaudeHome : targetConfigDir;
			var targetSlug = Path.Combine(targetRoot, Constants.ClaudeSessions.ProjectsFolderName, slug);
			var fileName   = claudeSessionId + Constants.ClaudeSessions.SessionFileExtension;
			var targetFile = Path.Combine(targetSlug, fileName);
			var targetDir  = Path.Combine(targetSlug, claudeSessionId);

			var transcriptMoved = false;
			var folderFiles     = 0;
			var oldRoots        = new List<string>();
			var oldPaths        = new List<string>();

			foreach (var sourceRoot in EnumerateOtherRoots(targetRoot))
			{
				var sourceSlug = Path.Combine(sourceRoot, Constants.ClaudeSessions.ProjectsFolderName, slug);
				var sourceFile = Path.Combine(sourceSlug, fileName);
				var sourceDir  = Path.Combine(sourceSlug, claudeSessionId);

				var movedHere = false;
				if (File.Exists(sourceFile) && !File.Exists(targetFile))
				{
					Directory.CreateDirectory(targetSlug);
					File.Move(sourceFile, targetFile);
					transcriptMoved = true;
					movedHere       = true;
				}
				else if (File.Exists(sourceFile))
					_log.Warning("Session {SessionId} has a transcript in both {Source} and {Target}; the target's is kept, the source copy is left in place", claudeSessionId, sourceFile, targetFile);

				if (Directory.Exists(sourceDir))
				{
					var moved = MergeDirectory(sourceDir, targetDir);
					folderFiles += moved;
					movedHere   |= moved > 0;
				}

				if (movedHere)
					oldRoots.Add(sourceRoot);
			}

			if (transcriptMoved && oldRoots.Count > 0)
				oldPaths.AddRange(ScanForOldPaths(targetFile, oldRoots));

			var found = File.Exists(targetFile);
			if (transcriptMoved || folderFiles > 0)
			{
				_log.Information(Constants.ProfileSwitch.SessionMovedLog,
					claudeSessionId, slug, string.Join(";", oldRoots), targetRoot, transcriptMoved, folderFiles, oldPaths.Count);
				foreach (var path in oldPaths)
					_log.Warning("Moved transcript of {SessionId} still names a path under the old config dir: {Path}", claudeSessionId, path);
			}

			return new SessionMigrationReport(found, transcriptMoved, folderFiles, oldPaths);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Error moving session {SessionId} between profile stores", claudeSessionId);
			return SessionMigrationReport.NotFound;
		}
	}

	private IEnumerable<string> EnumerateOtherRoots(string targetRoot)
	{
		if (!SamePath(_defaultClaudeHome, targetRoot))
			yield return _defaultClaudeHome;

		var profilesRoot = _profileService.ProfilesRootDirectory;
		if (!Directory.Exists(profilesRoot))
			yield break;

		foreach (var dir in Directory.GetDirectories(profilesRoot))
		{
			if (!SamePath(dir, targetRoot))
				yield return dir;
		}
	}

	private static bool SamePath(string a, string b) =>
		string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

	/// <summary>Moves every file of <paramref name="source"/> into <paramref name="target"/>, keeping existing target files; removes emptied source folders. Returns the files moved.</summary>
	private static int MergeDirectory(string source, string target)
	{
		var moved = 0;
		Directory.CreateDirectory(target);

		foreach (var file in Directory.GetFiles(source))
		{
			var destination = Path.Combine(target, Path.GetFileName(file));
			if (File.Exists(destination))
				continue;

			File.Move(file, destination);
			moved++;
		}

		foreach (var sub in Directory.GetDirectories(source))
			moved += MergeDirectory(sub, Path.Combine(target, Path.GetFileName(sub)));

		if (!Directory.EnumerateFileSystemEntries(source).Any())
			Directory.Delete(source);

		return moved;
	}

	/// <summary>Distinct absolute paths under any of <paramref name="oldRoots"/> named in the transcript (plain, JSON-escaped or with forward slashes).</summary>
	private static IEnumerable<string> ScanForOldPaths(string transcript, IReadOnlyList<string> oldRoots)
	{
		var alternatives = oldRoots
			.Select(Path.TrimEndingDirectorySeparator)
			.SelectMany(root => new[] { root, root.Replace("\\", "\\\\"), root.Replace('\\', '/') })
			.Distinct()
			.Select(Regex.Escape);
		var pattern = new Regex("(?:" + string.Join("|", alternatives) + @")[^""\s]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var line in File.ReadLines(transcript))
		{
			foreach (Match match in pattern.Matches(line))
			{
				found.Add(match.Value.Replace("\\\\", "\\").TrimEnd('\\', ',', '.'));
				if (found.Count >= Constants.ProfileSwitch.MaxReportedPaths)
					return found;
			}
		}

		return found;
	}
}
