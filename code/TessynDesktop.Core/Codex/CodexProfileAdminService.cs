using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Codex;

/// <inheritdoc cref="ICodexProfileAdminService" />
/// <remarks>
/// A singleton because it subscribes to the profile service's <c>AuthErrorChanged</c> for the life of the daemon (each change is a
/// <c>profiles.changed</c>), and it owns the background waits that turn a finished sign-in into <c>profiles.codexLoginCompleted</c>. The bus never
/// blocks, so publishing from the profile service's threads is safe. Created by Claude.
/// </remarks>
public sealed class CodexProfileAdminService : ICodexProfileAdminService
{
	private static readonly ILogger _log = Log.ForContext<CodexProfileAdminService>();

	private readonly ICodexProfileService _profiles;
	private readonly IAppSettingsService _appSettings;
	private readonly IRemoteEventBus _bus;
	private readonly PinnedSessionCounter _pins = new();

	public CodexProfileAdminService(ICodexProfileService profiles, IAppSettingsService appSettings, IRemoteEventBus bus)
	{
		_profiles    = profiles;
		_appSettings = appSettings;
		_bus         = bus;

		_profiles.AuthErrorChanged += (_, _) => PublishProfilesChanged();
	}

	/// <inheritdoc />
	public async Task<CodexSignInStartModel> BeginLoginAsync(bool deviceCode, string? profileId, CancellationToken cancellationToken = default)
	{
		var started = await _profiles.BeginSignInAsync(deviceCode, profileId, cancellationToken).ConfigureAwait(false);
		_ = PublishWhenCompletedAsync(started);
		return started;
	}

	/// <inheritdoc />
	public Task CancelLoginAsync(string loginId, CancellationToken cancellationToken = default) =>
		_profiles.CancelSignInAsync(loginId, cancellationToken);

	/// <inheritdoc />
	public async Task<CodexProfileInfoModel> LogoutAsync(string profileId, CancellationToken cancellationToken = default)
	{
		var known = Listed(profileId) ?? throw new InvalidOperationException(string.Format(Constants.Codex.Profiles.UnknownProfileFormat, profileId));
		await _profiles.SignOutAsync(known.ProfileId, cancellationToken).ConfigureAwait(false);
		PublishProfilesChanged();
		return Listed(known.ProfileId) ?? known;
	}

	/// <inheritdoc />
	public async Task<CodexProfileRemovalModel> RemoveAsync(string profileId, CancellationToken cancellationToken = default)
	{
		var known = Listed(profileId) ?? throw new InvalidOperationException(string.Format(Constants.Codex.Profiles.UnknownProfileFormat, profileId));
		if (known.IsDefault)
			throw new InvalidOperationException(Constants.Codex.Profiles.DefaultCannotBeDeleted);

		await _profiles.DeleteAsync(known.ProfileId, cancellationToken).ConfigureAwait(false);

		int pinned;
		lock (_appSettings.SyncRoot)
			pinned = _pins.Count(_appSettings.Settings, known.ProfileId);

		PublishProfilesChanged();
		return new CodexProfileRemovalModel(known, pinned);
	}

	private CodexProfileInfoModel? Listed(string profileId) =>
		_profiles.List().FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

	private async Task PublishWhenCompletedAsync(CodexSignInStartModel started)
	{
		CodexSignInOutcomeModel outcome;
		try
		{
			outcome = await _profiles.WaitForSignInAsync(started.LoginId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Waiting for ChatGPT sign-in {LoginId} failed", started.LoginId);
			outcome = new CodexSignInOutcomeModel(false, started.ProfileId, null, null, string.Format(Constants.Codex.Profiles.SignInFailedFormat, ex.Message));
		}

		_bus.Publish(RemoteProtocol.CodexProfilesAdmin.LoginCompletedEvent,
			new ProfileCodexLoginCompletedPayload(started.LoginId, outcome.ProfileId, outcome.Success, outcome.Error, outcome.Email, outcome.PlanType));
		if (outcome.Success)
			PublishProfilesChanged();
	}

	private void PublishProfilesChanged() =>
		_bus.Publish(RemoteProtocol.ProfilesAdmin.ChangedEvent, new ProfilesChangedPayload());
}
