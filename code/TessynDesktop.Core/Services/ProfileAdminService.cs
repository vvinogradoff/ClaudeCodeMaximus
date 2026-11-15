using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IProfileAdminService" />
/// <remarks>
/// The bodies are the ones of the Avalonia <c>ProfileController</c> (id choice, login, e-mail check, rename, removal with directory marking and
/// selection fix-up), moved here and run under the settings lock. Created by Claude.
/// </remarks>
public sealed class ProfileAdminService : IProfileAdminService
{
	private static readonly ILogger _log = Log.ForContext<ProfileAdminService>();

	private readonly IClaudeProfileService _profileService;
	private readonly IAppSettingsService _appSettings;
	private readonly IRemoteEventBus? _bus;
	private readonly ISessionAuthErrorTracker? _authErrors;
	private readonly object _gate = new();
	private readonly HashSet<string> _reservedIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _authRunning = new(StringComparer.OrdinalIgnoreCase);

	public ProfileAdminService(IClaudeProfileService profileService, IAppSettingsService appSettings, IRemoteEventBus? bus = null, ISessionAuthErrorTracker? authErrors = null)
	{
		_authErrors     = authErrors;
		_profileService = profileService;
		_appSettings    = appSettings;
		_bus            = bus;
	}

	/// <inheritdoc />
	public event Action? ProfilesChanged;

	/// <inheritdoc />
	public event Action<string, bool>? AuthenticationCompleted;

	/// <inheritdoc />
	public IReadOnlyList<ClaudeProfileModel> GetProfiles()
	{
		lock (_appSettings.SyncRoot)
			return [.. _appSettings.Settings.Profiles.Select(Copy)];
	}

	/// <inheritdoc />
	public int GetSelectedProfileIndex()
	{
		lock (_appSettings.SyncRoot)
			return _appSettings.Settings.SelectedProfileIndex;
	}

	/// <inheritdoc />
	public ProfileAuthState? GetAuthState(string profileId)
	{
		if (IsDefault(profileId))
			return ProfileAuthState.Unknown;

		string? configDir;
		lock (_appSettings.SyncRoot)
		{
			var index = IndexOf(profileId);
			if (index < 0)
				return null;

			configDir = _profileService.GetConfigDirForProfile(index + 1, _appSettings.Settings.Profiles);
		}

		return configDir != null && File.Exists(ClaudeCredentialsPaths.Resolve(configDir)) ? ProfileAuthState.LoggedIn : ProfileAuthState.LoggedOut;
	}

	/// <inheritdoc />
	public ClaudeProfileModel Add(string displayName)
	{
		ClaudeProfileModel profile;
		lock (_appSettings.SyncRoot)
		{
			profile = new ClaudeProfileModel { ProfileId = NextProfileIdLocked(), DisplayName = displayName };
			_appSettings.Settings.Profiles.Add(profile);
			_appSettings.Save();
		}

		RaiseProfilesChanged();
		return Copy(profile);
	}

	/// <inheritdoc />
	public async Task<ClaudeProfileModel?> AddAfterLoginAsync(DirectoryNodeModel? directoryToSelect)
	{
		string profileId;
		string claudePath;
		lock (_appSettings.SyncRoot)
		{
			profileId  = NextProfileIdLocked();
			claudePath = _appSettings.Settings.ClaudePath;
		}

		// The id is held until the profile is in the list: the config directory only appears once the login has written to it.
		lock (_gate)
			_reservedIds.Add(profileId);

		try
		{
			var configDir = Path.Combine(_profileService.ProfilesRootDirectory, profileId);
			await _profileService.LaunchAuthLoginAsync(claudePath, configDir);

			// Verify auth succeeded by querying the profile status
			var email = await _profileService.GetAccountEmailAsync(claudePath, configDir);
			if (string.IsNullOrEmpty(email))
				return null;

			var profile = new ClaudeProfileModel { ProfileId = profileId, DisplayName = email };
			lock (_appSettings.SyncRoot)
			{
				_appSettings.Settings.Profiles.Add(profile);

				// Select the newly added profile
				var newIndex = _appSettings.Settings.Profiles.Count; // 1-based (0 is Default)
				_appSettings.Settings.SelectedProfileIndex = newIndex;
				if (directoryToSelect != null)
					directoryToSelect.SelectedProfileIndex = newIndex;
				_appSettings.Save();
			}

			RaiseProfilesChanged();
			return Copy(profile);
		}
		finally
		{
			lock (_gate)
				_reservedIds.Remove(profileId);
		}
	}

