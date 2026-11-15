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
	private readonly IProfileAdminService _profileAdmin;
	private readonly IAppSettingsService _appSettings;
	private readonly DirectoryNodeModel? _directoryModel;
	private readonly SessionContentViewModel _content;
	private int _selectedProfileIndex;
	private bool _isProfileAuthInProgress;
	private bool _hasAuthError;
	private bool _isRepublishing;
	private string? _defaultProfileDisplayName;

	/// <summary>Entries for the profile selector (Default, stored profiles, "New..."). Rebuilt when profiles change.</summary>
	public ObservableCollection<ProfileListItemModel> AvailableProfiles { get; } = [];

	/// <summary>Selected profile index. 0=Default, 1..N=stored profiles, last="New...". Persisted per directory (FR.12.8).</summary>
	public int SelectedProfileIndex
	{
		get => _isRepublishing ? -1 : _selectedProfileIndex;
		set
		{
			// The ComboBox pushes -1 when its items are replaced/cleared; never treat that as a user choice
			if (value < 0)
				return;

			// "New..." is always the last item
			if (value == AvailableProfiles.Count - 1)
			{
				_ = HandleNewProfileAsync();
				// Revert selection to previous value (don't persist "New...")
				this.RaisePropertyChanged();
				return;
			}

			this.RaiseAndSetIfChanged(ref _selectedProfileIndex, value);
			this.RaisePropertyChanged(nameof(CanManageSelectedProfile));
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

	/// <summary>
	/// True when a stored (non-Default, non-"New...") profile is currently selected. Drives visibility
	/// of the command-bar Rename/Delete buttons (FR.12.19) — independent of auth-error state, unlike
	/// <see cref="CanRemoveProfile"/>.
	/// </summary>
	public bool CanManageSelectedProfile => _selectedProfileIndex > 0;

	public ProfileController(
		IClaudeProfileService profileService,
		IProfileAdminService profileAdmin,
		IAppSettingsService appSettings,
		DirectoryNodeModel? directoryModel,
		SessionContentViewModel content)
	{
		_profileService = profileService;
		_profileAdmin   = profileAdmin;
		_appSettings    = appSettings;
		_directoryModel = directoryModel;
		_content        = content;

		RebuildProfileList();
		_selectedProfileIndex = Math.Clamp(
			directoryModel?.SelectedProfileIndex ?? appSettings.Settings.SelectedProfileIndex,
			0, Math.Max(0, AvailableProfiles.Count - 2));
	}

	/// <summary>
	/// Re-publishes the selected index to the view. Clearing the list or replacing the selected item
	/// drops the ComboBox selection to -1, and a plain change notification carrying the unchanged
	/// index is ignored by the binding — so publish -1 first, then the real index, forcing a re-select.
	/// </summary>
	private void RepublishSelection()
	{
		_isRepublishing = true;
		this.RaisePropertyChanged(nameof(SelectedProfileIndex));
		_isRepublishing = false;
		this.RaisePropertyChanged(nameof(SelectedProfileIndex));
	}

	/// <summary>Builds the "Default" entry — the base, non-isolated claude auth identity, prefixed to distinguish it from stored profiles.</summary>
	private ProfileListItemModel BuildDefaultEntry() =>
		new($"[Sys] {_defaultProfileDisplayName ?? "Default"}", null, isSystem: true);

	/// <summary>The index of the stored profile with this id in the selector (never the Default or "New..." entry); -1 when none.</summary>
	private int IndexOfProfileId(string profileId)
	{
		for (var i = 1; i < AvailableProfiles.Count - 1; i++)
		{
			if (string.Equals(AvailableProfiles[i].ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
				return i;
		}

		return -1;
	}

	/// <summary>Rebuilds the AvailableProfiles list from appsettings. Always ends with "New...".</summary>
	public void RebuildProfileList()
	{
		AvailableProfiles.Clear();
		AvailableProfiles.Add(BuildDefaultEntry());
		foreach (var p in _appSettings.Settings.Profiles)
			AvailableProfiles.Add(new ProfileListItemModel(p.DisplayName, p.ProfileId));
		AvailableProfiles.Add(new ProfileListItemModel("New...", null));
		RepublishSelection();
	}

	/// <summary>
	/// Restores profile selection from a session file's last-recorded profile name, bypassing
	/// directory-model/settings persistence. Pass null when no such entry was found to reset to Default.
	/// </summary>
	public void RestoreFromEntry(string? profileName, string? pinnedProfileId = null)
	{
		// ISSUE.013 P6: the session's ProfileId pin wins over the name its last user entry recorded, so a profile switch made
		// elsewhere (profile.switch, a cascade, migrate) is not undone by the next send of a stale dropdown value.
		if (!string.IsNullOrEmpty(pinnedProfileId))
		{
			var index = string.Equals(pinnedProfileId, Constants.ProfileSwitch.DefaultProfileId, StringComparison.OrdinalIgnoreCase)
				? 0
				: IndexOfProfileId(pinnedProfileId);
			if (index >= 0)
			{
				_selectedProfileIndex = index;
				RepublishSelection();
				this.RaisePropertyChanged(nameof(CanManageSelectedProfile));
				return;
			}
		}

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
		RepublishSelection();
		this.RaisePropertyChanged(nameof(CanManageSelectedProfile));
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
					AvailableProfiles[0] = BuildDefaultEntry();
				RepublishSelection();
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
			// Launch interactive auth in a visible terminal
			_content.AddSystemMessage("Launching auth login for new profile... Complete authentication in the opened window.");

			// The service picks a free profile id, runs the login, checks the account, adds the profile named after it and selects it
			// (globally and on this session's directory) in one save.
			var profile = await _profileAdmin.AddAfterLoginAsync(_directoryModel);
			if (profile is null)
			{
				_content.AddSystemMessage("Profile authentication was cancelled or failed.");
				return;
			}

			// Select the newly added profile
			var newIndex = _appSettings.Settings.Profiles.Count; // 1-based (0 is Default)

			Dispatcher.UIThread.Post(() =>
			{
				RebuildProfileList();
				_selectedProfileIndex = newIndex;
				RepublishSelection();
				this.RaisePropertyChanged(nameof(CanManageSelectedProfile));
			});

			_content.AddSystemMessage($"Profile '{profile.DisplayName}' added and selected.");
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
			_content.AddSystemMessage("Launching re-authentication... Complete authentication in the opened window.");

			// The profile the selector shows: a stored one, or the Default (index 0, and an index that points at no stored profile).
			var storedIndex = _selectedProfileIndex - 1;
			var profileId = _selectedProfileIndex > 0 && storedIndex < _appSettings.Settings.Profiles.Count
				? _appSettings.Settings.Profiles[storedIndex].ProfileId
				: Constants.ProfileSwitch.DefaultProfileId;

			var login = await _profileAdmin.AuthenticateAsync(profileId);
			if (!login.Ran)
				return;

			var email = login.Email;
			if (string.IsNullOrEmpty(email))
			{
				_content.AddSystemMessage("Re-authentication was cancelled or failed.");
				return;
			}

			// The service renamed a stored profile after the account that logged in (e.g. a different account authenticated)
			if (_selectedProfileIndex > 0 && _selectedProfileIndex - 1 < _appSettings.Settings.Profiles.Count)
			{
				Dispatcher.UIThread.Post(RebuildProfileList);
			}
			else if (_selectedProfileIndex == 0)
			{
				_defaultProfileDisplayName = email;
				Dispatcher.UIThread.Post(() =>
				{
					if (AvailableProfiles.Count > 0)
						AvailableProfiles[0] = BuildDefaultEntry();
					RepublishSelection();
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

		if (_profileAdmin.Rename(item.ProfileId, newName) is null)
			return;

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

		// The service removes the entry, renames its config directory, fixes the selection up (Default when the removed one was selected,
		// one less when a later one was) and writes it globally and on this session's directory in one save.
		var removal = _profileAdmin.Remove(item.ProfileId, _selectedProfileIndex, _directoryModel);
		if (removal is null)
			return;

		var profile = removal.Profile;
		_selectedProfileIndex = removal.SelectedIndex;
		if (removal.WasSelected)
			HasAuthError = false;

		Dispatcher.UIThread.Post(() =>
		{
			RebuildProfileList();
			RepublishSelection();
			this.RaisePropertyChanged(nameof(CanManageSelectedProfile));
		});

		_content.AddSystemMessage(
			$"Profile '{profile.DisplayName}' removed. Its config directory was preserved as '{profile.ProfileId}{Constants.Auth.RemovedDirectorySuffix}'.");
	}
}
