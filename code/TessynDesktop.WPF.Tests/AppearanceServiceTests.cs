using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The theme (Light, Dark, follow Windows), the day/night toggle and the font sizes behind Ctrl+wheel (W10a, FR.9).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppearanceServiceTests
{
	private sealed class FakeProbe : ISystemThemeProbe
	{
		public bool IsLight { get; set; } = true;

		public event EventHandler? Changed;

		public void Switch(bool light)
		{
			IsLight = light;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	private readonly FakeProbe _probe = new();
	private readonly ClientSettingsStore _store = new(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
	private readonly AppearanceService _service;
	private int _changes;

	public AppearanceServiceTests()
	{
		_service = new AppearanceService(_store, _probe);
		_service.Changed += (_, _) => _changes++;
	}

	[Fact]
	public void System_FollowsWindows_AndTracksItsSwitch()
	{
		Assert.Equal(ClientTheme.System, _service.Chosen);
		Assert.False(_service.IsDark);

		_probe.Switch(light: false);

		Assert.True(_service.IsDark);
		Assert.Equal(1, _changes);
	}

	[Theory]
	[InlineData(ClientTheme.Dark, true)]
	[InlineData(ClientTheme.Light, false)]
	public void ExplicitChoice_WinsOverWindows(ClientTheme theme, bool dark)
	{
		_probe.IsLight = dark;   // Windows says the opposite of the choice
		_store.Update<AppearanceSettings>(a => a.Theme = theme);

		Assert.Equal(dark, _service.IsDark);
	}

	[Fact]
	public void WindowsSwitch_DoesNotMoveAnExplicitChoice()
	{
		_store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Light);
		_changes = 0;

		_probe.Switch(light: false);

		Assert.False(_service.IsDark);
		Assert.Equal(0, _changes);   // nothing the window shows changed
	}

	[Fact]
	public void Toggle_FromSystemOnALightWindows_ChoosesDark_AndBack()
	{
		_service.ToggleTheme();

		Assert.Equal(ClientTheme.Dark, _service.Chosen);
		Assert.True(_service.IsDark);
		Assert.Equal(1, _changes);

		_service.ToggleTheme();

		Assert.Equal(ClientTheme.Light, _service.Chosen);
		Assert.False(_service.IsDark);
	}

	[Fact]
	public void Toggle_FromSystemOnADarkWindows_ChoosesLight()
	{
		_probe.IsLight = false;

		_service.ToggleTheme();

		Assert.Equal(ClientTheme.Light, _service.Chosen);
	}

	[Theory]
	[InlineData(FontTarget.Assistant, 14)]
	[InlineData(FontTarget.AssistantMarkdown, 14)]
	[InlineData(FontTarget.User, 14)]
	[InlineData(FontTarget.Input, 14)]
	public void AdjustFont_ChangesOnlyTheTarget(FontTarget target, double expected)
	{
		_service.AdjustFont(target, +1);

		var sizes = _service.Current;
		var all = new Dictionary<FontTarget, double>
		{
			[FontTarget.Assistant] = sizes.AssistantFontSize,
			[FontTarget.AssistantMarkdown] = sizes.AssistantMarkdownFontSize,
			[FontTarget.User] = sizes.UserFontSize,
			[FontTarget.Input] = sizes.InputFontSize,
		};
		Assert.Equal(expected, all[target]);
		Assert.All(all.Where(p => p.Key != target), p => Assert.Equal(Constants.Defaults.FontSize, p.Value));
		Assert.Equal(1, _changes);
	}

	[Fact]
	public void AdjustFont_StaysWithinTheZoomLimits()
	{
		for (var i = 0; i < 40; i++)
			_service.AdjustFont(FontTarget.User, +1);

		Assert.Equal(Constants.Appearance.ZoomMax, _service.Current.UserFontSize);

		for (var i = 0; i < 60; i++)
			_service.AdjustFont(FontTarget.User, -1);

		Assert.Equal(Constants.Appearance.ZoomMin, _service.Current.UserFontSize);
	}

	[Fact]
	public void ImportedValues_AreWhatTheServiceReports()
	{
		// The Avalonia import writes the section; the service reads it with no further step.
		_store.Update<AppearanceSettings>(a =>
		{
			a.Theme = ClientTheme.Dark;
			a.AssistantFontSize = 15;
			a.AssistantMarkdownFontSize = 14;
			a.UserFontSize = 12;
			a.InputFontSize = 16;
		});

		Assert.True(_service.IsDark);
		Assert.Equal(PageAppearance.From(true, _service.Current), new PageAppearance("dark", 15, 14, 12));
		Assert.Equal(16, _service.Current.InputFontSize);
	}

	[Fact]
	public void TagFontSize_DefaultsToTheSizeTheChipsHad_AndAChangeIsAnnounced()
	{
		Assert.Equal(14, _service.Current.TagFontSize);
		Assert.Equal(Constants.Defaults.TagFontSize, _service.Current.TagFontSize);

		_store.Update<AppearanceSettings>(a => a.TagFontSize = 18);

		Assert.Equal(18, _service.Current.TagFontSize);
		Assert.Equal(1, _changes);
	}

	[Fact]
	public void PageMessage_CarriesTheThemeAndTheThreeSizes()
	{
		var json = new PageAppearance("dark", 15, 14, 12).ToMessage();

		Assert.Equal("""{"cmd":"appearance","theme":"dark","asst":15,"md":14,"user":12,"lh":1.25}""", json);
	}

	[Fact]
	public void PageMessage_CarriesTheSystemRowColourWhenGiven()
	{
		var json = new PageAppearance("light", 15, 14, 12, "#BFE0F7").ToMessage();

		Assert.Equal("""{"cmd":"appearance","theme":"light","asst":15,"md":14,"user":12,"sys":"#BFE0F7","lh":1.25}""", json);
	}

	[Theory]
	[InlineData(false, "#BFE0F7")]
	[InlineData(true, "#1A3A50")]
	public void SystemRowColour_DefaultsToAvaloniasPaleBlueInBothThemes(bool dark, string expected)
	{
		var definition = ColourCatalog.Find(ColourCatalog.SystemRow)!;

		Assert.Equal(expected, ColourCatalog.Effective(definition, null, dark));
		Assert.Equal("#445566", ColourCatalog.Effective(definition, new Dictionary<string, string> { [ColourCatalog.SystemRow] = "#445566" }, dark));
	}
}
