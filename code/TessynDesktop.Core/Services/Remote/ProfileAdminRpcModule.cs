using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>profile.list</c>, <c>profile.add</c>, <c>profile.rename</c>, <c>profile.remove</c> and <c>profile.authenticate</c> (package J3), next to
/// <see cref="ProfileRpcModule"/>'s <c>profile.switch</c>. Loopback only: the list carries the Default account's e-mail, and an authenticate opens
/// a console login on this machine. No result carries a config directory.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileAdminRpcModule : IRemoteRpcModule
{
	private readonly IProfileAdminService _admin;
	private readonly IProfileLabelService _labels;

	public ProfileAdminRpcModule(IProfileAdminService admin, IProfileLabelService labels)
	{
		_admin  = admin;
		_labels = labels;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(RemoteProtocol.ProfilesAdmin.ListMethod, async (_, _) => await ListAsync(), remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.AddMethod, Add, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.RenameMethod, Rename, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.RemoveMethod, Remove, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ProfilesAdmin.AuthenticateMethod, Authenticate, remoteCallable: false);
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

		return new ProfileListResult(profiles);
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

	private object? Remove(JsonNode? @params)
	{
		var profileId = TdcpParams.RequireString(@params, "profileId");
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
