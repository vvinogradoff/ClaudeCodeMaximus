using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>profile.list</c>, <c>profile.add</c>, <c>profile.rename</c>, <c>profile.remove</c> and <c>profile.authenticate</c> (package J3), next to
/// <see cref="ProfileRpcModule"/>'s <c>profile.switch</c>. Loopback only: the list carries the Default account's e-mail, and an authenticate opens
/// a console login on this machine. No result carries a config directory.
/// FR.33.5 (tdcp-spec 10.A14): with the Codex services, <c>profile.list</c> also lists the ChatGPT profiles (<c>codexProfiles</c>),
/// <c>profile.remove</c> deletes a ChatGPT profile by its id, and <c>profiles.codexLogin</c>, <c>profiles.codexLoginCancel</c> and
/// <c>profiles.codexLogout</c> are registered, all loopback only. No result carries a <c>CODEX_HOME</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileAdminRpcModule : IRemoteRpcModule
{
	private readonly IProfileAdminService _admin;
	private readonly IProfileLabelService _labels;
	private readonly ICodexProfileService? _codexProfiles;
	private readonly ICodexProfileAdminService? _codexAdmin;
	private readonly IAppSettingsService? _appSettings;
	private readonly CodexProfileNaming _codexNaming = new();

	/// <param name="codexProfiles">Lists the ChatGPT profiles; without it (and <paramref name="codexAdmin"/>) the module serves the Claude profiles only.</param>
	/// <param name="codexAdmin">Signs ChatGPT profiles in and out and deletes them.</param>
	/// <param name="appSettings">Supplies the catalog profile for <c>isCatalogProfile</c>.</param>
	public ProfileAdminRpcModule(IProfileAdminService admin, IProfileLabelService labels,
		ICodexProfileService? codexProfiles = null, ICodexProfileAdminService? codexAdmin = null, IAppSettingsService? appSettings = null)
	{
		_admin         = admin;
		_labels        = labels;
		_codexProfiles = codexProfiles;
		_codexAdmin    = codexAdmin;
		_appSettings   = appSettings;
	}

	private bool HasCodex => _codexProfiles != null && _codexAdmin != null;

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(RemoteProtocol.ProfilesAdmin.ListMethod, async (_, _) => await ListAsync(), remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.AddMethod, Add, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.RenameMethod, Rename, remoteCallable: false);
		registry.Add(RemoteProtocol.ProfilesAdmin.RemoveMethod, async (p, ct) => await RemoveAsync(p, ct), remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.AuthenticateMethod, Authenticate, remoteCallable: false);

		if (!HasCodex)
			return;

		registry.Add(RemoteProtocol.CodexProfilesAdmin.LoginMethod, async (p, ct) => await CodexLoginAsync(p, ct), remoteCallable: false);
		registry.Add(RemoteProtocol.CodexProfilesAdmin.LoginCancelMethod, async (p, ct) => await CodexLoginCancelAsync(p, ct), remoteCallable: false);
		registry.Add(RemoteProtocol.CodexProfilesAdmin.LogoutMethod, async (p, ct) => await CodexLogoutAsync(p, ct), remoteCallable: false);
	}

	private async Task<object?> ListAsync()
	{
		var label     = await _labels.GetDefaultLabelAsync();
		var selected  = _admin.GetSelectedProfileIndex();
		var email     = EmailOf(label);
		var profiles  = new List<ProfileDto>
		{
			new(Constants.ProfileSwitch.DefaultProfileId, label, IsSystem: true, email, ProfileAuthState.Unknown, IsDefault: selected == 0),
		};

		var stored = _admin.GetProfiles();
		for (var i = 0; i < stored.Count; i++)
			profiles.Add(ToDto(stored[i], _admin.GetAuthState(stored[i].ProfileId) ?? ProfileAuthState.Unknown, isDefault: selected == i + 1));

		return new ProfileListResult(profiles, HasCodex ? [.. _codexProfiles!.List().Select(ToCodexDto)] : null);
	}

	private object? Add(JsonNode? @params)
	{
		var name = ReadName(@params);
		var profile = _admin.Add(name);
		return ToDto(profile, ProfileAuthState.LoggedOut, isDefault: false);
	}

	private object? Rename(JsonNode? @params)
	{
		var profileId = TdcpParams.RequireString(@params, "profileId");
		var name      = ReadName(@params);

		var profile = _admin.Rename(profileId, name) ?? throw new TdcpInvalidParamsException($"There is no profile '{profileId}'.");
		return ToDto(profile, _admin.GetAuthState(profile.ProfileId) ?? ProfileAuthState.Unknown, isDefault: IsSelected(profile.ProfileId));
	}

	private async Task<object?> RemoveAsync(JsonNode? @params, CancellationToken cancellationToken)
	{
		var profileId = TdcpParams.RequireString(@params, "profileId");
		if (HasCodex && _codexNaming.IsCodexProfileId(profileId))
			return await RemoveCodexAsync(profileId, cancellationToken);

		if (profileId == Constants.ProfileSwitch.DefaultProfileId)
			throw new TdcpInvalidParamsException("The Default profile cannot be removed.");

		var removal = _admin.Remove(profileId) ?? throw new TdcpInvalidParamsException($"There is no profile '{profileId}'.");
		return new ProfileRemoveResult(ToDto(removal.Profile, ProfileAuthState.Unknown, isDefault: false), removal.PinnedSessionCount);
	}

	private object? Authenticate(JsonNode? @params)
	{
		var profileId = TdcpParams.RequireString(@params, "profileId");
		try
		{
			return new ProfileAuthenticateResult(_admin.TryStartAuthentication(profileId));
		}
		catch (ProfileAdminException ex)
		{
			throw new TdcpInvalidParamsException(ex.Message);
		}
	}

	/// <summary>FR.33.5 Delete: the host is stopped and the folder renamed with <c>_rem</c>; the result is shaped as for a Claude profile, with <c>engine: "codex"</c>.</summary>
	private async Task<object?> RemoveCodexAsync(string profileId, CancellationToken cancellationToken)
	{
		var removal = await CodexCall(() => _codexAdmin!.RemoveAsync(profileId, cancellationToken));
		var profile = removal.Profile;
		return new ProfileRemoveResult(
			new ProfileDto(profile.ProfileId, profile.DisplayName, IsSystem: false, profile.Email, ProfileAuthState.Unknown, IsDefault: false, RemoteProtocol.Engines.Codex, profile.PlanType),
			removal.PinnedSessionCount);
	}

	private async Task<object?> CodexLoginAsync(JsonNode? @params, CancellationToken cancellationToken)
	{
		var mode      = TdcpParams.RequireString(@params, "mode");
		var profileId = TdcpParams.OptionalString(@params, "profileId");
		if (mode != RemoteProtocol.CodexProfilesAdmin.BrowserMode && mode != RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode)
			throw new TdcpInvalidParamsException($"'mode' must be '{RemoteProtocol.CodexProfilesAdmin.BrowserMode}' or '{RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode}'");

		if (profileId != null && !_codexNaming.IsCodexProfileId(profileId))
			throw new TdcpInvalidParamsException($"'{profileId}' is not a ChatGPT profile");

		var deviceCode = mode == RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode;
		var started    = await CodexCall(() => _codexAdmin!.BeginLoginAsync(deviceCode, profileId, cancellationToken));
		return new ProfileCodexLoginResult(started.ProfileId, started.LoginId, mode, started.AuthUrl, started.VerificationUrl, started.UserCode);
	}

	private async Task<object?> CodexLoginCancelAsync(JsonNode? @params, CancellationToken cancellationToken)
	{
		var loginId = TdcpParams.RequireString(@params, "loginId");
		await CodexCall(async () =>
		{
			await _codexAdmin!.CancelLoginAsync(loginId, cancellationToken);
			return true;
		});
		return new ProfileCodexLoginCancelResult(true);
	}

	private async Task<object?> CodexLogoutAsync(JsonNode? @params, CancellationToken cancellationToken)
	{
		var profileId = TdcpParams.RequireString(@params, "profileId");
		if (!_codexNaming.IsCodexProfileId(profileId))
			throw new TdcpInvalidParamsException($"'{profileId}' is not a ChatGPT profile");

		return ToCodexDto(await CodexCall(() => _codexAdmin!.LogoutAsync(profileId, cancellationToken)));
	}

	/// <summary>
	/// The error mapping of the ChatGPT methods: an unknown profile or sign-in (and Codex Default for a delete) is <c>-32602</c>; Codex not installed,
	/// a Codex RPC error or a host that stopped is a user-facing <c>-32000</c> with its text.
	/// </summary>
	private static async Task<T> CodexCall<T>(Func<Task<T>> call)
	{
		try
		{
			return await call();
		}
		catch (InvalidOperationException ex)
		{
			throw new TdcpInvalidParamsException(ex.Message);
		}
		catch (Exception ex) when (ex is CodexNotFoundException or CodexRpcException or CodexHostCrashedException or CodexConnectionLostException)
		{
			throw new TdcpException(ex.Message);
		}
	}

	private CodexProfileDto ToCodexDto(CodexProfileInfoModel profile) =>
		new(profile.ProfileId, profile.DisplayName, profile.Email, profile.PlanType, profile.IsDefault, profile.IsKnownSignedIn, profile.HasAuthError,
			string.Equals(CatalogProfileId(), profile.ProfileId, StringComparison.OrdinalIgnoreCase));

	private string? CatalogProfileId()
	{
		if (_appSettings == null)
			return null;

		lock (_appSettings.SyncRoot)
			return _appSettings.Settings.CodexCatalogProfileId;
	}

	private static string ReadName(JsonNode? @params)
	{
		var name = TdcpParams.RequireString(@params, "displayName").Trim();
		if (name.Length == 0)
			throw new TdcpInvalidParamsException("'displayName' must not be empty");

		if (name.Length > Constants.ProfileAdmin.MaxDisplayNameLength)
			throw new TdcpInvalidParamsException($"'displayName' is longer than {Constants.ProfileAdmin.MaxDisplayNameLength} characters");

		return name;
	}

	private bool IsSelected(string profileId)
	{
		var stored   = _admin.GetProfiles();
		var selected = _admin.GetSelectedProfileIndex();
		return selected >= 1 && selected <= stored.Count && stored[selected - 1].ProfileId == profileId;
	}

	/// <summary>The account e-mail behind the Default label, or null while it is still the fallback.</summary>
	private static string? EmailOf(string label)
	{
		if (!label.StartsWith(Constants.Remote.Profiles.SystemPrefix))
			return null;

		var email = label[Constants.Remote.Profiles.SystemPrefix.Length..];
		return email == Constants.Remote.Profiles.FallbackName ? null : email;
	}

	private static ProfileDto ToDto(ClaudeProfileModel profile, ProfileAuthState authState, bool isDefault) =>
		new(profile.ProfileId, profile.DisplayName, IsSystem: false, Email: null, authState, isDefault);
}
