using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>The daemon sections of the Settings window (W6a, W6b): General, Notifications, Telegram. Versioned minimal patches, conflicts, validation, secrets, capability gating.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsDaemonSectionTests
{
	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
	private readonly FakeDialogPresenter _presenter = new();

	private GeneralSectionViewModel NewGeneral() => new(_client, NullLogger<GeneralSectionViewModel>.Instance);

	private NotificationsSectionViewModel NewNotifications() => new(_client, NullLogger<NotificationsSectionViewModel>.Instance);

	private TelegramSectionViewModel NewTelegram() => new(_client, new DialogService(_presenter, new ImmediateUiDispatcher()), NullLogger<TelegramSectionViewModel>.Instance);

	private static TessynRpcException Stale() => new(RemoteProtocol.ErrorCodes.DaemonError, Constants.Settings.StaleVersionMessage);

	private async Task<GeneralSectionViewModel> LoadedGeneralAsync()
	{
		var general = NewGeneral();
		await general.InitializeAsync(RemoteProtocol.SettingsApi.Capability);
		return general;
	}

	[Fact]
	public async Task General_LoadsEveryValue_AndTheThreeFixedPathsStayReadOnly()
	{
		var general = await LoadedGeneralAsync();

		Assert.Equal(@"C:\Sessions", general.SessionFilesRoot);
		Assert.Equal("claude", general.ClaudePath);
		Assert.Equal(@"C:\Src", general.SourceCodesLocation);
		Assert.Equal("http://localhost:11434", general.OllamaBaseUrl);
		Assert.Equal(FileAccessModeDto.ReferencedOnly, general.FileAccessMode);
		Assert.Equal("a.example.com", general.MediaHostsText);
		Assert.False(general.HasChanges);
	}

	[Fact]
	public async Task General_SaveSendsOnlyWhatChanged_WithTheLoadedVersion()
	{
		_client.SettingsVersions[RemoteProtocol.SettingsApi.GeneralSection] = 7;
		var general = await LoadedGeneralAsync();
		general.HttpsProxy = "  http://127.0.0.1:8080  ";
		general.FileAccessMode = FileAccessModeDto.WorkingDirectory;
		general.MediaHostsText = "a.example.com\r\n*.cdn.example.com, a.example.com";

		Assert.True(general.HasChanges);
		Assert.True(await general.SaveAsync());

		var (section, patch, baseVersion) = Assert.Single(_client.SettingsUpdates);
		Assert.Equal("general", section);
		Assert.Equal(7, baseVersion);
		var sent = Assert.IsType<GeneralSettingsPatch>(patch);
		Assert.Equal("http://127.0.0.1:8080", sent.HttpsProxy);
		Assert.Null(sent.OllamaBaseUrl);
		Assert.Null(sent.AgentToolsEnabled);
		Assert.Equal(FileAccessModeDto.WorkingDirectory, sent.FileAccess?.Mode);
		Assert.Equal(["a.example.com", "*.cdn.example.com"], sent.MediaPreviewHosts);
		Assert.Equal(Constants.Settings.Saved, general.StatusText);
		Assert.False(general.HasChanges);   // reloaded: the saved values are the new baseline
	}

	[Fact]
	public async Task General_NothingChanged_SendsNothing()
	{
		var general = await LoadedGeneralAsync();

		Assert.True(await general.SaveAsync());

		Assert.Empty(_client.SettingsUpdates);
	}

	[Fact]
	public async Task General_ClearingTheMediaHosts_SendsAnEmptyList_NotNothing()
	{
		var general = await LoadedGeneralAsync();
		general.MediaHostsText = string.Empty;

		await general.SaveAsync();

		var sent = Assert.IsType<GeneralSettingsPatch>(Assert.Single(_client.SettingsUpdates).Patch);
		Assert.NotNull(sent.MediaPreviewHosts);
		Assert.Empty(sent.MediaPreviewHosts);
	}

	[Fact]
	public async Task General_TooManyMediaHosts_IsRefusedBeforeAnythingIsSent()
	{
		var general = await LoadedGeneralAsync();
		general.MediaHostsText = string.Join('\n', Enumerable.Range(0, Constants.Settings.MaxMediaHosts + 1).Select(i => $"h{i}.example.com"));

		Assert.False(await general.SaveAsync());

		Assert.Empty(_client.SettingsUpdates);
		Assert.Contains("At most", general.ErrorText);
	}

	[Fact]
	public async Task StaleVersion_ReloadsAndKeepsTheUsersEditsOnTopOfTheNewValues()
	{
		var general = await LoadedGeneralAsync();
		general.HttpsProxy = "http://mine:1";
		// Someone else changed the Ollama address and the version while this window was open.
		_client.SettingsValues["general"] = ((GeneralSettingsDto)_client.SettingsValues["general"]) with { OllamaBaseUrl = "http://other:2" };
		_client.SettingsVersions["general"] = 9;
		_client.SettingsUpdateException = Stale();

		Assert.False(await general.SaveAsync());

		Assert.Equal("http://other:2", general.OllamaBaseUrl);   // the new value arrived
		Assert.Equal("http://mine:1", general.HttpsProxy);        // the edit survived
		Assert.Contains("changed elsewhere", general.StatusText);
		Assert.Null(general.ErrorText);
		Assert.Empty(_client.SettingsUpdates);                    // the stale write was refused

		// Pressing Save again sends only the user's edit, against the new version.
		Assert.True(await general.SaveAsync());
		var (_, patch, baseVersion) = Assert.Single(_client.SettingsUpdates);
		Assert.Equal(9, baseVersion);
		var sent = Assert.IsType<GeneralSettingsPatch>(patch);
		Assert.Equal("http://mine:1", sent.HttpsProxy);
		Assert.Null(sent.OllamaBaseUrl);   // the other client's change is not overwritten
	}

	[Fact]
	public async Task AnotherClientsChange_ReloadsAnUntouchedSection_ButOnlyWarnsAboutAnEditedOne()
	{
		var general = await LoadedGeneralAsync();
		_client.SettingsValues["general"] = ((GeneralSettingsDto)_client.SettingsValues["general"]) with { HttpsProxy = "http://new:3" };

		await general.OnChangedElsewhereAsync(2);
		Assert.Equal("http://new:3", general.HttpsProxy);

		general.OllamaBaseUrl = "http://typing:4";
		_client.SettingsValues["general"] = ((GeneralSettingsDto)_client.SettingsValues["general"]) with { HttpsProxy = "http://newer:5" };
		await general.OnChangedElsewhereAsync(3);

		Assert.Equal("http://new:3", general.HttpsProxy);   // not replaced under the user's hands
		Assert.Equal("http://typing:4", general.OllamaBaseUrl);
		Assert.Contains("changed elsewhere", general.StatusText);
	}

	[Fact]
	public async Task OwnSaveEcho_DoesNotReloadAgain()
	{
		var general = await LoadedGeneralAsync();
		general.HttpsProxy = "http://mine:1";
		await general.SaveAsync();
		var gets = _client.Calls.Count(c => c == "settings.get:general");

		await general.OnChangedElsewhereAsync(_client.SettingsVersions["general"]);   // the daemon's own settings.changed for our save

		Assert.Equal(gets, _client.Calls.Count(c => c == "settings.get:general"));
	}

	[Fact]
	public async Task RefusedPatch_ShowsTheDaemonsMessage_AndKeepsTheEdit()
	{
		var general = await LoadedGeneralAsync();
		general.OllamaBaseUrl = "ftp://nope";
		_client.SettingsUpdateException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "ollamaBaseUrl must be an http(s) URL");

		Assert.False(await general.SaveAsync());

		Assert.Contains("must be an http(s) URL", general.ErrorText);
		Assert.Equal("ftp://nope", general.OllamaBaseUrl);
	}

	[Fact]
	public async Task ASectionTheDaemonDoesNotAnnounce_ShowsANote_AndReadsNothing()
	{
		var client = new FakeTessynClient().AnnouncingSettings(RemoteProtocol.ProfilesAdmin.Capability);
		var general = new GeneralSectionViewModel(client, NullLogger<GeneralSectionViewModel>.Instance);

		await general.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		Assert.False(general.IsAvailable);
		Assert.Contains("settings", general.UnavailableText);
		Assert.DoesNotContain("settings.get:general", client.Calls);
	}

	[Fact]
	public async Task Notifications_ParsesTheThrottle_ListsSounds_AndPatchesOnlyTheChange()
	{
		var notifications = NewNotifications();
		await notifications.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		Assert.Equal("180", notifications.ThrottleText);
		Assert.Equal(2, notifications.Sounds.Count);

		notifications.ThrottleText = "30";
		notifications.NotificationsEnabled = false;
		Assert.True(await notifications.SaveAsync());

		var sent = Assert.IsType<NotificationsSettingsPatch>(Assert.Single(_client.SettingsUpdates).Patch);
		Assert.Equal(30, sent.SessionCompletionSoundThrottleSeconds);
		Assert.False(sent.NotificationsEnabled);
		Assert.Null(sent.SessionCompletionSoundFile);
	}

	[Theory]
	[InlineData("abc")]
	[InlineData("-5")]
	[InlineData("")]
	[InlineData("86401")]
	public async Task Notifications_AnInvalidThrottle_IsRefusedLocally(string text)
	{
		var notifications = NewNotifications();
		await notifications.InitializeAsync(RemoteProtocol.SettingsApi.Capability);
		notifications.ThrottleText = text;

		Assert.True(notifications.HasChanges);   // an entry that cannot be sent still counts as an unsaved change
		Assert.False(await notifications.SaveAsync());

		Assert.Empty(_client.SettingsUpdates);
		Assert.NotNull(notifications.ErrorText);
	}

	[Fact]
	public async Task Notifications_TestSound_PlaysTheChosenFile_AndSaysWhenItIsMissing()
	{
		var notifications = NewNotifications();
		await notifications.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		await notifications.TestSoundAsync();
		Assert.Contains(@"sound.test:C:\Windows\Media\ding.wav", _client.Calls);
		Assert.Null(notifications.SoundTestText);

		notifications.SoundFile = string.Empty;
		var before = _client.Calls.Count(c => c.StartsWith("sound.test", StringComparison.Ordinal));
		await notifications.TestSoundAsync();
		Assert.Equal(before, _client.Calls.Count(c => c.StartsWith("sound.test", StringComparison.Ordinal)));   // nothing to play
	}

	[Fact]
	public async Task Telegram_ParsesTheIds_AndRefusesAnInvalidOneBeforeSending()
	{
		var telegram = NewTelegram();
		await telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability);
		Assert.Equal("11, 22", telegram.AllowedUserIdsText);
		Assert.Equal("-100123", telegram.ChatIdText);

		telegram.AllowedUserIdsText = "33; 44 33,55";
		telegram.MirrorMode = TelegramMirrorModeDto.AnswersOnly;
		Assert.True(await telegram.SaveAsync());
		var sent = Assert.IsType<TelegramSettingsPatch>(Assert.Single(_client.SettingsUpdates).Patch);
		Assert.Equal([33L, 44L, 55L], sent.TelegramAllowedUserIds);
		Assert.Equal(TelegramMirrorModeDto.AnswersOnly, sent.TelegramMirrorMode);
		Assert.Null(sent.TelegramChatId);

		telegram.AllowedUserIdsText = "12, abc";
		Assert.False(await telegram.SaveAsync());
		Assert.Single(_client.SettingsUpdates);
		Assert.Contains("abc", telegram.ErrorText);
	}

	[Fact]
	public async Task Telegram_TheTokenIsWriteOnly_SentOnceAndThenForgotten()
	{
		var telegram = NewTelegram();
		await telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability);
		Assert.Equal(Constants.Settings.TelegramTokenNotSet, telegram.TokenText);
		Assert.True(telegram.TokenSupported);

		telegram.TokenInput = " 123456:ABCdefGhIJKlmNoPQRsTUVwxyZ ";
		await telegram.SaveTokenAsync();

		Assert.Equal(string.Empty, telegram.TokenInput);   // the field is emptied once it was sent
		Assert.Equal(Constants.Settings.TelegramTokenSet, telegram.TokenText);
		Assert.DoesNotContain("ABCdef", telegram.TokenText + telegram.StatusText + telegram.ErrorText + telegram.BridgeText);
		Assert.Contains("telegram.setToken", _client.Calls);
	}

	[Fact]
	public async Task Telegram_AnEmptyToken_IsNotSent()
	{
		var telegram = NewTelegram();
		await telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		await telegram.SaveTokenAsync();

		Assert.DoesNotContain("telegram.setToken", _client.Calls);
		Assert.Equal(Constants.Settings.TelegramTokenEmpty, telegram.ErrorText);
	}

	[Fact]
	public async Task Telegram_RemovingTheToken_AsksFirst()
	{
		_client.TelegramStatus = new TelegramStatusDto(true, true, true);
		var telegram = NewTelegram();
		await telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability);
		Assert.Equal(Constants.Settings.TelegramTokenSet, telegram.TokenText);

		await telegram.ClearTokenAsync();   // the fake presenter cancels by default
		Assert.DoesNotContain("telegram.clearToken", _client.Calls);

		_presenter.Answer = dialog =>
		{
			dialog.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};
		await telegram.ClearTokenAsync();

		Assert.Contains("telegram.clearToken", _client.Calls);
		Assert.Equal(Constants.Settings.TelegramTokenNotSet, telegram.TokenText);
		Assert.Equal(Constants.Settings.TelegramStopped, telegram.BridgeText);
	}

	[Fact]
	public async Task Telegram_TheBridgesLastErrorIsShown_FromTheStatus()
	{
		_client.TelegramStatus = new TelegramStatusDto(true, false, true, "Telegram rejected the bot token");
		var telegram = NewTelegram();

		await telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		Assert.Equal("Telegram rejected the bot token", telegram.BridgeError);
	}

	[Fact]
	public async Task Telegram_AnOlderDaemonWithoutTheTokenCapability_HidesTheTokenControls()
	{
		var client = new FakeTessynClient().AnnouncingSettings(RemoteProtocol.SettingsApi.Capability).WithSettingsValues();
		var telegram = new TelegramSectionViewModel(client, new DialogService(_presenter, new ImmediateUiDispatcher()), NullLogger<TelegramSectionViewModel>.Instance);

		await telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		Assert.False(telegram.TokenSupported);
		Assert.DoesNotContain("telegram.status", client.Calls);
	}
}
