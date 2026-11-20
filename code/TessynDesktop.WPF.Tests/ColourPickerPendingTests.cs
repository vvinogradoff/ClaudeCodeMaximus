using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;
using TessynDesktop.WPF.Views.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.9.19 (UC.CL.05, TC.CL.30-32, 34-36): the colour picker keeps colours pending until Apply or Save, lists the 140 CSS named colours and shows the harmonies of the new colour; and
/// the check behind the Architect's "I didn't see the colour being updated": an applied colour reaches the tree rows, the Recent list, the Focus chips, the selected row and the
/// transcript page's appearance message. TC.CL.33 (the Close prompt) waits for the Architect's decision (Focus item A54) and has no test yet.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class ColourPickerPendingTests
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

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();
	private readonly ImmediateUiDispatcher _ui = new();

	private static ColoursSectionViewModel NewTab(ClientSettingsStore store, out AppearanceService appearance, bool dark = false)
	{
		appearance = new AppearanceService(store, new FixedProbe { IsLight = !dark });
		store.Update<AppearanceSettings>(a => a.Theme = dark ? ClientTheme.Dark : ClientTheme.Light);
		return new ColoursSectionViewModel(store, appearance);
	}

	private static ColourRowViewModel Row(ColoursSectionViewModel tab, string key) => tab.Rows.Single(r => r.Key == key);

	private static double HueDistance(double a, double b)
	{
		var d = Math.Abs(a - b) % 360;
		return Math.Min(d, 360 - d);
	}

	// ---- TC.CL.30: dragging changes only the preview ----

	[Fact]
	public void MovingInThePicker_ChangesOnlyThePreview_NothingIsStoredOrApplied()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out var appearance);
		var colourChanges = 0;
		var appearanceChanges = 0;
		store.Changed += (_, e) => colourChanges += e.Section == Constants.SettingsSections.Colours ? 1 : 0;
		appearance.Changed += (_, _) => appearanceChanges++;
		var row = Row(tab, ColourCatalog.RecentTier1);
		var before = row.Hex;

		row.OpenPickerCommand.Execute(null);
		for (var i = 0; i < 50; i++)
			row.Picker.PickSquare(i / 50.0, 1 - i / 50.0);   // a drag across the colour area

		row.Picker.Alpha = 100;   // the opacity slider
		row.Picker.Hue = 200;

		Assert.NotEqual(before, row.Picker.Hex);   // the preview moved
		Assert.Equal(before, row.Hex);   // the row did not
		Assert.False(row.IsPending);
		Assert.False(tab.HasChanges);
		Assert.Empty(store.Get<ColourSettings>().Light);
		Assert.Equal(0, colourChanges);
		Assert.Equal(0, appearanceChanges);
	}

	// ---- TC.CL.31: closing keeps the colour pending; Esc drops it ----

	[Fact]
	public void ClosingThePicker_LeavesAPendingColourOnTheRow_AndEscDropsTheEdit()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out _);
		var row = Row(tab, ColourCatalog.RecentTier1);
		var stored = row.Hex;

		row.OpenPickerCommand.Execute(null);
		row.Picker.Show("#123456");   // stands for a drag that ended on this colour
		row.IsPickerOpen = false;   // a click outside
		Assert.Equal("#123456", row.Hex);
		Assert.True(row.IsPending);
		Assert.True(tab.HasChanges);
		Assert.Empty(store.Get<ColourSettings>().Light);

		row.OpenPickerCommand.Execute(null);
		Assert.Equal("#123456", row.Picker.Hex);   // the picker starts on the pending colour
		row.Picker.PickSquare(0.2, 0.2);
		row.Picker.Cancel();   // Esc
		Assert.False(row.IsPickerOpen);
		Assert.Equal("#123456", row.Hex);   // the previous colour (the pending one) stays
		Assert.Equal("#123456", row.Picker.Hex);

		row.Picker.Accepted += (_, _) => { };
		row.OpenPickerCommand.Execute(null);
		row.Picker.Show("#654321");
		row.Picker.Accept();   // Enter
		Assert.False(row.IsPickerOpen);
		Assert.Equal("#654321", row.Hex);

		// Esc on a row with nothing pending leaves it as stored.
		var other = Row(tab, ColourCatalog.RecentTier2);
		var otherBefore = other.Hex;
		other.OpenPickerCommand.Execute(null);
		other.Picker.PickSquare(0.9, 0.1);
		other.Picker.Cancel();
		Assert.Equal(otherBefore, other.Hex);
		Assert.False(other.IsPending);
		Assert.NotEqual(stored, row.Hex);
	}

	[Fact]
	public void ThePendingDot_ShowsOnTheSwatch_OnlyWhileTheColourIsNotApplied()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out _);
		StaTestRunner.Run(() =>
		{
			var view = new ColoursSettingsView { DataContext = tab };
			Layout(view, 900, 4000);
			var row = Row(tab, ColourCatalog.RecentTier1);
			Button Swatch() => FindAll<Button>(view).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == row.AutomationName);
			static System.Windows.Shapes.Ellipse Dot(Button swatch) => FindAll<System.Windows.Shapes.Ellipse>(swatch).Single(e => e.Name == "PendingDot");

			Assert.NotEqual(Visibility.Visible, Dot(Swatch()).Visibility);

			row.Set("#123456");
			Layout(view, 900, 4000);
			Assert.Equal(Visibility.Visible, Dot(Swatch()).Visibility);
			Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), ((SolidColorBrush)Swatch().Background).Color);   // the swatch shows the pending colour

			tab.ApplyCommand.Execute(null);   // the rows are read again
			Layout(view, 900, 4000);
			row = Row(tab, ColourCatalog.RecentTier1);
			Assert.NotEqual(Visibility.Visible, Dot(Swatch()).Visibility);
			Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), ((SolidColorBrush)Swatch().Background).Color);
		});
	}

	// ---- TC.CL.32: Apply and Save store the pending colours; Reset is pending too ----

	[Fact]
	public void Apply_StoresEveryPendingColour_InOneUpdate_AndReAppliesTheTranscriptAppearance()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out var appearance);
		var colourChanges = 0;
		store.Changed += (_, e) => colourChanges += e.Section == Constants.SettingsSections.Colours ? 1 : 0;

		Row(tab, ColourCatalog.PageBackground).Set("#102030");
		Row(tab, ColourCatalog.RecentTier1).Set("#405060");
		Row(tab, ColourCatalog.PageText).Set("#AABBCC");
		Assert.Equal(0, colourChanges);
		Assert.True(tab.HasChanges);

		tab.ApplyCommand.Execute(null);

		Assert.Equal(1, colourChanges);   // one update for all three
		var light = store.Get<ColourSettings>().Light;
		Assert.Equal("#102030", light[ColourCatalog.PageBackground]);
		Assert.Equal("#405060", light[ColourCatalog.RecentTier1]);
		Assert.Equal("#AABBCC", light[ColourCatalog.PageText]);
		Assert.False(tab.HasChanges);
		Assert.All(tab.Rows, r => Assert.False(r.IsPending));
		Assert.Equal("#102030", appearance.PageColours["--bg"]);
		Assert.Contains("\"--bg\":\"#102030\"", PageAppearance.From(appearance.IsDark, appearance.Current, appearance.PageColours).ToMessage());
	}

	[Fact]
	public async Task TheWindowsSave_DoesTheSame_AsApply()
	{
		var store = SettingsTestSupport.NewStore();
		var settings = SettingsTestSupport.NewSettings(new FakeTessynClient(), store);
		var colours = settings.Colours;
		Assert.False(settings.HasChanges);

		Row(colours, ColourCatalog.Text).Set("#112233");
		Row(colours, ColourCatalog.Border).Set("#445566");
		Assert.True(settings.HasChanges);   // the window's Save and its unsaved-changes check see it

		await settings.SaveAsync();

		Assert.Equal("#112233", store.Get<ColourSettings>().Light[ColourCatalog.Text]);
		Assert.Equal("#445566", store.Get<ColourSettings>().Light[ColourCatalog.Border]);
		Assert.False(settings.HasChanges);
	}

	[Fact]
	public void ResetAndResetAll_ArePending_UntilApply()
	{
		var store = SettingsTestSupport.NewStore();
		store.Update<ColourSettings>(c =>
		{
			c.Light[ColourCatalog.RecentTier1] = "#FF0000";
			c.Light[ColourCatalog.Running] = "#00FF00";
			c.Dark[ColourCatalog.Running] = "#0000FF";
		});
		var tab = NewTab(store, out _);

		Row(tab, ColourCatalog.RecentTier1).ResetCommand.Execute(null);
		Assert.Equal("#FF0000", store.Get<ColourSettings>().Light[ColourCatalog.RecentTier1]);   // still stored
		Assert.Equal(ColourCatalog.Find(ColourCatalog.RecentTier1)!.LightDefault, Row(tab, ColourCatalog.RecentTier1).Hex);
		Assert.True(Row(tab, ColourCatalog.RecentTier1).IsPending);
		Assert.True(tab.HasChanges);

		tab.ResetAllCommand.Execute(null);
		Assert.Equal(2, store.Get<ColourSettings>().Light.Count);
		Assert.All(tab.Rows, r => Assert.False(r.IsOverridden));

		tab.ApplyCommand.Execute(null);
		Assert.Empty(store.Get<ColourSettings>().Light);
		Assert.Equal("#0000FF", store.Get<ColourSettings>().Dark[ColourCatalog.Running]);   // the other theme is left alone
		Assert.False(tab.HasChanges);
	}

	[Fact]
	public void PendingColours_SurviveTheThemeSwitch_AndReloadDropsThem()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out _);
		Row(tab, ColourCatalog.Text).Set("#112233");
		tab.EditingDark = true;
		Assert.False(Row(tab, ColourCatalog.Text).IsPending);   // the Dark rows have their own
		Row(tab, ColourCatalog.Text).Set("#445566");
		tab.EditingDark = false;
		Assert.Equal("#112233", Row(tab, ColourCatalog.Text).Hex);   // not lost by the switch
		Assert.True(tab.HasChanges);

		tab.ApplyCommand.Execute(null);
		Assert.Equal("#112233", store.Get<ColourSettings>().Light[ColourCatalog.Text]);
		Assert.Equal("#445566", store.Get<ColourSettings>().Dark[ColourCatalog.Text]);

		Row(tab, ColourCatalog.Border).Set("#778899");
		tab.ReloadCommand.Execute(null);
		Assert.False(tab.HasChanges);
		Assert.False(Row(tab, ColourCatalog.Border).IsPending);
	}

	// ---- TC.CL.34: the named colours ----

	[Fact]
	public void TheNamedColours_AreThe140CssNames_WithExactValues()
	{
		var all = NamedColours.All;
		Assert.Equal(140, all.Count);
		Assert.Equal("aliceblue", all[0].Name);
		Assert.Equal("yellowgreen", all[^1].Name);
		Assert.Equal(all.Count, all.Select(c => c.Name).Distinct().Count());
		Assert.Equal(all.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal), all.Select(c => c.Name));
		Assert.All(all, c => Assert.True(ColourCatalog.IsValid(c.Hex) && c.Hex.Length == 7 && c.Hex == c.Hex.ToUpperInvariant(), c.Name));
		Assert.DoesNotContain(all, c => c.Name == "transparent");

		// spot checks from the CSS specification
		Assert.Equal("#EEE8AA", all.Single(c => c.Name == "palegoldenrod").Hex);
		Assert.Equal("#F0F8FF", all.Single(c => c.Name == "aliceblue").Hex);
		Assert.Equal("#9ACD32", all.Single(c => c.Name == "yellowgreen").Hex);
		Assert.Equal("#808080", all.Single(c => c.Name == "gray").Hex);
		Assert.Equal("#008000", all.Single(c => c.Name == "green").Hex);
		Assert.Equal("#00FF00", all.Single(c => c.Name == "lime").Hex);
		Assert.Equal("#FF00FF", all.Single(c => c.Name == "fuchsia").Hex);

		// Independent check: WPF knows the same names with the same values (it also has Transparent and the grey spellings, which are not in the 140).
		foreach (var named in all)
		{
			var wpf = (Color)ColorConverter.ConvertFromString(named.Name);
			Assert.Equal($"#{wpf.R:X2}{wpf.G:X2}{wpf.B:X2}", named.Hex);
		}

		var wpfNames = typeof(Colors).GetProperties().Select(p => p.Name.ToLowerInvariant()).Where(n => n != "transparent" && !n.EndsWith("grey", StringComparison.Ordinal)).ToHashSet();
		Assert.Equal(wpfNames.OrderBy(n => n, StringComparer.Ordinal), all.Select(c => c.Name));
	}

	[Fact]
	public void TheNamedFilter_MatchesPartOfAName_AndAClickSetsTheColourKeepingTheOpacity()
	{
		var picker = new ColourPickerViewModel("#FF0000");
		Assert.Equal(140, picker.NamedColours.Count);

		picker.NamedFilter = "gold";
		Assert.Equal(["darkgoldenrod", "gold", "goldenrod", "lightgoldenrodyellow", "palegoldenrod"], picker.NamedColours.Select(c => c.Name));
		picker.NamedFilter = "  GOLD ";
		Assert.Equal(5, picker.NamedColours.Count);   // case and spaces do not matter
		picker.NamedFilter = "grey";
		Assert.Contains(picker.NamedColours, c => c.Name == "darkgray");   // the other spelling of gray
		picker.NamedFilter = "nosuchcolour";
		Assert.Empty(picker.NamedColours);

		picker.Alpha = 128;
		var raised = new List<string>();
		picker.Picked += (_, hex) => raised.Add(hex);
		picker.PickNamedCommand.Execute("#EEE8AA");   // palegoldenrod
		Assert.Equal("#80EEE8AA", picker.Hex);   // the opacity stays
		Assert.Equal("palegoldenrod", picker.ColourName);
		Assert.True(picker.HasColourName);
		Assert.Equal("#80EEE8AA", raised[^1]);

		picker.PickSquare(0.3, 0.3);
		Assert.Equal(string.Empty, picker.ColourName);
		Assert.False(picker.HasColourName);

		picker.Show("#00FFFF");
		Assert.Equal("aqua / cyan", picker.ColourName);   // two names for one colour
		picker.PickNamedCommand.Execute("not a colour");   // refused
		Assert.Equal("#00FFFF", picker.Hex);
	}

	// ---- TC.CL.35: the harmonies ----

	[Theory]
	[InlineData("#FF0000")]
	[InlineData("#3A7BD5")]
	[InlineData("#8040C0")]
	[InlineData("#80DAA520")]
	public void TheHarmonies_RotateTheHue_AndKeepSaturationBrightnessAndOpacity(string hex)
	{
		var picker = new ColourPickerViewModel(hex);
		var source = Hsv.FromHex(hex);

		void Check(IReadOnlyList<string> swatches, params double[] offsets)
		{
			Assert.Equal(offsets.Length, swatches.Count);
			for (var i = 0; i < offsets.Length; i++)
			{
				var colour = Hsv.FromHex(swatches[i]);
				Assert.True(HueDistance(colour.H, (source.H + offsets[i] + 720) % 360) <= 2.5, $"{hex} {offsets[i]}: hue {colour.H} for {source.H}");
				Assert.Equal(source.S, colour.S, 2);
				Assert.Equal(source.V, colour.V, 2);
				Assert.Equal(source.A, colour.A);
			}
		}

		Check(picker.Complementary, 180);
		Check(picker.Analogous, -30, 30);
		Check(picker.Triadic, 120, 240);
		Assert.Equal("#00FFFF", new ColourPickerViewModel("#FF0000").Complementary[0]);
		Assert.Equal(["#FF0080", "#FF8000"], new ColourPickerViewModel("#FF0000").Analogous);
		Assert.Equal(["#00FF00", "#0000FF"], new ColourPickerViewModel("#FF0000").Triadic);
	}

	[Fact]
	public void TheHarmonies_FollowTheNewColour_AndAClickSetsIt()
	{
		var picker = new ColourPickerViewModel("#FF0000");
		var changed = new List<string>();
		picker.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

		picker.Hue = 120;
		Assert.Contains(nameof(ColourPickerViewModel.Complementary), changed);
		Assert.Contains(nameof(ColourPickerViewModel.Analogous), changed);
		Assert.Contains(nameof(ColourPickerViewModel.Triadic), changed);
		Assert.Equal("#FF00FF", picker.Complementary[0]);   // opposite of green

		picker.Alpha = 90;
		Assert.Equal("#5AFF00FF", picker.Complementary[0]);   // the opacity follows

		var raised = new List<string>();
		picker.Picked += (_, hex) => raised.Add(hex);
		var chosen = picker.Analogous[1];
		picker.PickHarmonyCommand.Execute(chosen);
		Assert.Equal(chosen, picker.Hex);
		Assert.Equal(chosen, raised[^1]);
		Assert.Equal(Hsv.FromHex(chosen).H, picker.Hue, 0);   // and the harmonies are now those of the chosen colour
		picker.PickHarmonyCommand.Execute("bad");
		Assert.Equal(chosen, picker.Hex);
	}

	[Fact]
	public void TheHarmonies_OfAGrey_AreTheGreyItself()
	{
		var picker = new ColourPickerViewModel("#808080");
		Assert.All(picker.Complementary.Concat(picker.Analogous).Concat(picker.Triadic), h => Assert.Equal("#808080", h));
	}

	// ---- TC.CL.36: the popup fits the screen, real layout ----

	[Fact]
	public void ThePickerPopup_WithTheNewSections_FitsTheScreen_AndItsControlsAreLaidOut()
	{
		var picker = new ColourPickerViewModel("#3A7BD5");
		StaTestRunner.Run(() =>
		{
			var control = new ColourPickerControl { DataContext = picker };
			control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			control.Arrange(new Rect(control.DesiredSize));
			control.UpdateLayout();

			var work = SystemParameters.WorkArea;
			Assert.True(control.ActualHeight <= work.Height, $"the picker is {control.ActualHeight} px high on a {work.Height} px work area");
			Assert.Equal(300, control.ActualWidth, 0.5);

			var filter = FindAll<TextBox>(control).Single();
			Assert.Equal("Filter named colours", System.Windows.Automation.AutomationProperties.GetName(filter));
			var list = (ListBox)control.FindName("NamedList");
			var square = (FrameworkElement)control.FindName("Square");
			var harmonies = (FrameworkElement)control.FindName("Harmonies");
			Assert.True(filter.ActualWidth > 100 && filter.ActualHeight > 10);
			Assert.True(list.ActualHeight > 50 && list.ActualWidth > 200);
			Assert.True(BoundsIn(filter, control).Bottom <= BoundsIn(list, control).Top + 1, "the filter box is above the list");
			Assert.True(BoundsIn(square, control).Bottom <= BoundsIn(harmonies, control).Top, "the colour area is above the harmonies");
			Assert.True(BoundsIn(harmonies, control).Bottom <= BoundsIn(filter, control).Top, "the harmonies are above the named colours");

			// the three harmony rows: 1, 2 and 2 clickable swatches, inside the picker
			var swatches = FindAll<Button>(harmonies);
			Assert.Equal(5, swatches.Count);
			Assert.All(swatches, b => Assert.True(b.ActualWidth > 20 && b.ActualHeight > 10 && BoundsIn(b, control).Right <= control.ActualWidth));
			Assert.Equal(["Complementary", "Analogous", "Triadic"], FindAll<TextBlock>(harmonies).Select(t => t.Text));

			// the named colours show a swatch and a name each (the list shows the first ones; the rest scroll)
			var named = FindAll<Button>(list);
			Assert.NotEmpty(named);
			Assert.All(named, b => Assert.NotEmpty(FindAll<TextBlock>(b)));

			// on a short screen the picker scrolls instead of growing off it
			var scroller = (ScrollViewer)control.FindName("Scroller");
			Assert.True(scroller.MaxHeight <= work.Height - 40, $"the picker may be at most {scroller.MaxHeight} px high");
		});
	}

	// ---- the investigation: an applied colour shows everywhere ----

	[Fact]
	public async Task AnAppliedColour_ReachesTheTreeRows_TheRecentList_TheFocusChips_TheSelectedRow_AndTheTranscriptPage()
	{
		var activity = DateTimeOffset.UtcNow.AddMinutes(-5);
		var state = new SessionStateDto("s1", true, false, new RowAttentionDto(2, AttentionKind.Critical), false, activity, activity, false, "Work", Resumable: true);
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.SessionWith(state, "Session one"))];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);

		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out var appearance);
		var applier = new ColourApplier(store, appearance, _ui);

		StaTestRunner.Run(() =>
		{
			var app = StaTestRunner.EnsureApplication();
			Window? window = null;
			try
			{
				applier.Start();   // the live path of the running application: the brushes in the application resources, kept current

				// The views sit in a window of the application (not shown), as in the running app: a change of an application resource reaches the elements of its windows.
				var panel = new TreePanel { DataContext = tree };
				var host = new Grid { Width = 340, Height = 400 };
				host.Children.Add(panel);
				var stack = new StackPanel();
				stack.Children.Add(host);
				window = new Window { Content = stack };
				tree.SelectedNode = tree.Rows.First(r => r.Id == "s1");
				Layout(host, 340, 400);

				var recent = new RecentTabViewModel(new NoActions(), _ui, NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
				recent.Attach(tree);
				var list = new RecentPanel { DataContext = recent, Width = 340, Height = 300 };
				stack.Children.Add(list);
				Layout(list, 340, 300);

				var face = FindAll<SessionRowFace>(panel).First(f => ((TreeNodeViewModel)f.DataContext).Id == "s1");
				var recentFace = FindAll<SessionRowFace>(list).First();
				var chip = new Border { Tag = "ask", Width = 100, Height = 30 };
				stack.Children.Add(chip);
				chip.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/KindPill.xaml", UriKind.Relative) });
				chip.SetResourceReference(FrameworkElement.StyleProperty, "KindPill");
				Layout(chip, 100, 30);

				Color Tint(SessionRowFace f) => ((SolidColorBrush)((Border)f.Content).Background).Color;
				Color Square(SessionRowFace f) => ((SolidColorBrush)FindAll<System.Windows.Shapes.Rectangle>(f).First(r => r.Width == 8 && r.Opacity >= 1).Fill).Color;
				Color Pill(SessionRowFace f) => ((SolidColorBrush)FindAll<Border>(f).Single(b => "critical".Equals(b.Tag)).Background).Color;
				Color Selected()
				{
					var item = FindAll<ListBoxItem>(panel).First(i => i.IsSelected);
					return ((SolidColorBrush)((Border)item.Template.FindName("Fill", item)).Background).Color;
				}

				// the defaults, before any change
				Assert.Equal(Color.FromRgb(0x00, 0x80, 0x00), Tint(face));
				Assert.Equal(Color.FromRgb(0xCD, 0xE6, 0xF7), Selected());

				// a colour is picked: nothing changes anywhere yet
				Row(tab, ColourCatalog.RecentTier1).Set("#123456");
				Row(tab, ColourCatalog.Running).Set("#654321");
				Row(tab, ColourCatalog.FocusCritical).Set("#0A0B0C");
				Row(tab, ColourCatalog.FocusAsk).Set("#C0FFEE");
				Row(tab, ColourCatalog.Selection).Set("#ABCDEF");
				Row(tab, ColourCatalog.PageBackground).Set("#FEDCBA");
				Layout(host, 340, 400);
				Assert.Equal(Color.FromRgb(0x00, 0x80, 0x00), Tint(face));
				Assert.Empty(appearance.PageColours);

				// Apply: they show at once, without a restart or a theme switch
				tab.ApplyCommand.Execute(null);
				Layout(host, 340, 400);
				Layout(list, 340, 300);
				Layout(chip, 100, 30);
				Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Tint(face));   // the tree row's recency box
				Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Tint(recentFace));   // the Recent list's
				Assert.Equal(Color.FromRgb(0x65, 0x43, 0x21), Square(face));   // the running square
				Assert.Equal(Color.FromRgb(0x0A, 0x0B, 0x0C), Pill(face));   // the Focus count pill in the row
				Assert.Equal(Color.FromRgb(0xC0, 0xFF, 0xEE), ((SolidColorBrush)chip.Background).Color);   // a Focus chip
				Assert.Equal(Color.FromRgb(0xAB, 0xCD, 0xEF), Selected());   // the selected row
				Assert.Equal("#FEDCBA", appearance.PageColours["--bg"]);   // the transcript page: its appearance message carries it
				Assert.Contains("\"--bg\":\"#FEDCBA\"", PageAppearance.From(appearance.IsDark, appearance.Current, appearance.PageColours).ToMessage());

				// and back to the default through Reset + Apply
				Row(tab, ColourCatalog.RecentTier1).ResetCommand.Execute(null);
				tab.ApplyCommand.Execute(null);
				Layout(host, 340, 400);
				Assert.Equal(Color.FromRgb(0x00, 0x80, 0x00), Tint(face));
			}
			finally
			{
				window?.Close();
				foreach (var definition in ColourCatalog.All)
				{
					app.Resources.Remove(definition.ResourceKey);
					foreach (var target in definition.TargetBrushes)
					{
						app.Resources.Remove(target);
						if (target.EndsWith("Brush", StringComparison.Ordinal))
							app.Resources.Remove(target[..^"Brush".Length]);
					}
				}
			}
		});
	}

	private sealed class NoActions : ISessionListActions
	{
		public void Open(string nodeId, string name)
		{
		}

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}

	private static Rect BoundsIn(FrameworkElement element, FrameworkElement ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

	private static void Layout(FrameworkElement element, double width, double height)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		void Walk(DependencyObject node)
		{
			for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
			{
				var child = VisualTreeHelper.GetChild(node, i);
				if (child is T match)
					found.Add(match);

				Walk(child);
			}
		}

		Walk(root);
		return found;
	}
}
