using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<SettingsSectionsResult> SettingsSectionsAsync(CancellationToken cancellationToken = default) =>
		CallAsync<SettingsSectionsResult>(RemoteProtocol.SettingsApi.SectionsMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<SettingsGetResult<TValues>> SettingsGetAsync<TValues>(string section, CancellationToken cancellationToken = default) =>
		CallAsync<SettingsGetResult<TValues>>(RemoteProtocol.SettingsApi.GetMethod, Params(("section", Node(section))), cancellationToken);

	/// <inheritdoc />
	public Task<SettingsUpdateResult> SettingsUpdateAsync<TPatch>(string section, TPatch patch, long baseVersion, CancellationToken cancellationToken = default)
		where TPatch : class =>
		CallAsync<SettingsUpdateResult>(
			RemoteProtocol.SettingsApi.UpdateMethod,
			Params(("section", Node(section)), ("patch", JsonSerializer.SerializeToNode(patch, TdcpJsonOptions.Default)), ("baseVersion", Node(baseVersion))),
			cancellationToken);

	/// <inheritdoc />
	public Task<ProfileListResult> ProfileListAsync(CancellationToken cancellationToken = default) =>
		CallAsync<ProfileListResult>(RemoteProtocol.ProfilesAdmin.ListMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<ProfileDto> ProfileAddAsync(string displayName, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileDto>(RemoteProtocol.ProfilesAdmin.AddMethod, Params(("displayName", Node(displayName))), cancellationToken);

	/// <inheritdoc />
	public Task<ProfileDto> ProfileRenameAsync(string profileId, string displayName, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileDto>(RemoteProtocol.ProfilesAdmin.RenameMethod, Params(("profileId", Node(profileId)), ("displayName", Node(displayName))), cancellationToken);

	/// <inheritdoc />
	public Task<ProfileDto> ProfileRemoveAsync(string profileId, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileDto>(RemoteProtocol.ProfilesAdmin.RemoveMethod, Params(("profileId", Node(profileId))), cancellationToken);

	/// <inheritdoc />
	public Task<ProfileAuthenticateResult> ProfileAuthenticateAsync(string profileId, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileAuthenticateResult>(RemoteProtocol.ProfilesAdmin.AuthenticateMethod, Params(("profileId", Node(profileId))), cancellationToken);

	/// <inheritdoc />
	public Task<ProfileSwitchResultDto> ProfileSwitchAsync(string nodeId, string profileId, bool includeDescendants = false, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileSwitchResultDto>(
			RemoteProtocol.LoopbackMethods.ProfileSwitch,
			Params(("nodeId", Node(nodeId)), ("profileId", Node(profileId)), ("includeDescendants", includeDescendants ? Node(true) : null)),
			cancellationToken);

	/// <inheritdoc />
	public Task<SoundListResult> SoundListAsync(CancellationToken cancellationToken = default) =>
		CallAsync<SoundListResult>(RemoteProtocol.SoundApi.ListMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<SoundTestResult> SoundTestAsync(string file, CancellationToken cancellationToken = default) =>
		CallAsync<SoundTestResult>(RemoteProtocol.SoundApi.TestMethod, Params(("file", Node(file))), cancellationToken);
}