	/// <inheritdoc />
	public ClaudeProfileModel? Rename(string profileId, string displayName)
	{
		ClaudeProfileModel copy;
		lock (_appSettings.SyncRoot)
		{
			var profile = _appSettings.Settings.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
			if (profile is null)
				return null;

			profile.DisplayName = displayName;
			_appSettings.Save();
			copy = Copy(profile);
		}

		RaiseProfilesChanged();
		return copy;
	}

	/// <inheritdoc />
	public ProfileRemoval? Remove(string profileId) => Remove(profileId, GetSelectedProfileIndex(), directoryToSelect: null);

	/// <inheritdoc />
	public ProfileRemoval? Remove(string profileId, int selectedIndex, DirectoryNodeModel? directoryToSelect)
	{
		ProfileRemoval removal;
		lock (_appSettings.SyncRoot)
		{
			var index = _appSettings.Settings.Profiles.FindIndex(p => p.ProfileId == profileId);
			if (index < 0)
				return null;

			var profile = _appSettings.Settings.Profiles[index];
			var pinned = CountPinnedSessions(profile.ProfileId);
			_appSettings.Settings.Profiles.RemoveAt(index);

			_profileService.MarkProfileDirectoryRemoved(profile.ProfileId);

			// The removed entry sits at selector index (index + 1) (0 is Default).
			var removedListIndex = index + 1;
			var wasSelected = selectedIndex == removedListIndex;
			var newSelected = selectedIndex;
			if (wasSelected)
				newSelected = 0;
			else if (selectedIndex > removedListIndex)
				newSelected--;

			_appSettings.Settings.SelectedProfileIndex = newSelected;
			if (directoryToSelect != null)
				directoryToSelect.SelectedProfileIndex = newSelected;
			_appSettings.Save();

			removal = new ProfileRemoval(Copy(profile), removedListIndex, newSelected, wasSelected, pinned);
		}

		RaiseProfilesChanged();
		return removal;
	}

	/// <inheritdoc />
	public async Task<ProfileAuthentication> AuthenticateAsync(string profileId)
	{
		RequireProfile(profileId);
		if (!TryReserveLogin(profileId))
			return new ProfileAuthentication(Ran: false, Email: null);

		try
		{
			return await RunLoginAsync(profileId);
		}
		finally
		{
			ReleaseLogin(profileId);
		}
	}

	/// <inheritdoc />
	public bool TryStartAuthentication(string profileId)
	{
		RequireProfile(profileId);
		if (!TryReserveLogin(profileId))
			return false;

		_ = Task.Run(async () =>
		{
			try
			{
				await RunLoginAsync(profileId);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Authenticating profile {ProfileId} failed", profileId);
				Complete(profileId, success: false);
			}
			finally
			{
				ReleaseLogin(profileId);
			}
		});

		return true;
	}

