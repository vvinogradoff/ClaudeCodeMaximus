using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace TessynDesktop.WPF.Tests;

/// <summary>The client sections of the Settings window (W6a): Appearance, Screensaver, Keyboard shortcuts and the one-time import from Avalonia; plus the window's Save across sections.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsClientSectionTests
{
	private readonly MemoryClientState _state = new();
	private readonly ClientSettingsStore _store;
	private readonly ShellCommands _commands;
	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
	private readonly FakeDialogPresenter _presenter = new();

	public SettingsClientSectionTests()
	{
		_store = SettingsTestSupport.NewStore(_state);
		_commands = SettingsTestSupport.NewCommands(_store);
	}

	[Fact]
	public async Task Appearance_SavesThemeAndFonts_ToTheClientSettings_NotTheDaemon()
	{
		var appearance = new AppearanceSectionViewModel(_store);
		Assert.Equal(ClientTheme.System, appearance.Theme);
		Assert.Equal("13", appearance.AssistantFontSize);

		appearance.Theme = ClientTheme.Dark;
		appearance.InputFontSize = "15.5";
		Assert.True(appearance.HasChanges);
		Assert.True(await appearance.SaveAsync());

		var stored = _store.Get<AppearanceSettings>();
		Assert.Equal(ClientTheme.Dark, stored.Theme);
		Assert.Equal(15.5, stored.InputFontSize);
		Assert.Equal(13, stored.UserFontSize);
		Assert.False(appearance.HasChanges);
		Assert.Empty(_client.Calls);
	}

	[Fact]
	public async Task Appearance_TheTagCloudFontSize_DefaultsToTheCurrentSize_SavesAndIsReportedAsAChange()
	{
		var appearance = new AppearanceSectionViewModel(_store);
		Assert.Equal("14", appearance.TagFontSize);
		Assert.False(appearance.HasChanges);

		appearance.TagFontSize = "17.5";
		Assert.True(appearance.HasChanges);
		Assert.True(await appearance.SaveAsync());

		Assert.Equal(17.5, _store.Get<AppearanceSettings>().TagFontSize);
		Assert.Equal(13, _store.Get<AppearanceSettings>().InputFontSize);
		Assert.False(appearance.HasChanges);

		appearance.TagFontSize = "9";
		await appearance.ReloadAsync();
		Assert.Equal("17.5", appearance.TagFontSize);
	}

	[Theory]
	[InlineData("7")]
	[InlineData("41")]
	[InlineData("big")]
	public async Task Appearance_AnInvalidTagCloudFontSize_IsRefused_AndNothingIsStored(string text)
	{
		var appearance = new AppearanceSectionViewModel(_store);
		appearance.Theme = ClientTheme.Light;
		appearance.TagFontSize = text;

		Assert.False(await appearance.SaveAsync());

		Assert.Equal(14, _store.Get<AppearanceSettings>().TagFontSize);
		Assert.Equal(ClientTheme.System, _store.Get<AppearanceSettings>().Theme);
		Assert.NotNull(appearance.ErrorText);
	}

	[Theory]
	[InlineData("7")]
	[InlineData("41")]
	[InlineData("big")]
	public async Task Appearance_AnInvalidFontSize_IsRefused_AndNothingIsStored(string text)
	{
		var appearance = new AppearanceSectionViewModel(_store);
		appearance.Theme = ClientTheme.Light;
		appearance.UserFontSize = text;

		Assert.False(await appearance.SaveAsync());

		Assert.Equal(ClientTheme.System, _store.Get<AppearanceSettings>().Theme);   // not even the valid part was stored
		Assert.NotNull(appearance.ErrorText);
	}

	[Fact]
	public async Task Screensaver_SavesAndValidates_AndBrowsesThroughTheFolderPicker()
	{
		var picker = new ScriptedFolderPicker([@"E:\Slides"], NullLogger<ScriptedFolderPicker>.Instance);
		var screensaver = new ScreensaverSectionViewModel(_store, picker);
		Assert.Equal("120", screensaver.TimeoutText);

		await screensaver.BrowseAsync();
		screensaver.TimeoutText = "0";   // 0 turns it off
		screensaver.IntervalText = "5";
		Assert.True(await screensaver.SaveAsync());

		var stored = _store.Get<ScreensaverSettings>();
		Assert.Equal(@"E:\Slides", stored.Directory);
		Assert.Equal(0, stored.TimeoutSeconds);
		Assert.Equal(5, stored.SlideshowIntervalSeconds);

		screensaver.IntervalText = "0";   // the interval is at least a second
		Assert.False(await screensaver.SaveAsync());
		Assert.Equal(5, _store.Get<ScreensaverSettings>().SlideshowIntervalSeconds);
	}

	[Fact]
	public async Task Screensaver_ACancelledFolderPick_ChangesNothing()
	{
		var screensaver = new ScreensaverSectionViewModel(_store, new ScriptedFolderPicker([], NullLogger<ScriptedFolderPicker>.Instance));

		await screensaver.BrowseAsync();

		Assert.Equal(string.Empty, screensaver.Directory);
	}

	[Fact]
	public async Task Keyboard_ListsTheRegisteredCommands_WithTheirEffectiveGesture()
	{
		_store.Update<KeyBindingSettings>(k => k.Gestures[Constants.TreeCommands.Rename] = "Ctrl+R");
		var keyboard = new KeyboardSectionViewModel(_store, _commands);

		Assert.Equal(3, keyboard.Rows.Count);
		Assert.Equal("Ctrl+R", keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.Rename).Text);
		Assert.Equal("Ctrl+OemComma", keyboard.Rows.Single(r => r.CommandId == Constants.Settings.OpenCommandId).Text);
		Assert.Equal(string.Empty, keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.AddDirectory).Text);
		await Task.CompletedTask;
	}

	[Fact]
	public async Task Keyboard_StoresOnlyDifferencesFromTheDefault_AndAnEmptyBoxUnbinds()
	{
		var keyboard = new KeyboardSectionViewModel(_store, _commands);
		keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.AddDirectory).Text = "Ctrl+Shift+D";
		keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.Rename).Text = string.Empty;   // unbind F2

		Assert.True(keyboard.HasChanges);
		Assert.True(await keyboard.SaveAsync());

		var gestures = _store.Get<KeyBindingSettings>().Gestures;
		Assert.Equal("Ctrl+Shift+D", gestures[Constants.TreeCommands.AddDirectory]);
		Assert.Equal(string.Empty, gestures[Constants.TreeCommands.Rename]);
		Assert.DoesNotContain(Constants.Settings.OpenCommandId, gestures.Keys);   // the default needs no entry
		Assert.Null(_commands.GetGesture(Constants.TreeCommands.Rename));
		Assert.Equal("Ctrl+Shift+D", _commands.GetGesture(Constants.TreeCommands.AddDirectory));

		keyboard.ResetRowCommand.Execute(keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.Rename));
		await keyboard.SaveAsync();
		Assert.DoesNotContain(Constants.TreeCommands.Rename, _store.Get<KeyBindingSettings>().Gestures.Keys);
		Assert.Equal("F2", _commands.GetGesture(Constants.TreeCommands.Rename));
	}

	[Fact]
	public async Task Keyboard_KeepsTheStoredEntriesOfCommandsItDoesNotShow()
	{
		_store.Update<KeyBindingSettings>(k => k.Gestures["future.command"] = "Ctrl+Q");
		var keyboard = new KeyboardSectionViewModel(_store, _commands);
		keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.AddDirectory).Text = "Ctrl+Shift+D";

		await keyboard.SaveAsync();

		Assert.Equal("Ctrl+Q", _store.Get<KeyBindingSettings>().Gestures["future.command"]);
	}

	[Fact]
	public async Task Keyboard_AnInvalidCombination_IsFlagged_AndRefusedOnSave()
	{
		var keyboard = new KeyboardSectionViewModel(_store, _commands);
		var row = keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.AddDirectory);
		row.Text = "Ctrl+Banana";
		keyboard.Validate();

		Assert.True(row.HasProblem);
		Assert.False(await keyboard.SaveAsync());
		Assert.Empty(_store.Get<KeyBindingSettings>().Gestures);
	}

	[Fact]
	public void Keyboard_TwoCommandsOnOneCombination_AreBothFlagged_ButNotForbidden()
	{
		var keyboard = new KeyboardSectionViewModel(_store, _commands);
		keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.AddDirectory).Text = "F2";   // Rename already has it

		keyboard.Validate();

		Assert.All(keyboard.Rows.Where(r => r.CommandId is Constants.TreeCommands.AddDirectory or Constants.TreeCommands.Rename), r => Assert.True(r.HasProblem));
		Assert.False(keyboard.Rows.Single(r => r.CommandId == Constants.Settings.OpenCommandId).HasProblem);
	}

	[Fact]
	public async Task Save_AppliesEverySectionWithEdits_AndSaysNothingToSaveOtherwise()
	{
		var settings = SettingsTestSupport.NewSettings(_client, _store, _commands, _presenter);
		await settings.InitializeAsync();

		await settings.SaveAsync();
		Assert.Equal(Constants.Settings.NothingToSave, settings.StatusText);
		Assert.Empty(_client.SettingsUpdates);

		settings.General.HttpsProxy = "http://p:1";
		settings.Notifications.ThrottleText = "10";
		settings.Appearance.Theme = ClientTheme.Light;
		settings.Keyboard.Rows.Single(r => r.CommandId == Constants.TreeCommands.AddDirectory).Text = "Ctrl+Shift+D";
		Assert.True(settings.HasChanges);

		await settings.SaveAsync();

		Assert.Equal(2, _client.SettingsUpdates.Count);
		Assert.Equal(ClientTheme.Light, _store.Get<AppearanceSettings>().Theme);
		Assert.Equal("Ctrl+Shift+D", _store.Get<KeyBindingSettings>().Gestures[Constants.TreeCommands.AddDirectory]);
		Assert.Equal(Constants.Settings.Saved, settings.StatusText);
		Assert.False(settings.HasChanges);
	}

	[Fact]
	public async Task Save_OneSectionFailing_DoesNotStopTheOthers_AndIsReported()
	{
		var settings = SettingsTestSupport.NewSettings(_client, _store, _commands, _presenter);
		await settings.InitializeAsync();
		settings.General.OllamaBaseUrl = "ftp://x";
		_client.SettingsUpdateException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "ollamaBaseUrl must be an http(s) URL");
		settings.Appearance.Theme = ClientTheme.Dark;

		await settings.SaveAsync();

		Assert.Equal(ClientTheme.Dark, _store.Get<AppearanceSettings>().Theme);
		Assert.NotNull(settings.ErrorText);
		Assert.Contains("must be an http(s) URL", settings.General.ErrorText);
		Assert.True(settings.General.HasChanges);   // the edit is still there to fix
	}

	[Fact]
	public async Task SettingsChanged_FromTheDaemon_ReachesTheMatchingSection()
	{
		var settings = SettingsTestSupport.NewSettings(_client, _store, _commands, _presenter);
		await settings.InitializeAsync();
		_client.SettingsValues["notifications"] = new NotificationsSettingsDto(false, "x.wav", 99);

		_client.RaiseNotification(RemoteProtocol.SettingsApi.ChangedEvent, new SettingsChangedPayload("notifications", 5));

		Assert.Equal("99", settings.Notifications.ThrottleText);
		settings.Dispose();
	}

	[Fact]
	public async Task AClosedWindow_StopsFollowingTheDaemon()
	{
		var settings = SettingsTestSupport.NewSettings(_client, _store, _commands, _presenter);
		await settings.InitializeAsync();
		settings.Dispose();
		_client.SettingsValues["notifications"] = new NotificationsSettingsDto(false, "x.wav", 99);

		_client.RaiseNotification(RemoteProtocol.SettingsApi.ChangedEvent, new SettingsChangedPayload("notifications", 5));

		Assert.Equal("180", settings.Notifications.ThrottleText);
	}
}
