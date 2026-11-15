using System;
using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// A real settings service on a temp directory, a scripted profile service and the real <see cref="ProfileAdminService"/> with a real event bus.
/// Disposing deletes the temp directory.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ProfileAdminTestEnv : IDisposable
{
	private readonly string _dir;

	public ProfileAdminTestEnv()
	{
		_dir = Path.Combine(Path.GetTempPath(), "profile-admin-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);

		Settings = new AppSettingsService(_dir);
		Settings.Settings.ClaudePath = "claude-test";
		Profiles = new ScriptedProfileService(Path.Combine(_dir, "profiles"));
		Admin    = new ProfileAdminService(Profiles, Settings, Bus);
	}

	public AppSettingsService Settings { get; }
	public ScriptedProfileService Profiles { get; }
	public RemoteEventBus Bus { get; } = new();
	public ProfileAdminService Admin { get; }

	/// <summary>The stored profiles as the settings file holds them: a second service reading the same file.</summary>
	public AppSettingsModel Reloaded()
	{
		var other = new AppSettingsService(_dir);
		other.Load();
		return other.Settings;
	}

	/// <summary>Adds a stored profile; with <paramref name="loggedIn"/> its config directory holds a credentials file.</summary>
	public ClaudeProfileModel AddStored(string id, string name, bool loggedIn = true)
	{
		var profile = new ClaudeProfileModel { ProfileId = id, DisplayName = name };
		Settings.Settings.Profiles.Add(profile);
		if (loggedIn)
		{
			var dir = Path.Combine(Profiles.ProfilesRootDirectory, id);
			Directory.CreateDirectory(dir);
			File.WriteAllText(ClaudeCredentialsPaths.Resolve(dir), "{}");
		}

		return profile;
	}

	public string ConfigDir(string id) => Path.Combine(Profiles.ProfilesRootDirectory, id);

	public void Dispose()
	{
		Bus.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}
}
