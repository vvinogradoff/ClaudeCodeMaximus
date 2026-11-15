using System.IO;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// The package J3 wrappers of <see cref="TessynClient"/> (settings sections, profile administration, profile switch, sounds) and the typed
/// <c>settings.changed</c> / <c>profiles.changed</c> notifications, against a real loopback daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsClientTests : IDisposable
{
	private readonly SettingsAdminEnv _env = new();

	public SettingsClientTests()
	{
		_env.AddStoredProfile("p1", "first@example.com");
		File.WriteAllText(Path.Combine(_env.DataRoot, "ding.wav"), "RIFF");
	}

	public void Dispose() => _env.Dispose();

	private TessynClient NewClient() =>
		new(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);

	[Fact]
	public async Task Hello_ReportsTheThreeCapabilities()
	{
		await using var client = NewClient();

		await client.ConnectAsync();

		Assert.Contains("settings", client.ServerInfo!.Capabilities);
		Assert.Contains("profiles.admin", client.ServerInfo.Capabilities);
		Assert.Contains("sound", client.ServerInfo.Capabilities);
	}

	[Fact]
	public async Task SettingsSectionsAsync_ListsTheSectionsWithTheirVersions()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var sections = await client.SettingsSectionsAsync();

		Assert.Equal([("general", 1L), ("notifications", 1L), ("telegram", 1L)], sections.Sections.Select(s => (s.Name, s.Version)));
	}

	[Fact]
	public async Task SettingsGetAsync_ReadsATypedSection()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var general = await client.SettingsGetAsync<GeneralSettingsDto>(RemoteProtocol.SettingsApi.GeneralSection);
		var telegram = await client.SettingsGetAsync<TelegramSettingsDto>(RemoteProtocol.SettingsApi.TelegramSection);

		Assert.Equal(("general", 1L, "claude-test"), (general.Section, general.Version, general.Values.ClaudePath));
		Assert.Equal(FileAccessModeDto.WorkingDirectory, general.Values.FileAccess.Mode);
		Assert.Equal(TelegramMirrorModeDto.AnswersOnly, telegram.Values.TelegramMirrorMode);
	}

	[Fact]
	public async Task SettingsUpdateAsync_SendsOnlyTheSetMembers_AndTheValuesFollow()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		var before = await client.SettingsGetAsync<TelegramSettingsDto>("telegram");

		var updated = await client.SettingsUpdateAsync("telegram", new TelegramSettingsPatch(TelegramChatId: -1007, TelegramMirrorMode: TelegramMirrorModeDto.Off), before.Version);

		Assert.Equal(before.Version + 1, updated.Version);
		var after = await client.SettingsGetAsync<TelegramSettingsDto>("telegram");
		Assert.Equal((-1007L, TelegramMirrorModeDto.Off), (after.Values.TelegramChatId, after.Values.TelegramMirrorMode));
		Assert.Equal((before.Values.TelegramEnabled, before.Values.TelegramMentionHandle), (after.Values.TelegramEnabled, after.Values.TelegramMentionHandle)); // not in the patch: untouched
		Assert.Equal(before.Values.TelegramAllowedUserIds, after.Values.TelegramAllowedUserIds);
		Assert.Equal(updated.Version, after.Version);
	}

	[Fact]
	public async Task SettingsUpdateAsync_AStaleVersion_IsATessynRpcException_SettingsChanged()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		await client.SettingsUpdateAsync("notifications", new NotificationsSettingsPatch(NotificationsEnabled: false), 1);

		var ex = await Assert.ThrowsAsync<TessynRpcException>(() =>
			client.SettingsUpdateAsync("notifications", new NotificationsSettingsPatch(NotificationsEnabled: true), 1));

		Assert.Equal((RemoteProtocol.ErrorCodes.DaemonError, "Settings changed"), (ex.Code, ex.Message));
	}

	[Fact]
	public async Task SettingsUpdateAsync_AnInvalidPatch_IsAnInvalidParamsError()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var ex = await Assert.ThrowsAsync<TessynRpcException>(() =>
			client.SettingsUpdateAsync("general", new GeneralSettingsPatch(ClaudePath: "   "), 1));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, ex.Code);
	}

	[Fact]
	public async Task TheTypedNotifications_ReportSettingsChanged_AndProfilesChanged()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		await client.SettingsUpdateAsync("notifications", new NotificationsSettingsPatch(SessionCompletionSoundThrottleSeconds: 12), 1);
		var changed = await NextAsync(probe, n => n.TryGetSettingsChanged(out _));
		Assert.True(changed.TryGetSettingsChanged(out var payload));
		Assert.Equal(new SettingsChangedPayload("notifications", 2), payload);

		await client.ProfileAddAsync("Work");
		var profiles = await NextAsync(probe, n => n.IsProfilesChanged());
		Assert.False(profiles.TryGetSettingsChanged(out _));
	}

	[Fact]
	public async Task TheProfileMethods_ListAddRenameRemoveAuthenticate()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var list = await client.ProfileListAsync();
		var added = await client.ProfileAddAsync("Work");
		var renamed = await client.ProfileRenameAsync(added.Id, "Home");
		var started = await client.ProfileAuthenticateAsync("p1");
		var removed = await client.ProfileRemoveAsync(added.Id);
		var after = await client.ProfileListAsync();

		Assert.Equal(["default", "p1"], list.Profiles.Select(p => p.Id));
		Assert.True(list.Profiles[0].IsSystem);
		Assert.Equal(("profile_1", "Work", ProfileAuthState.LoggedOut), (added.Id, added.DisplayName, added.AuthState));
		Assert.Equal("Home", renamed.DisplayName);
		Assert.True(started.Started);
		Assert.Equal(added.Id, removed.Id);
		Assert.Equal(["default", "p1"], after.Profiles.Select(p => p.Id));
	}

	[Fact]
	public async Task AProfileThatDoesNotExist_IsAnInvalidParamsError()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var ex = await Assert.ThrowsAsync<TessynRpcException>(() => client.ProfileRemoveAsync("nope"));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, ex.Code);
	}

	[Fact]
	public async Task TheSoundMethods_ListAndTest()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var list = await client.SoundListAsync();
		var played = await client.SoundTestAsync(Path.Combine(_env.DataRoot, "ding.wav"));

		Assert.Equal(_env.Sound.Available, list.Sounds.Select(s => s.File));
		Assert.True(played.Played);
	}

	[Fact]
	public async Task ProfileSwitchAsync_SwitchesASession_ThroughTheExistingMethod()
	{
		using var switchEnv = new ProfileSwitchEnv();
		var oldDir = switchEnv.AddProfile("p1", "Account A");
		switchEnv.AddProfile("p2", "Account B");
		var node = switchEnv.AddSession("S", profileId: "p1");
		switchEnv.WriteSession(oldDir, node);
		using var host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), new ProfileRpcModule(switchEnv.Switch)]));
		await using var client = new TessynClient(host.Endpoint, ClientTestHost.Token);
		await client.ConnectAsync();

		var result = await client.ProfileSwitchAsync(node.NodeId, "p2");

		Assert.Equal(("p2", "Account B", true), (result.ProfileId, result.ProfileName, result.NodeMoved));
		Assert.Equal("p2", node.ProfileId);
	}

	private static async Task<RemoteNotification> NextAsync(ClientProbe probe, Func<RemoteNotification, bool> match)
	{
		while (true)
		{
			var notification = await probe.NextNotificationAsync();
			if (match(notification))
				return notification;
		}
	}
}
