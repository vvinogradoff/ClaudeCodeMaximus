using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// An <see cref="IClaudeProfileService"/> whose login and account lookup are scripted: no process is started. The config directory of a stored
/// profile is <c>root\id</c> as in the real service.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedProfileService : IClaudeProfileService
{
	private const string DefaultKey = "(default)";

	private readonly Dictionary<string, string?> _emails = new(StringComparer.OrdinalIgnoreCase);

	public ScriptedProfileService(string root)
	{
		ProfilesRootDirectory = root;
	}

	public string ProfilesRootDirectory { get; }

	/// <summary>Every login: the Claude path and the config directory (null = Default).</summary>
	public List<(string ClaudePath, string? ConfigDir)> Logins { get; } = [];

	/// <summary>Every profile id passed to <see cref="MarkProfileDirectoryRemoved"/>.</summary>
	public List<string> Removed { get; } = [];

	/// <summary>Runs inside the login (a test holds it open with a gate, or writes credentials).</summary>
	public Func<string?, Task>? OnLogin { get; set; }

	/// <summary>Sets the account the lookup of <paramref name="configDir"/> (null = Default) answers; null means not logged in.</summary>
	public void SetEmail(string? configDir, string? email) => _emails[configDir ?? DefaultKey] = email;

	public Task<string?> GetAccountEmailAsync(string claudePath, string? configDir) =>
		Task.FromResult(_emails.GetValueOrDefault(configDir ?? DefaultKey));

	public async Task LaunchAuthLoginAsync(string claudePath, string? configDir)
	{
		lock (Logins)
			Logins.Add((claudePath, configDir));
		if (OnLogin != null)
			await OnLogin(configDir);
	}

	public string? GetConfigDirForProfile(int profileIndex, IReadOnlyList<ClaudeProfileModel> profiles) =>
		profileIndex >= 1 && profileIndex <= profiles.Count ? Path.Combine(ProfilesRootDirectory, profiles[profileIndex - 1].ProfileId) : null;

	public void MarkProfileDirectoryRemoved(string profileId) => Removed.Add(profileId);
}
