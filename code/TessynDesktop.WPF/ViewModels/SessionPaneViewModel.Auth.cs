using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The auth-error indicator of the session pane (W1b): the daemon flags a session whose last turn or compaction failed with invalid credentials
/// (<c>SessionStateDto.authError</c>, in <c>session.get</c> and <c>session.changed</c>) until a turn succeeds or its profile signs in again. The
/// notice offers Re-authenticate (<c>profile.authenticate</c> for the profile the session uses); the flag clears when the daemon says so.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	/// <summary>The open session's profile is signed out (the daemon's flag).</summary>
	[ObservableProperty]
	private bool _hasAuthError;

	/// <summary>Outcome of the last Re-authenticate press; cleared with the error.</summary>
	[ObservableProperty]
	private string? _authNotice;

	private void ApplyAuthError(SessionStateDto state)
	{
		HasAuthError = state.AuthError;
		if (!state.AuthError)
			AuthNotice = null;
	}

	private void ResetAuthForOpen()
	{
		HasAuthError = false;
		AuthNotice = null;
	}

	/// <summary>The profile a turn sent now would use: the pick, else the profile the session was last used with, else the default one.</summary>
	private string CurrentProfileId() => PicksOfOpenSession().ProfileId ?? (_lastUsedNodeId == NodeId ? _lastUsed?.ProfileId : null) ?? Constants.TurnTexts.DefaultOption;

	[RelayCommand]
	private async Task ReauthenticateAsync()
	{
		var id = NodeId;
		if (id == null)
			return;

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
}
