using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// FR.33 (tdcp-spec 10.A14): ChatGPT profile administration, loopback only, announced by the <c>profiles.codex</c> capability. The rest of the
/// engine surface rides on existing calls: <see cref="HelloAsync"/> (<c>engines</c>), <see cref="CatalogGetAsync"/> (each model's <c>engine</c> and
/// <c>efforts</c>, the ChatGPT profiles), <see cref="UsageGetAsync"/> (labels, <c>engine</c>, <c>exhausted</c>), <see cref="ProfileListAsync"/>
/// (<c>codexProfiles</c>), <see cref="ProfileRemoveAsync"/> (a ChatGPT id deletes it), and the <c>general</c> settings section (the Codex members;
/// Reset to defaults is <see cref="GeneralSettingsPatch.ResetCodexModelTiers"/>). <c>profiles.codexLoginCompleted</c> arrives as a notification
/// (<see cref="SettingsNotificationExtensions.TryGetCodexLoginCompleted"/>).
/// </summary>
public partial interface ITessynClient
{
	/// <summary>
	/// <c>profiles.codexLogin</c>: starts a ChatGPT sign-in and answers at once. <paramref name="mode"/> is <c>browser</c> (open <c>authUrl</c>) or
	/// <c>deviceCode</c> (show <c>userCode</c> and <c>verificationUrl</c>), see <see cref="RemoteProtocol.CodexProfilesAdmin"/>. Without
	/// <paramref name="profileId"/> a new profile is created on success; with one, that profile signs in again.
	/// </summary>
	Task<ProfileCodexLoginResult> ProfileCodexLoginAsync(string mode, string? profileId = null, CancellationToken cancellationToken = default);

	/// <summary><c>profiles.codexLoginCancel</c>: cancels the sign-in; its <c>profiles.codexLoginCompleted</c> then reports the cancel.</summary>
	Task<ProfileCodexLoginCancelResult> ProfileCodexLoginCancelAsync(string loginId, CancellationToken cancellationToken = default);

	/// <summary><c>profiles.codexLogout</c>: signs a ChatGPT profile out; it stays listed. Answers the profile afterwards.</summary>
	Task<CodexProfileDto> ProfileCodexLogoutAsync(string profileId, CancellationToken cancellationToken = default);
}
