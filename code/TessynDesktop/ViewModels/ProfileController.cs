using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Owns the profile selector list, selection, and the profile lifecycle (add/reauthenticate/
/// rename/remove) for a single session (FR.12.7, FR.12.8, FR.12.16, FR.12.17, FR.12.18).
/// Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileController : ReactiveObject
{
	private static readonly ILogger _log = Log.ForContext<ProfileController>();

	private readonly IClaudeProfileService _profileService;
	private readonly IAppSettingsService _appSettings;
	private readonly DirectoryNodeModel? _directoryModel;
	private readonly SessionContentViewModel _content;
	private int _selectedProfileIndex;
	private bool _isProfileAuthInProgress;
	private bool _hasAuthError;
	private string? _defaultProfileDisplayName;

	/// <summary>Entries for the profile selector (Default, stored profiles, "New..."). Rebuilt when profiles change.</summary>
	public ObservableCollection<ProfileListItemModel> AvailableProfiles { get; } = [];

	/// <summary>Selected profile index. 0=Default, 1..N=stored profiles, last="New...". Persisted per directory (FR.12.8).</summary>
	public int SelectedProfileIndex
	{
		get => _selectedProfileIndex;
		set
		{
			// "New..." is always the last item
			if (value == AvailableProfiles.Count - 1)
			{
				_ = HandleNewProfileAsync();
				// Revert selection to previous value (don't persist "New...")
				this.RaisePropertyChanged();
				return;
			}

			this.RaiseAndSetIfChanged(ref _selectedProfileIndex, value);
			if (_directoryModel != null)
				_directoryModel.SelectedProfileIndex = value;
			_appSettings.Settings.SelectedProfileIndex = value;
			_appSettings.Save();
			HasAuthError = false;
		}
	}

	/// <summary>Returns the CLAUDE_CONFIG_DIR path for the selected profile, or null if Default is selected.</summary>
	public string? SelectedProfileConfigDir =>
		_profileService.GetConfigDirForProfile(_selectedProfileIndex, _appSettings.Settings.Profiles);

	/// <summary>Returns the display name of the currently selected profile entry, for recording against sent turns.</summary>
	public string? SelectedProfileDisplayName =>
		_selectedProfileIndex >= 0 && _selectedProfileIndex < AvailableProfiles.Count
			? AvailableProfiles[_selectedProfileIndex].DisplayName
			: null;

	/// <summary>True when the last request failed with an "Invalid authentication credentials" error (FR.12.16). Drives Re-authenticate/Remove button visibility.</summary>
	public bool HasAuthError
	{
		get => _hasAuthError;
		internal set
		{
			this.RaiseAndSetIfChanged(ref _hasAuthError, value);
			this.RaisePropertyChanged(nameof(CanRemoveProfile));
		}
	}

	/// <summary>True when the Remove button should be shown: an auth error is active and a non-Default profile is selected (FR.12.18).</summary>
	public bool CanRemoveProfile => HasAuthError && _selectedProfileIndex > 0;

	public ProfileController(
		IClaudeProfileService profileService,
		IAppSettingsService appSettings,
		DirectoryNodeModel? directoryModel,
		SessionContentViewModel content)
	{
		_profileService = profileService;
		_appSettings    = appSettings;
		_directoryModel = directoryModel;
		_content        = content;

		RebuildProfileList();
		_selectedProfileIndex = Math.Clamp(
			directoryModel?.SelectedProfileIndex ?? appSettings.Settings.SelectedProfileIndex,
			0, Math.Max(0, AvailableProfiles.Count - 2));
	}

	/// <summary>Rebuilds the AvailableProfiles list from appsettings. Always ends with "New...".</summary>
	public void RebuildProfileList()
	{
		AvailableProfiles.Clear();
		AvailableProfiles.Add(new ProfileListItemModel(_defaultProfileDisplayName ?? "Default", null));
		foreach (var p in _appSettings.Settings.Profiles)
			AvailableProfiles.Add(new ProfileListItemModel(p.DisplayName, p.ProfileId));
		AvailableProfiles.Add(new ProfileListItemModel("New...", null));
	}

	/// <summary>
	/// Restores profile selection from a session file's last-recorded profile name, bypassing
	/// directory-model/settings persistence. Pass null when no such entry was found to reset to Default.
	/// </summary>
	public void RestoreFromEntry(string? profileName)
	{
		if (profileName != null)
		{
			var found = false;
			// Skip index 0 (Default) and the last entry ("New...") when searching
			for (var i = 1; i < AvailableProfiles.Count - 1; i++)
			{
				if (string.Equals(AvailableProfiles[i].DisplayName, profileName, StringComparison.OrdinalIgnoreCase))
				{
					_selectedProfileIndex = i;
					found = true;
					break;
				}
			}
			if (!found)
				_selectedProfileIndex = 0; // profile was deleted or renamed → fall back to Default
		}
		else
		{
			_selectedProfileIndex = 0;
		}
		this.RaisePropertyChanged(nameof(SelectedProfileIndex));
	}

	/// <summary>Resolves the default profile email on first load (fire-and-forget).</summary>
	internal void ResolveDefaultProfileEmail()
	{
		_ = ResolveDefaultProfileEmailAsync();
	}

	private async Task ResolveDefaultProfileEmailAsync()
	{
		var email = await _profileService.GetAccountEmailAsync(_appSettings.Settings.ClaudePath, null);
		if (!string.IsNullOrEmpty(email))
		{
			_defaultProfileDisplayName = email;
			Dispatcher.UIThread.Post(() =>
			{
				if (AvailableProfiles.Count > 0)
					AvailableProfiles[0] = new ProfileListItemModel(email, null);
			});
		}
	}

	private async Task HandleNewProfileAsync()
	{
		if (_isProfileAuthInProgress)
			return;

		_isProfileAuthInProgress = true;
		try
		{
			// Generate a unique profile ID — check both settings and filesystem to avoid collisions
			var existingIds = _appSettings.Settings.Profiles.Select(p => p.ProfileId).ToHashSet();
			var profileId = "profile_1";
			for (var i = 2;
			     existingIds.Contains(profileId)
			     || Directory.Exists(Path.Combine(_profileService.ProfilesRootDirectory, profileId));
			     i++)
				profileId = $"profile_{i}";

			// Build the config directory path for this profile
			var configDir = Path.Combine(_profileService.ProfilesRootDirectory, profileId);

			// Launch interactive auth in a visible terminal
			_content.AddSystemMessage("Launching auth login for new profile... Complete authentication in the opened window.");

			await _profileService.LaunchAuthLoginAsync(_appSettings.Settings.ClaudePath, configDir);

			// Verify auth succeeded by querying the profile status
			var email = await _profileService.GetAccountEmailAsync(_appSettings.Settings.ClaudePath, configDir);
			if (string.IsNullOrEmpty(email))
			{
				_content.AddSystemMessage("Profile authentication was cancelled or failed.");
				return;
			}

			var displayName = email;

			// Add profile to settings
			_appSettings.Settings.Profiles.Add(new ClaudeProfileModel
			{
				ProfileId   = profileId,
				DisplayName = displayName,
			});

			// Select the newly added profile
			var newIndex = _appSettings.Settings.Profiles.Count; // 1-based (0 is Default)
			_appSettings.Settings.SelectedProfileIndex = newIndex;
			if (_directoryModel != null)
				_directoryModel.SelectedProfileIndex = newIndex;
			_appSettings.Save();

			Dispatcher.UIThread.Post(() =>
			{
				RebuildProfileList();
				_selectedProfileIndex = newIndex;
				this.RaisePropertyChanged(nameof(SelectedProfileIndex));
			});

			_content.AddSystemMessage($"Profile '{displayName}' added and selected.");
		}
		finally
		{
			_isProfileAuthInProgress = false;
		}
	}

	/// <summary>Re-runs interactive auth login against the currently selected profile (FR.12.17).</summary>
	public async Task HandleReauthenticateAsync()
	{
		if (_isProfileAuthInProgress)
			return;

		_isProfileAuthInProgress = true;
		try
		{
			var configDir = SelectedProfileConfigDir;

			_content.AddSystemMessage("Launching re-authentication... Complete authentication in the opened window.");

			await _profileService.LaunchAuthLoginAsync(_appSettings.Settings.ClaudePath, configDir);

			var email = await _profileService.GetAccountEmailAsync(_appSettings.Settings.ClaudePath, configDir);
			if (string.IsNullOrEmpty(email))
			{
				_content.AddSystemMessage("Re-authentication was cancelled or failed.");
				return;
			}

			// Update the display name if it changed (e.g. different account authenticated)
			if (_selectedProfileIndex > 0 && _selectedProfileIndex - 1 < _appSettings.Settings.Profiles.Count)
			{
				_appSettings.Settings.Profiles[_selectedProfileIndex - 1].DisplayName = email;
				_appSettings.Save();
				Dispatcher.UIThread.Post(RebuildProfileList);
			}
			else if (_selectedProfileIndex == 0)
			{
				_defaultProfileDisplayName = email;
				Dispatcher.UIThread.Post(() =>
				{
					if (AvailableProfiles.Count > 0)
						AvailableProfiles[0] = new ProfileListItemModel(email, null);
				});
			}

			Dispatcher.UIThread.Post(() => HasAuthError = false);

			_content.AddSystemMessage($"Re-authenticated as '{email}'.");
		}
		finally
		{
			_isProfileAuthInProgress = false;
		}
	}

	/// <summary>Removes the currently selected profile from the app's list (FR.12.18). Delegates to <see cref="DeleteProfile"/>.</summary>
	public void HandleRemoveProfile()
	{
		if (_selectedProfileIndex <= 0 || _selectedProfileIndex >= AvailableProfiles.Count - 1)
			return;

		DeleteProfile(AvailableProfiles[_selectedProfileIndex]);
	}

	/// <summary>Renames a stored profile's display name. Context-menu action on the Profile dropdown; no-op for the "Default"/"New..." entries.</summary>
	public void RenameProfile(ProfileListItemModel item, string newName)
	{
		if (item.ProfileId is null)
			return;

		var profile = _appSettings.Settings.Profiles.FirstOrDefault(p => p.ProfileId == item.ProfileId);
		if (profile is null)
			return;

		profile.DisplayName = newName;
		_appSettings.Save();
		RebuildProfileList();
	}

	/// <summary>
	/// Removes a stored profile from the app's list and renames its config directory with a "_rem" suffix (FR.12.18).
	/// Context-menu action on the Profile dropdown; no-op for the "Default"/"New..." entries.
	/// </summary>
	public void DeleteProfile(ProfileListItemModel item)
	{
		if (item.ProfileId is null)
			return;

		var index = _appSettings.Settings.Profiles.FindIndex(p => p.ProfileId == item.ProfileId);
		if (index < 0)
			return;

		var profile = _appSettings.Settings.Profiles[index];
		_appSettings.Settings.Profiles.RemoveAt(index);

		_profileService.MarkProfileDirectoryRemoved(profile.ProfileId);

		// The removed entry sits at AvailableProfiles index (index + 1) (0 is Default).
		var removedListIndex = index + 1;
		if (_selectedProfileIndex == removedListIndex)
		{
			_selectedProfileIndex = 0;
			HasAuthError = false;
		}
		else if (_selectedProfileIndex > removedListIndex)
		{
			_selectedProfileIndex--;
		}

		_appSettings.Settings.SelectedProfileIndex = _selectedProfileIndex;
		if (_directoryModel != null)
			_directoryModel.SelectedProfileIndex = _selectedProfileIndex;
		_appSettings.Save();

		Dispatcher.UIThread.Post(() =>
		{
			RebuildProfileList();
			this.RaisePropertyChanged(nameof(SelectedProfileIndex));
		});

		_content.AddSystemMessage(
			$"Profile '{profile.DisplayName}' removed. Its config directory was preserved as '{profile.ProfileId}{Constants.Auth.RemovedDirectorySuffix}'.");
	}
}
