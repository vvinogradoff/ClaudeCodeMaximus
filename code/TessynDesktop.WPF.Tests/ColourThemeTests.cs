using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The Colours tab per theme: the picker (HSV maths, view model, control), Light and Dark kept apart, the move of the earlier single-theme colours into both, any key stored
/// generically, the application colours and the transcript page variables.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class ColourThemeTests
{
	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight { get; set; } = true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private sealed class BindingErrors : TraceListener
	{
		public List<string> Messages { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Messages)
					Messages.Add(message);
		}
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				found.Add(match);

			found.AddRange(FindAll<T>(child));
		}

		return found;
	}

	private static void Layout(FrameworkElement element, double width = 700, double height = 600)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static ColoursSectionViewModel NewTab(ClientSettingsStore store, bool dark = false)
	{
		var appearance = new AppearanceService(store, new FixedProbe { IsLight = !dark });
		store.Update<AppearanceSettings>(a => a.Theme = dark ? ClientTheme.Dark : ClientTheme.Light);
		return new ColoursSectionViewModel(store, appearance);
	}

	// ---- the colour maths ----

	[Theory]
	[InlineData("#FF0000", 0, 1, 1)]
	[InlineData("#00FF00", 120, 1, 1)]
	[InlineData("#0000FF", 240, 1, 1)]
	[InlineData("#FFFFFF", 0, 0, 1)]
	[InlineData("#000000", 0, 0, 0)]
	public void Hsv_ReadsThePrimaries(string hex, double hue, double saturation, double value)
	{
		var hsv = Hsv.FromHex(hex);
		Assert.Equal(hue, hsv.H, 3);
		Assert.Equal(saturation, hsv.S, 3);
		Assert.Equal(value, hsv.V, 3);
	}

	[Theory]
	[InlineData("#1E90FF")]
	[InlineData("#DAA520")]
	[InlineData("#264F78")]
	[InlineData("#7F7F7F")]
	[InlineData("#80FF8000")]
	[InlineData("#00FFFFFF")]
	public void Hsv_RoundTripsAHex(string hex) => Assert.Equal(hex, Hsv.FromHex(hex).ToHex());

	[Fact]
	public void Hsv_RoundTripsEveryPaletteColour_AndEvery16thRgbCube()
	{
		Assert.All(ColourPickerViewModel.Palette, hex => Assert.Equal(hex, Hsv.FromHex(hex).ToHex()));
		for (var r = 0; r < 256; r += 17)
		for (var g = 0; g < 256; g += 17)
		for (var b = 0; b < 256; b += 17)
		{
			var hex = $"#{r:X2}{g:X2}{b:X2}";
			Assert.Equal(hex, Hsv.FromHex(hex).ToHex());
		}
	}

	// ---- the picker's view model ----

	[Fact]
	public void ThePicker_RaisesPicked_ForWhatAPersonDoes_NotForWhatItIsGiven()
	{
		var picker = new ColourPickerViewModel("#112233");
		var picked = new List<string>();
		picker.Picked += (_, hex) => picked.Add(hex);

		picker.Show("#FF0000");
		Assert.Empty(picked);
		Assert.Equal("#FF0000", picker.Hex);

		picker.PickSquare(1, 0.5);   // full saturation, half value: dark red
		Assert.Equal("#800000", picked[^1]);

		picker.Hue = 120;
		Assert.Equal("#008000", picked[^1]);

		picker.Alpha = 128;
		Assert.Equal("#80008000", picked[^1]);

		picked.Clear();
		picker.PickPaletteCommand.Execute("#1E90FF");   // an opaque palette entry keeps the opacity chosen so far
		Assert.Single(picked);
		Assert.Equal("#801E90FF", picked[0]);
	}

	[Fact]
	public void ThePicker_KeepsTheHue_WhenTheSquareGoesThroughGrey()
	{
		var picker = new ColourPickerViewModel("#0000FF");
		var hue = picker.Hue;
		picker.Show("#808080");   // a grey has no hue of its own
		Assert.Equal(hue, picker.Hue);

		picker.PickSquare(0, 0.2);
		picker.PickSquare(0.9, 0.9);
		Assert.Equal(240, Hsv.FromHex(picker.Hex).H, 0);   // back to colour: still blue
	}

	[Fact]
	public void ThePalette_IsAGridOfValidDistinctColours()
	{
		Assert.Equal(48, ColourPickerViewModel.Palette.Count);
		Assert.Equal(ColourPickerViewModel.Palette.Count, ColourPickerViewModel.Palette.Distinct().Count());
		Assert.All(ColourPickerViewModel.Palette, hex => Assert.True(ColourCatalog.IsValid(hex), hex));
	}

	[Fact]
	public void ThePicker_RefusesAnInvalidPaletteEntry()
	{
		var picker = new ColourPickerViewModel("#112233");
		picker.PickPaletteCommand.Execute("nope");
		Assert.Equal("#112233", picker.Hex);
	}

	// ---- Light and Dark apart ----

	[Fact]
	public void TheTab_StartsOnTheCurrentTheme_AndEachThemeKeepsItsOwnColours()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, dark: true);
		Assert.True(tab.EditingDark);
		Assert.False(tab.EditingLight);

		var selection = tab.Rows.Single(r => r.Key == ColourCatalog.Selection);
		Assert.Equal("#264F78", selection.DefaultHex);   // the dark default
		selection.Set("#112233");
		Assert.Equal("#112233", store.Get<ColourSettings>().Dark[ColourCatalog.Selection]);
		Assert.Empty(store.Get<ColourSettings>().Light);

		tab.EditingLight = true;   // the switch: the rows are the light theme's
		selection = tab.Rows.Single(r => r.Key == ColourCatalog.Selection);
		Assert.Equal("#CDE6F7", selection.Hex);
		Assert.False(selection.IsOverridden);
		selection.Set("#445566");

		Assert.Equal("#445566", store.Get<ColourSettings>().Light[ColourCatalog.Selection]);
		Assert.Equal("#112233", store.Get<ColourSettings>().Dark[ColourCatalog.Selection]);

		tab.EditingDark = true;
		Assert.Equal("#112233", tab.Rows.Single(r => r.Key == ColourCatalog.Selection).Hex);

		// reset one colour of this theme only, then reset all of it
		tab.Rows.Single(r => r.Key == ColourCatalog.Selection).ResetCommand.Execute(null);
		Assert.DoesNotContain(ColourCatalog.Selection, store.Get<ColourSettings>().Dark.Keys);
		Assert.Equal("#445566", store.Get<ColourSettings>().Light[ColourCatalog.Selection]);

		tab.Rows.Single(r => r.Key == ColourCatalog.RecentTier1).Set("#FF0000");
		tab.ResetAllCommand.Execute(null);
		Assert.Empty(store.Get<ColourSettings>().Dark);
		Assert.Equal("#445566", store.Get<ColourSettings>().Light[ColourCatalog.Selection]);   // Light untouched
		Assert.Contains("Dark", tab.ResetAllText);
	}

	[Fact]
	public void TheTab_StartsOnLight_WhenTheCurrentThemeIsLight()
	{
		var tab = NewTab(SettingsTestSupport.NewStore(), dark: false);
		Assert.True(tab.EditingLight);
		Assert.Contains("Light", tab.ResetAllText);
	}

	[Fact]
	public void TheEarlierSingleThemeColours_ShowInBothThemes_AndMoveIntoBothOnTheFirstChange()
	{
		var store = SettingsTestSupport.NewStore();
		store.Update<ColourSettings>(c =>
		{
			c.Overrides[ColourCatalog.RecentTier1] = "#ff0000";
			c.Overrides[ColourCatalog.Selection] = "#112233";
		});

		var tab = NewTab(store);
		Assert.Equal("#FF0000", tab.Rows.Single(r => r.Key == ColourCatalog.RecentTier1).Hex);
		tab.EditingDark = true;
		Assert.Equal("#FF0000", tab.Rows.Single(r => r.Key == ColourCatalog.RecentTier1).Hex);
		Assert.Equal("#112233", tab.Rows.Single(r => r.Key == ColourCatalog.Selection).Hex);

		tab.Rows.Single(r => r.Key == ColourCatalog.Running).Set("#00FF00");   // the first change migrates
		var stored = store.Get<ColourSettings>();
		Assert.Empty(stored.Overrides);
		Assert.Equal("#FF0000", stored.Light[ColourCatalog.RecentTier1]);
		Assert.Equal("#FF0000", stored.Dark[ColourCatalog.RecentTier1]);
		Assert.Equal("#112233", stored.Light[ColourCatalog.Selection]);
		Assert.Equal("#00FF00", stored.Dark[ColourCatalog.Running]);
		Assert.DoesNotContain(ColourCatalog.Running, stored.Light.Keys);
	}

	[Fact]
	public void ResettingAnEarlierColour_InOneTheme_LeavesItInTheOther()
	{
		var store = SettingsTestSupport.NewStore();
		store.Update<ColourSettings>(c => c.Overrides[ColourCatalog.RecentTier1] = "#FF0000");

		var tab = NewTab(store, dark: true);
		tab.Rows.Single(r => r.Key == ColourCatalog.RecentTier1).ResetCommand.Execute(null);

		var stored = store.Get<ColourSettings>();
		Assert.DoesNotContain(ColourCatalog.RecentTier1, stored.Dark.Keys);
		Assert.Equal("#FF0000", stored.Light[ColourCatalog.RecentTier1]);
		Assert.Empty(stored.Overrides);
	}

	[Fact]
	public void AnyKey_IsStoredPerTheme_EvenOneTheCatalogDoesNotKnowYet()
	{
		var store = SettingsTestSupport.NewStore();
		store.Update<ColourSettings>(c => c.Of(false)["SystemRow"] = "#ABCDEF");   // a palette key another change adds

		var tab = NewTab(store);
		tab.Rows.Single(r => r.Key == ColourCatalog.Selection).Set("#010203");

		var stored = store.Get<ColourSettings>();
		Assert.Equal("#ABCDEF", stored.Light["SystemRow"]);   // a change of another colour leaves it alone
		Assert.Equal("#ABCDEF", stored.For(false)["SystemRow"]);
		Assert.False(stored.For(true).ContainsKey("SystemRow"));
		Assert.Null(ColourCatalog.Find("SystemRow"));
		Assert.DoesNotContain("SystemRow", ColourCatalog.PageVariables(stored, false).Keys);
	}

	[Fact]
	public void TheStoredColours_SurviveTheSettingsFileRoundTrip()
	{
		var state = new MemoryClientState();
		var store = SettingsTestSupport.NewStore(state);
		store.Update<ColourSettings>(c =>
		{
			c.Of(false)["A"] = "#111111";
			c.Of(true)["A"] = "#222222";
		});

		var again = SettingsTestSupport.NewStore(state).Get<ColourSettings>();
		Assert.Equal("#111111", again.Light["A"]);
		Assert.Equal("#222222", again.Dark["A"]);
	}

	// ---- the applier and the page ----

	[Fact]
	public void TheApplier_SetsTheFluentBrushesOfAChangedColour_PerTheme_AndTakesThemOutAgain()
	{
		var store = SettingsTestSupport.NewStore();
		var probe = new FixedProbe();
		var appearance = new AppearanceService(store, probe);
		var applier = new ColourApplier(store, appearance, new ImmediateUiDispatcher());

		StaTestRunner.Run(() =>
		{
			var resources = new ResourceDictionary();
			applier.Apply(resources);
			Assert.False(resources.Contains("TextFillColorPrimaryBrush"));   // unchanged: Fluent's own shows
			Assert.Equal(Color.FromRgb(0x1A, 0x1A, 0x1A), ((SolidColorBrush)resources["Colour.Text"]).Color);

			store.Update<ColourSettings>(c =>
			{
				c.Light[ColourCatalog.Text] = "#101010";
				c.Dark[ColourCatalog.Text] = "#EEEEEE";
				c.Light[ColourCatalog.InputBackground] = "#FFFFE0";
			});
			applier.Apply(resources);
			Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), ((SolidColorBrush)resources["TextFillColorPrimaryBrush"]).Color);
			Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xE0), ((SolidColorBrush)resources["ControlFillColorDefaultBrush"]).Color);
			Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xE0), ((SolidColorBrush)resources["ControlSolidFillColorDefaultBrush"]).Color);

			store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Dark);
			applier.Apply(resources);
			Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), ((SolidColorBrush)resources["TextFillColorPrimaryBrush"]).Color);
			Assert.False(resources.Contains("ControlFillColorDefaultBrush"));   // only Light changed it

			store.Update<ColourSettings>(c => c.Dark.Remove(ColourCatalog.Text));
			applier.Apply(resources);
			Assert.False(resources.Contains("TextFillColorPrimaryBrush"));
		});
	}

	[Fact]
	public void TheApplier_SetsAnApplicationColourOnTheLiveApplication_AndAControlTakesItAtOnce()
	{
		var store = SettingsTestSupport.NewStore();
		var appearance = new AppearanceService(store, new FixedProbe());
		var applier = new ColourApplier(store, appearance, new ImmediateUiDispatcher());

		StaTestRunner.Run(() =>
		{
			var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
			var block = new TextBlock();
			block.SetResourceReference(TextBlock.ForegroundProperty, "TitleBarBackgroundBrush");
			var window = new Window { Content = block };
			store.Update<ColourSettings>(c => c.Light[ColourCatalog.TitleBarBackground] = "#336699");
			applier.Apply(app.Resources);
			try
			{
				Assert.Equal(Color.FromRgb(0x33, 0x66, 0x99), ((SolidColorBrush)block.Foreground).Color);
			}
			finally
			{
				app.Resources.Remove("TitleBarBackgroundBrush");
				app.Resources.Remove("StatusBarBackgroundBrush");
				window.Content = null;
			}
		});
	}

	[Fact]
	public void ThePageVariables_AreOnlyTheChangedColoursOfTheTheme_InCssOrder()
	{
		var settings = new ColourSettings();
		Assert.Empty(ColourCatalog.PageVariables(settings, false));

		settings.Light[ColourCatalog.PageBackground] = "#FFEEDD";
		settings.Light[ColourCatalog.Accent] = "#80102030";   // WPF: alpha first
		settings.Light[ColourCatalog.Text] = "#101010";       // an application colour the page does not use
		settings.Dark[ColourCatalog.PageText] = "#EEEEEE";
		settings.Light[ColourCatalog.PageLine] = "bad";

		var light = ColourCatalog.PageVariables(settings, false);
		Assert.Equal("#FFEEDD", light["--bg"]);
		Assert.Equal("#10203080", light["--accent"]);   // CSS: alpha last
		Assert.Equal(2, light.Count);
		Assert.Equal("#EEEEEE", Assert.Single(ColourCatalog.PageVariables(settings, true)).Value);
	}

	[Fact]
	public void ThePageMessage_CarriesTheColours_AndTheServiceFollowsTheTheme()
	{
		var store = SettingsTestSupport.NewStore();
		var appearance = new AppearanceService(store, new FixedProbe());
		var raised = 0;
		appearance.Changed += (_, _) => raised++;
		Assert.Empty(appearance.PageColours);

		store.Update<ColourSettings>(c => c.Dark[ColourCatalog.PageBackground] = "#101820");
		Assert.Equal(1, raised);   // a colour change reaches the page like a theme change does
		Assert.Empty(appearance.PageColours);   // Light is in effect

		store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Dark);
		Assert.Equal("#101820", appearance.PageColours["--bg"]);

		var message = PageAppearance.From(true, appearance.Current, appearance.PageColours).ToMessage();
		Assert.Contains("\"colours\":{\"--bg\":\"#101820\"}", message);
		Assert.DoesNotContain("colours", PageAppearance.From(true, appearance.Current).ToMessage());   // none: the page drops the ones it had
	}

	[Fact]
	public void EveryCatalogColour_IsValid_Unique_AndTheTargetsAreBrushNames()
	{
		Assert.Equal(ColourCatalog.All.Count, ColourCatalog.All.Select(d => d.Key).Distinct().Count());
		Assert.All(ColourCatalog.All, d =>
		{
			Assert.True(ColourCatalog.IsValid(d.LightDefault), d.Key);
			Assert.True(ColourCatalog.IsValid(d.DarkDefault), d.Key);
			Assert.All(d.TargetBrushes, t => Assert.EndsWith("Brush", t));
			Assert.True(d.Css == null || d.Css.StartsWith("--", StringComparison.Ordinal), d.Key);
		});
		Assert.NotEmpty(ColourCatalog.All.Where(d => d.Group == ColourCatalog.AppGroup));
		Assert.NotEmpty(ColourCatalog.All.Where(d => d.Css != null && d.Group == ColourCatalog.PageGroup));
	}

	// ---- the controls ----

	[Fact]
	public void ThePickerControl_MovesItsThumbWithTheSquare_HasNoTextBox_AndBindsCleanly()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				var picker = new ColourPickerViewModel("#FF0000");
				var control = new ColourPickerControl { DataContext = picker };
				Layout(control);

				Assert.Empty(FindAll<TextBox>(control));
				Assert.Equal(2, FindAll<Slider>(control).Count);
				Assert.Equal(ColourPickerViewModel.Palette.Count, FindAll<Button>(control).Count);

				var square = (FrameworkElement)control.FindName("Square");
				var thumb = (FrameworkElement)control.FindName("Thumb");
				Assert.True(square.ActualWidth > 100);

				picker.PickSquare(0.5, 0.25);
				Assert.Equal(0.5 * square.ActualWidth - thumb.Width / 2, Canvas.GetLeft(thumb), 3);
				Assert.Equal(0.75 * square.ActualHeight - thumb.Height / 2, Canvas.GetTop(thumb), 3);

				picker.Show("#00FF00");   // given from outside: the thumb follows, nothing is picked
				Assert.Equal(square.ActualWidth - thumb.Width / 2, Canvas.GetLeft(thumb), 3);
				Assert.Equal(-thumb.Height / 2, Canvas.GetTop(thumb), 3);

				var sliders = FindAll<Slider>(control);
				sliders[0].Value = 240;   // the hue slider drives the view model
				Assert.Equal(240, picker.Hue);
				Assert.Equal("#0000FF", picker.Hex);

				var swatch = FindAll<Button>(control).First();
				swatch.Command.Execute(swatch.CommandParameter);
				Assert.Equal(ColourPickerViewModel.Palette[0], picker.Hex);
			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void TheColoursTab_HasASwatchPerColour_NoTextBox_AndTheThemeSwitch()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store);
		var listener = new BindingErrors();

		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				var view = new ColoursSettingsView { DataContext = tab };
				Layout(view, 800, 5000);

				Assert.Empty(FindAll<TextBox>(view));
				var swatches = FindAll<Button>(view).Where(b => b.Command == null ? false : System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Colour ", StringComparison.Ordinal)).ToList();
				Assert.Equal(ColourCatalog.All.Count, swatches.Count);

				var radios = FindAll<RadioButton>(view);
				Assert.Equal(["Light", "Dark"], radios.Select(r => (string)r.Content));
				Assert.True(radios[0].IsChecked);

				radios[1].IsChecked = true;   // the switch shows the Dark rows
				Assert.True(tab.EditingDark);
				Layout(view, 800, 5000);
				Assert.Equal(ColourCatalog.All.Count, FindAll<Button>(view).Count(b => b.Command != null && System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Colour ", StringComparison.Ordinal)));

				// a swatch opens the picker
				var first = tab.Rows[0];
				var swatch = FindAll<Button>(view).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == first.AutomationName);
				swatch.Command.Execute(null);
				Assert.True(first.IsPickerOpen);

				// a changed colour shows its Reset button, an unchanged one does not
				var reset = FindAll<Button>(view).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == first.ResetAutomationName);
				Assert.Equal(Visibility.Collapsed, reset.Visibility);
				first.Set("#123456");
				Assert.Equal(Visibility.Visible, reset.Visibility);
			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
	}
}
