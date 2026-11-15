using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The read-only <c>avalonia-ui</c> section (decision A23 Q8): it serves the Avalonia client's own settings for a one-time import, writes nothing, takes no
/// part in change detection, and adds no member to the settings model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AvaloniaUiSettingsSectionTests : IDisposable
{
	private readonly string _dir;
	private readonly AppSettingsService _settings;
	private readonly RemoteEventBus _bus = new();
	private readonly SettingsSectionService _service;
	private readonly System.Collections.Generic.List<SettingsChangedPayload> _changes = [];

	public AvaloniaUiSettingsSectionTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "avalonia-ui-section-" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(_dir);
		_settings = new AppSettingsService(_dir);
		_settings.Settings.SessionFilesRoot = Path.Combine(_dir, "sessions");
		_service = new SettingsSectionService(_settings,
			[new GeneralSettingsSection(), new NotificationsSettingsSection(), new TelegramSettingsSection(), new AvaloniaUiSettingsSection()], _bus);
		_bus.Published += n =>
		{
			if (n.Method == RemoteProtocol.SettingsApi.ChangedEvent)
				lock (_changes) _changes.Add(n.Params.Deserialize<SettingsChangedPayload>(TdcpJsonOptions.Default)!);
		};
	}

	public void Dispose()
	{
		_service.Dispose();
		_bus.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	private AvaloniaUiSettingsDto Ui() => (AvaloniaUiSettingsDto)_service.Get("avalonia-ui").Values;

	[Fact]
	public void ItIsListedLast_WithTheFirstVersion()
	{
		Assert.Equal(["general", "notifications", "telegram", "avalonia-ui"], _service.GetSections().Select(s => s.Name));
		Assert.Equal(1, _service.GetSections().Last().Version);
	}

	[Fact]
	public void Get_ServesTheThemeColoursFontSizesKeyBindingsAndScreensaver()
	{
		var model = _settings.Settings;
		model.Theme = "Light";
		model.AssistantFontSize = 14;
		model.AssistantMarkdownFontSize = 15;
		model.UserFontSize = 12.5;
		model.InputFontSize = 16;
		model.LightColors.UserBubbleBackground = "#112233";
		model.DarkColors.CodeBlockText = "#445566";
		model.KeyBindings.Bindings["Send"] = "Ctrl+Enter, Alt+Enter";
		model.ScreensaverDirectory = @"D:\Pictures\Slides";
		model.ScreensaverTimeout = 300;
		model.ScreensaverSlideshowInterval = 7;

		var ui = Ui();

		Assert.Equal(("Light", 14d, 15d, 12.5, 16d), (ui.Theme, ui.AssistantFontSize, ui.AssistantMarkdownFontSize, ui.UserFontSize, ui.InputFontSize));
		Assert.Equal("#112233", ui.LightColors.UserBubbleBackground);
		Assert.Equal("#445566", ui.DarkColors.CodeBlockText);
		Assert.Contains(new KeyBindingDto("Send", "Ctrl+Enter, Alt+Enter"), ui.KeyBindings);
		Assert.Equal(new ScreensaverSettingsDto(@"D:\Pictures\Slides", 300, 7), ui.Screensaver);
	}

	[Fact]
	public void TheKeyBindings_AreSortedByActionName_SoTheResultIsStable()
	{
		_settings.Settings.KeyBindings.Bindings.Clear();
		foreach (var action in new[] { "Zeta", "Alpha", "Mid", "alpha2" })
			_settings.Settings.KeyBindings.Bindings[action] = "Ctrl+X";

		Assert.Equal(["Alpha", "Mid", "Zeta", "alpha2"], Ui().KeyBindings.Select(b => b.Action));   // ordinal
	}

	[Fact]
	public void Get_ChangesNothingInTheModel()
	{
		var before = JsonSerializer.Serialize(_settings.Settings);

		_ = Ui();
		_ = _service.GetSections();

		Assert.Equal(before, JsonSerializer.Serialize(_settings.Settings));
	}

	[Theory]
	[InlineData("{}", 1)]
	[InlineData("""{"theme":"Dark"}""", 1)]
	[InlineData("""{"tdcpToken":"x"}""", 1)]
	[InlineData("""{"theme":"Dark"}""", 99)]   // read-only is refused before the version is compared: not a "Settings changed"
	public void Update_IsRefusedAsReadOnly_WhateverThePatchAndVersion(string patch, long baseVersion)
	{
		var before = JsonSerializer.Serialize(_settings.Settings);

		var ex = Assert.Throws<TdcpInvalidParamsException>(() => _service.Update("avalonia-ui", JsonNode.Parse(patch)!, baseVersion));

		Assert.Contains("read-only", ex.Message);
		Assert.Equal(before, JsonSerializer.Serialize(_settings.Settings));
	}

	[Fact]
	public void ASaveThatChangesTheThemeOrTheBindings_BumpsNoVersion_AndPublishesNothing()
	{
		_settings.Settings.Theme = "Light";
		_settings.Settings.KeyBindings.Bindings["Send"] = "F9";
		_settings.Settings.ScreensaverTimeout = 5;
		_settings.Save();

		Assert.Empty(_changes);
		Assert.Equal([1L, 1L, 1L, 1L], _service.GetSections().Select(s => s.Version));
		Assert.Equal("Light", Ui().Theme);                                      // it still serves the live values
	}

	[Fact]
	public void TheOtherSections_StillDetectTheirChanges_NextToIt()
	{
		_settings.Settings.TelegramMentionHandle = "@me";
		_settings.Settings.Theme = "Light";
		_settings.Save();

		Assert.Equal(["telegram"], _changes.Select(c => c.Section));
	}

	[Fact]
	public void ASectionUpdate_OfAnotherSection_NeverWritesAnAvaloniaMember()
	{
		var model = _settings.Settings;
		model.Theme = "Light";
		model.UserFontSize = 21;
		model.ScreensaverDirectory = @"D:\keep";
		model.KeyBindings.Bindings["Send"] = "F9";
		var before = (model.Theme, model.UserFontSize, model.ScreensaverDirectory, model.KeyBindings.Bindings["Send"]);

		_service.Update("general", JsonNode.Parse("""{"httpsProxy":"http://127.0.0.1:8080"}""")!, 1);
		_service.Update("telegram", JsonNode.Parse("""{"telegramChatId":-5}""")!, 1);

		Assert.Equal(before, (model.Theme, model.UserFontSize, model.ScreensaverDirectory, model.KeyBindings.Bindings["Send"]));
	}

	[Fact]
	public void TheSectionDtos_HoldNoSecretAndNoPathBesidesTheScreensaverDirectory()
	{
		_settings.Settings.TdcpToken = "legacy-token-value-0001";
		_settings.Settings.TelegramTopicNamesByNodeId["n"] = "123456789:AAEsecretInATopicNameForTheTest_0123456";

		var json = JsonSerializer.Serialize(Ui(), TdcpJsonOptions.Default);

		Assert.DoesNotContain("legacy-token-value-0001", json);
		Assert.DoesNotContain("123456789:", json);
		Assert.DoesNotContain(_dir.Replace("\\", "\\\\"), json);
	}
}
