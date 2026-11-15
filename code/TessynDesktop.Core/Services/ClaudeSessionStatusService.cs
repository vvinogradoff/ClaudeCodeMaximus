using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;
using TessynDesktop.Models;

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

	public IReadOnlyList<string> GetAllProjectsDirs() => Constants.ClaudeSessions.GetAllProjectsDirs();

	public bool IsSessionResumable(string workingDirectory, string claudeSessionId, IReadOnlyList<string>? projectsDirs = null)
	{
		if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(claudeSessionId))
			return false;

		try
		{
			return Constants.ClaudeSessions.FindExistingJsonlPath(workingDirectory, claudeSessionId, projectsDirs) != null;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Error checking session resumability for {SessionId}", claudeSessionId);
			return false;
		}
	}

	public IReadOnlyList<bool> AreSessionsResumable(IReadOnlyList<SessionResumabilityRequest> requests, IReadOnlyList<string>? projectsDirs = null)
	{
		var results = new bool[requests.Count];

		// File.Exists is case-insensitive on Windows only; the name sets must compare the same way.
		var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

		// Slugs that some request actually asks about, each with the file names found for it.
		var namesBySlug = new Dictionary<string, HashSet<string>>(comparer);
		foreach (var request in requests)
		{
			if (!string.IsNullOrEmpty(request.WorkingDirectory) && !string.IsNullOrEmpty(request.ClaudeSessionId))
				namesBySlug.TryAdd(Constants.ClaudeSessions.BuildProjectSlug(request.WorkingDirectory), new HashSet<string>(comparer));
		}

		if (namesBySlug.Count == 0)
			return results;

		// One listing per projects dir: its slug directories, then only the wanted ones are listed for files.
		foreach (var projectsDir in projectsDirs ?? GetAllProjectsDirs())
		{
			try
			{
				if (!Directory.Exists(projectsDir))
					continue;

				foreach (var slugDir in Directory.EnumerateDirectories(projectsDir))
				{
					if (!namesBySlug.TryGetValue(Path.GetFileName(slugDir), out var names))
						continue;

					foreach (var file in Directory.EnumerateFiles(slugDir))
						names.Add(Path.GetFileName(file));
				}
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Error listing Claude projects directory {ProjectsDir} for resumability", projectsDir);
			}
		}

		for (var i = 0; i < requests.Count; i++)
		{
			var request = requests[i];
			if (string.IsNullOrEmpty(request.WorkingDirectory) || string.IsNullOrEmpty(request.ClaudeSessionId))
				continue;

			var slug = Constants.ClaudeSessions.BuildProjectSlug(request.WorkingDirectory);
			results[i] = namesBySlug[slug].Contains(request.ClaudeSessionId + Constants.ClaudeSessions.SessionFileExtension);
		}

		return results;
	}

	/// <summary>
	/// FR.11.13 / ISSUE.013 R3: the transcript and its sibling session folder are moved into the active profile's store by the shared
	/// <see cref="ClaudeSessionMigrator"/>; true when the session is (now) there.
	/// </summary>
	public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir) =>
		new ClaudeSessionMigrator(_profileService).Migrate(workingDirectory, claudeSessionId, activeProfileConfigDir, skipWhenTargetExists: true).Found;
}
