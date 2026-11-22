using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<ProfileCodexLoginResult> ProfileCodexLoginAsync(string mode, string? profileId = null, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileCodexLoginResult>(
			RemoteProtocol.CodexProfilesAdmin.LoginMethod,
			Params(("mode", Node(mode)), ("profileId", Node(profileId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<ProfileCodexLoginCancelResult> ProfileCodexLoginCancelAsync(string loginId, CancellationToken cancellationToken = default) =>
		CallAsync<ProfileCodexLoginCancelResult>(RemoteProtocol.CodexProfilesAdmin.LoginCancelMethod, Params(("loginId", Node(loginId))), cancellationToken);

	/// <inheritdoc />
	public Task<CodexProfileDto> ProfileCodexLogoutAsync(string profileId, CancellationToken cancellationToken = default) =>
		CallAsync<CodexProfileDto>(RemoteProtocol.CodexProfilesAdmin.LogoutMethod, Params(("profileId", Node(profileId))), cancellationToken);
}
