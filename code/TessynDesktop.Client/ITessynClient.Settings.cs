using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// Package J3: the settings sections, profile administration and sound methods. All are loopback only; the daemon announces them with the
/// <c>settings</c>, <c>profiles.admin</c> and <c>sound</c> capabilities. <c>settings.changed</c> and <c>profiles.changed</c> arrive as notifications
/// (<see cref="SettingsNotificationExtensions"/> reads them).
/// </summary>
public partial interface ITessynClient
{
	/// <summary><c>settings.sections</c>: the section names and their versions.</summary>
	Task<SettingsSectionsResult> SettingsSectionsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>settings.get</c>: a section's values and version. <typeparamref name="TValues"/> is the section's DTO
	/// (<see cref="GeneralSettingsDto"/>, <see cref="NotificationsSettingsDto"/> or <see cref="TelegramSettingsDto"/>).
	/// </summary>
	Task<SettingsGetResult<TValues>> SettingsGetAsync<TValues>(string section, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>settings.update</c>: applies <paramref name="patch"/> (the section's patch DTO; unset members are left alone) when <paramref name="baseVersion"/>
	/// is the current version. A stale version is a <see cref="TessynRpcException"/> "Settings changed": reload with <see cref="SettingsGetAsync{TValues}"/>.
	/// </summary>
	Task<SettingsUpdateResult> SettingsUpdateAsync<TPatch>(string section, TPatch patch, long baseVersion, CancellationToken cancellationToken = default)
		where TPatch : class;

	/// <summary><c>profile.list</c>: the Default profile, then the stored ones.</summary>
	Task<ProfileListResult> ProfileListAsync(CancellationToken cancellationToken = default);

	/// <summary><c>profile.add</c>: registers a profile without logging in; follow with <see cref="ProfileAuthenticateAsync"/>.</summary>
	Task<ProfileDto> ProfileAddAsync(string displayName, CancellationToken cancellationToken = default);

	/// <summary><c>profile.rename</c>.</summary>
	Task<ProfileDto> ProfileRenameAsync(string profileId, string displayName, CancellationToken cancellationToken = default);

	/// <summary><c>profile.remove</c>: removes a stored profile (its config directory is preserved with a suffix); answers the removed one and how many sessions are pinned to it.</summary>
	Task<ProfileRemoveResult> ProfileRemoveAsync(string profileId, CancellationToken cancellationToken = default);

	/// <summary><c>profile.authenticate</c>: starts the login on the daemon's machine and answers at once; <c>profiles.changed</c> follows when it ends.</summary>
	Task<ProfileAuthenticateResult> ProfileAuthenticateAsync(string profileId, CancellationToken cancellationToken = default);

	/// <summary><c>profile.switch</c> (ISSUE.013 R1): moves a session, and optionally its descendants, to another profile (<c>"default"</c> or a stored id).</summary>
	Task<ProfileSwitchResultDto> ProfileSwitchAsync(string nodeId, string profileId, bool includeDescendants = false, CancellationToken cancellationToken = default);

	/// <summary><c>sound.list</c>: the .wav files of the Windows sound library.</summary>
	Task<SoundListResult> SoundListAsync(CancellationToken cancellationToken = default);

	/// <summary><c>sound.test</c>: plays a .wav on the daemon's machine.</summary>
	Task<SoundTestResult> SoundTestAsync(string file, CancellationToken cancellationToken = default);
}
