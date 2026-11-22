using System;
using System.Linq;
using System.IO;
using Serilog;
using TessynDesktop.Codex;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IProfileResolver" />
/// <remarks>Created by Claude</remarks>
public sealed class ProfileResolver : IProfileResolver
{
	private static readonly ILogger _log = Log.ForContext<ProfileResolver>();

	private readonly IAppSettingsService _appSettings;
	private readonly IClaudeProfileService _profileService;

	// FR.33.5: a ChatGPT pin or name never stands in for a Claude one. For the Claude engine a session pinned to a ChatGPT profile has no pin
	// (an engine change gives it the inherit rule), and a ChatGPT name is no recorded profile.
	private readonly CodexProfileNaming _codex = new();

	public ProfileResolver(IAppSettingsService appSettings, IClaudeProfileService profileService)
	{
		_appSettings    = appSettings;
		_profileService = profileService;
	}

	/// <inheritdoc />
	public ProfileResolution Resolve(SessionNodeModel node, string? recordedName, string? perTurnName = null, bool checkLogin = false)
	{
		var codexPinned  = _codex.IsCodexProfile(node.ProfileId) || _codex.IsChatGptName(node.ProfileName);
		var pinnedByName = !string.IsNullOrEmpty(node.ProfileName) && !codexPinned;

		if (!string.IsNullOrEmpty(perTurnName))
		{
			if (_codex.IsCodexProfile(perTurnName))
				return new ProfileResolution(perTurnName, perTurnName, null, false,
					Error: string.Format(Constants.Codex.Profiles.ChatGptProfileForClaudeFormat, perTurnName, ListClaudeNames()));
			return FindByName(perTurnName, checkLogin) ?? Missing(node, perTurnName, pinned: false);
		}

		if (!string.IsNullOrEmpty(node.ProfileId) && !codexPinned)
			return FindById(node.ProfileId, checkLogin) ?? Missing(node, node.ProfileName ?? node.ProfileId, pinned: true);

		if (string.IsNullOrEmpty(recordedName) || _codex.IsChatGptName(recordedName) || (codexPinned && recordedName == node.ProfileName))
			return Default(pinned: false);

		var found = FindByName(recordedName, checkLogin);
		if (found != null)
			return found with { IsPinned = pinnedByName };

		// A legacy name-only record (no ProfileId pin) that matches no stored profile runs on Default as before, but not silently:
		// it is logged and the turn result carries a note (ISSUE.013, supervisor decision). Only an id pin or an explicit choice is strict.
		_log.Warning("Session {NodeId} ({Name}) records profile '{ProfileName}', which matches no stored profile; the turn runs on the Default profile", node.NodeId, node.Name, recordedName);
		return Default(pinned: false) with { Note = string.Format(Constants.ProfileSwitch.LegacyProfileMissingNoteFormat, recordedName) };
	}

	/// <inheritdoc />
	public ProfileResolution? FindById(string profileId, bool checkLogin = false)
	{
		if (_codex.IsCodexProfileId(profileId))
			return null;

		if (string.Equals(profileId, Constants.ProfileSwitch.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
			return Default(pinned: true);

		var (profile, index) = Find(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
		return profile == null ? null : Stored(profile, index, checkLogin);
	}

	/// <inheritdoc />
	public ProfileResolution? FindByName(string displayName, bool checkLogin = false)
	{
		if (_codex.IsChatGptName(displayName))
			return null;

		if (displayName.StartsWith(Constants.Remote.Profiles.SystemPrefix, StringComparison.Ordinal))
			return Default(pinned: true);

		var (profile, index) = Find(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
		return profile == null ? null : Stored(profile, index, checkLogin);
	}

	/// <inheritdoc />
	public ProfileResolution? DescribePin(SessionNodeModel node)
	{
		if (_codex.IsCodexProfile(node.ProfileId) || _codex.IsChatGptName(node.ProfileName))
			return null;

		if (!string.IsNullOrEmpty(node.ProfileId))
			return FindById(node.ProfileId) ?? Missing(node, node.ProfileName ?? node.ProfileId, pinned: true);

		if (string.IsNullOrEmpty(node.ProfileName))
			return null;

		return FindByName(node.ProfileName) ?? Missing(node, node.ProfileName, pinned: true);
	}

	/// <summary><c>default</c> and the stored Claude profiles' names, for the cross-engine error.</summary>
	private string ListClaudeNames()
	{
		lock (_appSettings.SyncRoot)
			return string.Join(", ", new[] { Constants.ProfileSwitch.DefaultProfileId }.Concat(_appSettings.Settings.Profiles.Select(p => p.DisplayName)));
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
