using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionStatusService : IClaudeSessionStatusService
{
	private static readonly ILogger _log = Log.ForContext<ClaudeSessionStatusService>();

	private readonly IClaudeProfileService _profileService;

	public ClaudeSessionStatusService(IClaudeProfileService profileService)
	{
		_profileService = profileService;
	}

	public bool IsSessionResumable(string workingDirectory, string claudeSessionId)
	{
		if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(claudeSessionId))
			return false;

		try
		{
			var sessionPath = Constants.ClaudeSessions.FindExistingJsonlPath(workingDirectory, claudeSessionId);
			_log.Debug("Resumability check: {SessionId} wd={WorkingDirectory} path={Path} exists={Exists}",
				claudeSessionId, workingDirectory, sessionPath, sessionPath != null);
			return sessionPath != null;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Error checking session resumability for {SessionId}", claudeSessionId);
			return false;
		}
	}

	public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir)
	{
		if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(claudeSessionId))
			return false;

		try
		{
			var slug     = Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);
			var fileName = claudeSessionId + Constants.ClaudeSessions.SessionFileExtension;

			var activeRoot = ResolveProfileRoot(activeProfileConfigDir);
			var activePath = Path.Combine(activeRoot, Constants.ClaudeSessions.ProjectsFolderName, slug, fileName);
			if (File.Exists(activePath))
				return true;

			foreach (var candidateRoot in EnumerateOtherProfileRoots(activeRoot))
			{
				var candidatePath = Path.Combine(candidateRoot, Constants.ClaudeSessions.ProjectsFolderName, slug, fileName);
				if (!File.Exists(candidatePath))
					continue;

				Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
				File.Move(candidatePath, activePath);
				_log.Information(
					"Adopted Claude session {SessionId} (slug {Slug}) from {Source} into active profile store {Target}",
					claudeSessionId, slug, candidatePath, activePath);
				return true;
			}

			return false;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Error adopting session {SessionId} across profiles", claudeSessionId);
			return false;
		}
	}

	private static string ResolveProfileRoot(string? profileConfigDir)
	{
		if (!string.IsNullOrEmpty(profileConfigDir))
			return profileConfigDir;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return Path.Combine(userProfile, Constants.ClaudeSessions.ClaudeHomeFolderName);
	}

	private IEnumerable<string> EnumerateOtherProfileRoots(string activeRoot)
	{
		var defaultRoot = ResolveProfileRoot(null);
		if (!string.Equals(defaultRoot, activeRoot, StringComparison.OrdinalIgnoreCase))
			yield return defaultRoot;

		var profilesRoot = _profileService.ProfilesRootDirectory;
		if (!Directory.Exists(profilesRoot))
			yield break;

		foreach (var dir in Directory.GetDirectories(profilesRoot))
		{
			if (!string.Equals(dir, activeRoot, StringComparison.OrdinalIgnoreCase))
				yield return dir;
		}
	}
}
