using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The auth-error indicator of the session pane (W1b): the daemon flags a session whose last turn or compaction failed with invalid credentials
/// (<c>SessionStateDto.authError</c>, in <c>session.get</c> and <c>session.changed</c>) until a turn succeeds or its profile signs in again. For a
/// Claude profile the notice offers Re-authenticate (<c>profile.authenticate</c>); for a ChatGPT profile (FR.33.13) it offers <b>Sign in again</b>,
/// a browser sign-in of that profile (<c>profiles.codexLogin</c> with its id) whose end <c>profiles.codexLoginCompleted</c> reports. The flag clears
/// when the daemon says so.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private string? _pinnedProfileId;
	private string? _pendingLoginId;

	/// <summary>The open session's profile is signed out (the daemon's flag).</summary>
	[ObservableProperty]
	private bool _hasAuthError;

	/// <summary>Outcome of the last Re-authenticate / Sign in again press; cleared with the error.</summary>
	[ObservableProperty]
	private string? _authNotice;

	/// <summary>
	/// The ChatGPT profile a turn sent now would use (FR.33.5), or null when it is a Claude profile. The auth notice and the profile flyout use it to offer
	/// the ChatGPT sign-in instead of Claude's re-authenticate.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ReauthenticateLabel))]
	private string? _chatGptProfileId;

	/// <summary>The auth notice's button: <c>Sign in again</c> for a ChatGPT profile, <c>Re-authenticate</c> for a Claude one.</summary>
	public string ReauthenticateLabel => ChatGptProfileId != null ? Constants.Codex.SignInAgainAction : Constants.ComposerTexts.Reauthenticate;

	private void ApplyAuthError(SessionStateDto state)
	{
		_pinnedProfileId = state.PinnedProfileId;
		HasAuthError = state.AuthError;
		if (!state.AuthError)
			AuthNotice = null;
		UpdateChatGptProfile();
	}

	private void ResetAuthForOpen()
	{
		HasAuthError = false;
		AuthNotice = null;
		_pinnedProfileId = null;
		_pendingLoginId = null;
		ChatGptProfileId = null;
	}

	/// <summary>The profile a turn sent now would use: the pick, else the profile the session was last used with, else its pin, else the default one.</summary>
	private string CurrentProfileId() =>
		PicksOfOpenSession().ProfileId ?? (_lastUsedNodeId == NodeId ? _lastUsed?.ProfileId : null) ?? _pinnedProfileId ?? Constants.TurnTexts.DefaultOption;

	/// <summary>Called when anything the current profile depends on changed (the pick, the session's settings, its state, the catalog).</summary>
	private void UpdateChatGptProfile()
	{
		if (NodeId == null)
		{
			ChatGptProfileId = null;
			return;
		}

		// The engine of the next turn decides (FR.33.5: a profile is never used across engines): a Claude model means a Claude profile.
		var engine = SelectedModel != null ? ModelPickerList.EngineOf(SelectedModel) : _sessionEngine ?? RemoteProtocol.Engines.Claude;
		if (engine != RemoteProtocol.Engines.Codex)
		{
			ChatGptProfileId = null;
			return;
		}

		var id = CurrentProfileId();
		ChatGptProfileId = IsChatGptProfileId(id) ? id : SelectedProfile is { IsCodex: true } shown ? shown.Id : null;
	}

	/// <summary>
	/// A ChatGPT profile id: listed with the Codex engine in the catalog, or by its form (<c>codex-default</c>, <c>codex_N</c>), because the catalog lists
	/// only signed-in ChatGPT profiles and a profile with an auth error may be missing from it.
	/// </summary>
	private bool IsChatGptProfileId(string id) =>
		_allProfiles.Any(p => p.Id == id && p.IsCodex)
		|| id == Constants.Codex.CodexDefaultProfileId
		|| id.StartsWith(Constants.Codex.StoredProfileIdPrefix, StringComparison.Ordinal);

	[RelayCommand]
	private async Task ReauthenticateAsync()
	{
		var id = NodeId;
		if (id == null)
			return;

		if (ChatGptProfileId is { } chatGpt)
		{
			await SignInChatGptAgainAsync(id, chatGpt);
			return;
		}

		try
		{
			var result = await _client.ProfileAuthenticateAsync(CurrentProfileId());
			if (id == NodeId)
				AuthNotice = result.Started ? Constants.ComposerTexts.ReauthenticateStarted : Constants.ComposerTexts.ReauthenticateRunning;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "profile.authenticate failed for {NodeId}", id);
			if (id == NodeId)
				AuthNotice = Constants.ComposerTexts.ReauthenticateFailed + ex.Message;
		}
	}

	/// <summary>FR.33.13: the browser sign-in of the session's ChatGPT profile; the address goes to the system browser (FR.26.9).</summary>
	private async Task SignInChatGptAgainAsync(string nodeId, string profileId)
	{
		try
		{
			var login = await _client.ProfileCodexLoginAsync(RemoteProtocol.CodexProfilesAdmin.BrowserMode, profileId);
			if (nodeId != NodeId)
				return;

			_pendingLoginId = login.LoginId;
			AuthNotice = _systemLauncher.TryOpen(login.AuthUrl)
				? Constants.Codex.BannerBrowserOpened
				: string.Format(Constants.Codex.BannerOpenAddressFormat, login.AuthUrl);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "profiles.codexLogin failed for {NodeId}", nodeId);
			if (nodeId == NodeId)
				AuthNotice = Constants.ComposerTexts.ReauthenticateFailed + ex.Message;
		}
	}

	/// <summary><c>profiles.codexLoginCompleted</c> of the sign-in this pane started: says how it ended (the auth flag itself clears through the state).</summary>
	private bool TryHandleCodexLoginCompleted(RemoteNotification notification)
	{
		if (!notification.TryGetCodexLoginCompleted(out var completed))
			return false;

		_ui.Post(() =>
		{
			if (completed.LoginId != _pendingLoginId)
				return;

			_pendingLoginId = null;
			AuthNotice = completed.Success
				? string.Format(Constants.Codex.BannerSignedInFormat, completed.Email ?? completed.ProfileId)
				: string.Format(Constants.Codex.SignInFailedFormat, string.IsNullOrWhiteSpace(completed.Error) ? Constants.Codex.SignInFailedUnknown : completed.Error);
		});
		return true;
	}
}