	private async Task<ProfileAuthentication> RunLoginAsync(string profileId)
	{
		string claudePath;
		string? configDir = null;
		var removed = false;
		lock (_appSettings.SyncRoot)
		{
			claudePath = _appSettings.Settings.ClaudePath;
			if (!IsDefault(profileId))
			{
				// A profile removed since the request was accepted must not fall through to the Default account (index 0 means Default).
				var index = IndexOf(profileId);
				removed = index < 0;
				configDir = removed ? null : _profileService.GetConfigDirForProfile(index + 1, _appSettings.Settings.Profiles);
			}
		}

		if (removed)
		{
			Complete(profileId, success: false);
			return new ProfileAuthentication(Ran: false, Email: null);
		}

		await _profileService.LaunchAuthLoginAsync(claudePath, configDir);

		var email = await _profileService.GetAccountEmailAsync(claudePath, configDir);
		if (string.IsNullOrEmpty(email))
		{
			Complete(profileId, success: false);
			return new ProfileAuthentication(Ran: true, Email: null);
		}

		// Update the display name if it changed (e.g. different account authenticated)
		if (!IsDefault(profileId))
		{
			lock (_appSettings.SyncRoot)
			{
				var profile = _appSettings.Settings.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
				if (profile != null)
				{
					profile.DisplayName = email;
					_appSettings.Save();
				}
			}
		}

		Complete(profileId, success: true);
		return new ProfileAuthentication(Ran: true, Email: email);
	}

	private void Complete(string profileId, bool success)
	{
		// A successful login ends the sessions' auth error (V5): the flag is per session, not per profile, so every session's flag is cleared;
		// one that still fails sets it again with its next turn.
		if (success)
			_authErrors?.ClearAll();

		RaiseProfilesChanged();
		try
		{
			AuthenticationCompleted?.Invoke(profileId, success);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "An AuthenticationCompleted subscriber threw");
		}
	}

	private void RaiseProfilesChanged()
	{
		_bus?.Publish(RemoteProtocol.ProfilesAdmin.ChangedEvent, new ProfilesChangedPayload());
		try
		{
			ProfilesChanged?.Invoke();
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "A ProfilesChanged subscriber threw");
		}
	}

	private void RequireProfile(string profileId)
	{
		if (IsDefault(profileId))
			return;

		lock (_appSettings.SyncRoot)
		{
			if (IndexOf(profileId) < 0)
				throw new ProfileAdminException($"There is no profile '{profileId}'.");
		}
	}

	private bool TryReserveLogin(string profileId)
	{
		lock (_gate)
			return _authRunning.Add(profileId);
	}

	private void ReleaseLogin(string profileId)
	{
		lock (_gate)
			_authRunning.Remove(profileId);
	}

	/// <summary>The id and check the Avalonia controller used: a free <c>profile_N</c> in both the settings and the profiles folder. The caller holds the settings lock.</summary>
	private string NextProfileIdLocked()
	{
		var existingIds = _appSettings.Settings.Profiles.Select(p => p.ProfileId).ToHashSet();
		lock (_gate)
		{
			var profileId = Constants.ProfileAdmin.IdPrefix + "1";
			for (var i = 2;
			     existingIds.Contains(profileId)
			     || _reservedIds.Contains(profileId)
			     || Directory.Exists(Path.Combine(_profileService.ProfilesRootDirectory, profileId));
			     i++)
				profileId = Constants.ProfileAdmin.IdPrefix + i;

			return profileId;
		}
	}

	/// <summary>Sessions pinned by id to the profile (ISSUE.013 rule A18: after the removal their turns fail with a "profile missing" error, never fall back to Default).</summary>
	private int CountPinnedSessions(string profileId)
	{
		static int Count(IEnumerable<SessionNodeModel> sessions, IEnumerable<GroupNodeModel> groups, string id) =>
			sessions.Count(s => string.Equals(s.ProfileId, id, StringComparison.OrdinalIgnoreCase))
			+ groups.Sum(g => Count(g.Sessions, g.Groups, id));

		return _appSettings.Settings.Tree.Sum(d => Count(d.Sessions, d.Groups, profileId));
	}

	private int IndexOf(string profileId) => _appSettings.Settings.Profiles.FindIndex(p => p.ProfileId == profileId);

	private static bool IsDefault(string profileId) =>
		string.Equals(profileId, Constants.ProfileSwitch.DefaultProfileId, StringComparison.OrdinalIgnoreCase);

	private static ClaudeProfileModel Copy(ClaudeProfileModel profile) => new() { ProfileId = profile.ProfileId, DisplayName = profile.DisplayName };
}
