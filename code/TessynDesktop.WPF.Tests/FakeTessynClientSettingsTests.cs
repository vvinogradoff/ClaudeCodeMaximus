using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The package J3 members of <see cref="FakeTessynClient"/>: scriptable, recording, and usable through <see cref="ITessynClient"/> as the WPF packages will.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTessynClientSettingsTests
{
	[Fact]
	public async Task Settings_AreScriptedAndRecorded_AndAnUpdateBumpsTheVersion()
	{
		var fake = new FakeTessynClient();
		fake.SettingsValues["notifications"] = new NotificationsSettingsDto(true, @"C:\a.wav", 30);
		ITessynClient client = fake;

		var sections = await client.SettingsSectionsAsync();
		var before = await client.SettingsGetAsync<NotificationsSettingsDto>("notifications");
		var updated = await client.SettingsUpdateAsync("notifications", new NotificationsSettingsPatch(NotificationsEnabled: false), before.Version);

		Assert.Equal(4, sections.Sections.Count);
		Assert.Equal(30, before.Values.SessionCompletionSoundThrottleSeconds);
		Assert.Equal(before.Version + 1, updated.Version);
		Assert.Equal([("notifications", before.Version)], fake.SettingsUpdates.Select(u => (u.Section, u.BaseVersion)));
		Assert.Equal(["settings.sections", "settings.get:notifications", $"settings.update:notifications:{before.Version}"], fake.Calls);
	}

	[Fact]
	public async Task Profiles_CanBeAddedRenamedRemovedAndAuthenticated_AndTheSwitchIsRecorded()
	{
		var fake = new FakeTessynClient();
		ITessynClient client = fake;

		var added = await client.ProfileAddAsync("Work");
		await client.ProfileRenameAsync(added.Id, "Home");
		var started = await client.ProfileAuthenticateAsync(added.Id);
		var switched = await client.ProfileSwitchAsync("node-1", added.Id, includeDescendants: true);
		await client.ProfileRemoveAsync(added.Id);

		Assert.True(started.Started);
		Assert.Equal(added.Id, switched.ProfileId);
		Assert.Equal(["default"], (await client.ProfileListAsync()).Profiles.Select(p => p.Id));
		Assert.Contains($"profile.switch:node-1:{added.Id}:True", fake.Calls);
	}

	[Fact]
	public async Task AScriptedFailure_Surfaces()
	{
		var fake = new FakeTessynClient { ProfileException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "nope") };

		await Assert.ThrowsAsync<TessynRpcException>(() => fake.ProfileListAsync());
	}

	[Fact]
	public async Task Sounds_AreListedAndTested()
	{
		var fake = new FakeTessynClient { Sounds = [@"C:\Windows\Media\a.wav"] };

		var list = await fake.SoundListAsync();
		var played = await fake.SoundTestAsync(list.Sounds[0].File);

		Assert.Equal([@"C:\Windows\Media\a.wav"], list.Sounds.Select(s => s.File));
		Assert.True(played.Played);
	}
}
