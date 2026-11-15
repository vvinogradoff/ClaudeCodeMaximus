using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Package J3 members of <see cref="FakeTessynClient"/>: settings sections, profile administration, profile switch and sounds. Each is scriptable and records its call.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	/// <summary>The sections <see cref="SettingsSectionsAsync"/> reports.</summary>
	public List<SettingsSectionInfoDto> SettingsSections { get; set; } =
		[new(RemoteProtocol.SettingsApi.GeneralSection, 1), new(RemoteProtocol.SettingsApi.NotificationsSection, 1), new(RemoteProtocol.SettingsApi.TelegramSection, 1), new(RemoteProtocol.SettingsApi.AvaloniaUiSection, 1)];

	/// <summary>The values <see cref="SettingsGetAsync{TValues}"/> answers, by section name (the section's DTO).</summary>
	public Dictionary<string, object> SettingsValues { get; } = [];

	/// <summary>The version of each section.</summary>
	public Dictionary<string, long> SettingsVersions { get; } = [];

	/// <summary>Every update the client sent: section, patch and base version.</summary>
	public List<(string Section, object Patch, long BaseVersion)> SettingsUpdates { get; } = [];

	/// <summary>When set, called after each accepted update with the section and the patch; the DTO it returns becomes the section's values (so a later <c>settings.get</c> shows the change).</summary>
	public Func<string, object, object?>? SettingsPatchApplier { get; set; }

	/// <summary>When set, <see cref="SettingsUpdateAsync{TPatch}"/> throws it once (for example a stale-version <c>TessynRpcException</c>) and clears it.</summary>
	public Exception? SettingsUpdateException { get; set; }

	/// <summary>The profiles <see cref="ProfileListAsync"/> answers; the Default profile first.</summary>
	public List<ProfileDto> Profiles { get; set; } =
		[new("default", "[Sys] Default", true, null, ProfileAuthState.Unknown, true)];

	/// <summary>When set, the profile methods throw it.</summary>
	public Exception? ProfileException { get; set; }

	/// <summary>What <see cref="ProfileAuthenticateAsync"/> answers.</summary>
	public bool AuthenticateStarted { get; set; } = true;

	/// <summary>The pinned-session count <see cref="ProfileRemoveAsync"/> reports.</summary>
	public int RemovedProfilePinnedSessions { get; set; }

	/// <summary>The answer of <see cref="ProfileSwitchAsync"/>.</summary>
	public ProfileSwitchResultDto? ProfileSwitchResult { get; set; }

	/// <summary>The files <see cref="SoundListAsync"/> answers.</summary>
	public List<string> Sounds { get; set; } = [];

	public Task<SettingsSectionsResult> SettingsSectionsAsync(CancellationToken cancellationToken = default)
	{
		Record("settings.sections");
		return Task.FromResult(new SettingsSectionsResult([.. SettingsSections]));
	}

	public Task<SettingsGetResult<TValues>> SettingsGetAsync<TValues>(string section, CancellationToken cancellationToken = default)
	{
		Record($"settings.get:{section}");
		var values = SettingsValues.TryGetValue(section, out var scripted)
			? (TValues)scripted
			: throw new InvalidOperationException($"No scripted values for the settings section '{section}'.");
		return Task.FromResult(new SettingsGetResult<TValues>(section, SettingsVersions.GetValueOrDefault(section, 1), values));
	}

	public Task<SettingsUpdateResult> SettingsUpdateAsync<TPatch>(string section, TPatch patch, long baseVersion, CancellationToken cancellationToken = default)
		where TPatch : class
	{
		Record($"settings.update:{section}:{baseVersion}");
		if (SettingsUpdateException != null)
		{
			var failure = SettingsUpdateException;
			SettingsUpdateException = null;
			throw failure;
		}

		if (section == RemoteProtocol.SettingsApi.AvaloniaUiSection)
			throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, $"Settings section '{section}' is read-only");

		lock (SettingsUpdates)
			SettingsUpdates.Add((section, patch, baseVersion));
		var applied = SettingsPatchApplier?.Invoke(section, patch);
		if (applied != null)
			SettingsValues[section] = applied;

		var version = SettingsVersions.GetValueOrDefault(section, 1) + 1;
		SettingsVersions[section] = version;
		return Task.FromResult(new SettingsUpdateResult(version));
	}

	/// <summary>When set, <see cref="ProfileListAsync"/> awaits it (with the 1-based call number) after it read the list, so a test can make an older answer arrive last.</summary>
	public Func<int, Task>? ProfileListDelay { get; set; }

	private int _profileListCalls;

	public async Task<ProfileListResult> ProfileListAsync(CancellationToken cancellationToken = default)
	{
		Record("profile.list");
		ThrowIfProfileFails();
		var answer = new ProfileListResult([.. Profiles]);
		var call = Interlocked.Increment(ref _profileListCalls);
		if (ProfileListDelay != null)
			await ProfileListDelay(call);

		return answer;
	}

	public Task<ProfileDto> ProfileAddAsync(string displayName, CancellationToken cancellationToken = default)
	{
		Record($"profile.add:{displayName}");
		ThrowIfProfileFails();
		var profile = new ProfileDto($"profile_{Profiles.Count}", displayName, false, null, ProfileAuthState.LoggedOut, false);
		Profiles.Add(profile);
		return Task.FromResult(profile);
	}

	public Task<ProfileDto> ProfileRenameAsync(string profileId, string displayName, CancellationToken cancellationToken = default)
	{
		Record($"profile.rename:{profileId}:{displayName}");
		ThrowIfProfileFails();
		var index = Profiles.FindIndex(p => p.Id == profileId);
		var renamed = Profiles[index] with { DisplayName = displayName };
		Profiles[index] = renamed;
		return Task.FromResult(renamed);
	}

	public Task<ProfileRemoveResult> ProfileRemoveAsync(string profileId, CancellationToken cancellationToken = default)
	{
		Record($"profile.remove:{profileId}");
		ThrowIfProfileFails();
		var removed = Profiles.Single(p => p.Id == profileId);
		Profiles.Remove(removed);
		return Task.FromResult(new ProfileRemoveResult(removed, RemovedProfilePinnedSessions));
	}

	public Task<ProfileAuthenticateResult> ProfileAuthenticateAsync(string profileId, CancellationToken cancellationToken = default)
	{
		Record($"profile.authenticate:{profileId}");
		ThrowIfProfileFails();
		return Task.FromResult(new ProfileAuthenticateResult(AuthenticateStarted));
	}

	public Task<ProfileSwitchResultDto> ProfileSwitchAsync(string nodeId, string profileId, bool includeDescendants = false, CancellationToken cancellationToken = default)
	{
		Record($"profile.switch:{nodeId}:{profileId}:{includeDescendants}");
		ThrowIfProfileFails();
		return Task.FromResult(ProfileSwitchResult ?? new ProfileSwitchResultDto(profileId, profileId, true, [], [], [], []));
	}

	public Task<SoundListResult> SoundListAsync(CancellationToken cancellationToken = default)
	{
		Record("sound.list");
		return Task.FromResult(new SoundListResult([.. Sounds.Select(f => new SoundDto(f))]));
	}

	public Task<SoundTestResult> SoundTestAsync(string file, CancellationToken cancellationToken = default)
	{
		Record($"sound.test:{file}");
		return Task.FromResult(new SoundTestResult(true));
	}

	private void ThrowIfProfileFails()
	{
		if (ProfileException != null)
			throw ProfileException;
	}
}
