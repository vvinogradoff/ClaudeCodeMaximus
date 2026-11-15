using System;
using System.IO;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IProfileResolver" />
/// <remarks>Created by Claude</remarks>
public sealed class ProfileResolver : IProfileResolver
{
	private readonly IAppSettingsService _appSettings;
	private readonly IClaudeProfileService _profileService;

	public ProfileResolver(IAppSettingsService appSettings, IClaudeProfileService profileService)
	{
		_appSettings    = appSettings;
		_profileService = profileService;
	}

	/// <inheritdoc />
	public ProfileResolution Resolve(SessionNodeModel node, string? recordedName, string? perTurnName = null, bool checkLogin = false)
	{
		var pinnedByName = !string.IsNullOrEmpty(node.ProfileName);

		if (!string.IsNullOrEmpty(perTurnName))
			return FindByName(perTurnName, checkLogin) ?? Missing(node, perTurnName, pinned: false);

		if (!string.IsNullOrEmpty(node.ProfileId))
			return FindById(node.ProfileId, checkLogin) ?? Missing(node, node.ProfileName ?? node.ProfileId, pinned: true);

		if (string.IsNullOrEmpty(recordedName))
			return Default(pinned: false);

		var found = FindByName(recordedName, checkLogin);
		if (found != null)
			return found with { IsPinned = pinnedByName };

		// A name only taken from the file history (not pinned on the node) that matches nothing stays on Default, as before.
		return pinnedByName ? Missing(node, recordedName, pinned: true) : Default(pinned: false);
	}

	/// <inheritdoc />
	public ProfileResolution? FindById(string profileId, bool checkLogin = false)
	{
		if (string.Equals(profileId, Constants.ProfileSwitch.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
			return Default(pinned: true);

		var (profile, index) = Find(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
		return profile == null ? null : Stored(profile, index, checkLogin);
	}

	/// <inheritdoc />
	public ProfileResolution? FindByName(string displayName, bool checkLogin = false)
	{
		if (displayName.StartsWith(Constants.Remote.Profiles.SystemPrefix, StringComparison.Ordinal))
			return Default(pinned: true);

		var (profile, index) = Find(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
		return profile == null ? null : Stored(profile, index, checkLogin);
	}

	/// <inheritdoc />
	public ProfileResolution? DescribePin(SessionNodeModel node)
	{
		if (!string.IsNullOrEmpty(node.ProfileId))
			return FindById(node.ProfileId) ?? Missing(node, node.ProfileName ?? node.ProfileId, pinned: true);

		if (string.IsNullOrEmpty(node.ProfileName))
			return null;

		return FindByName(node.ProfileName) ?? Missing(node, node.ProfileName, pinned: true);
	}

	private (ClaudeProfileModel? Profile, int Index) Find(Func<ClaudeProfileModel, bool> match)
	{
		lock (_appSettings.SyncRoot)
		{
			var profiles = _appSettings.Settings.Profiles;
			for (var i = 0; i < profiles.Count; i++)
			{
				if (match(profiles[i]))
					return (profiles[i], i);
			}
		}

		return (null, -1);
	}

	private ProfileResolution Stored(ClaudeProfileModel profile, int index, bool checkLogin)
	{
		string? configDir;
		lock (_appSettings.SyncRoot)
			configDir = _profileService.GetConfigDirForProfile(index + 1, _appSettings.Settings.Profiles);

		var loggedOut = checkLogin && configDir != null && !File.Exists(ClaudeCredentialsPaths.Resolve(configDir));
		return new ProfileResolution(profile.ProfileId, profile.DisplayName, configDir, IsPinned: true, Error: null, IsLoggedOut: loggedOut);
	}

	private static ProfileResolution Default(bool pinned) =>
		new(Constants.ProfileSwitch.DefaultProfileId, Constants.ProfileSwitch.DefaultProfileName, null, pinned);

	private static ProfileResolution Missing(SessionNodeModel node, string profileName, bool pinned) =>
		new(node.ProfileId ?? profileName, profileName, null, pinned,
			Error: string.Format(Constants.ProfileSwitch.PinnedProfileMissingFormat, node.Name, profileName));
}
