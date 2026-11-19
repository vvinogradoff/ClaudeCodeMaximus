using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Themes.VS2013.Themes;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.21: the whole docking chrome wears Tessyn's light and dark colours and a day/night switch recolours it at once with no panel recreated. The brushes come from
/// <c>DockingColours.Light/Dark.xaml</c> in the application resources, so a switch is the swap of the application's colour dictionary (<see cref="AppearanceApplier"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingThemeViewTests
{
	/// <summary>The palette of a mode, the documented mapping (row = tab row and splitters, content = selected tab and panel body).</summary>
	private sealed record Palette(string Row, string Content, string Hover, string Stroke, string Primary, string Secondary, string Accent, string Guide)
	{
		public static readonly Palette Light = new("#E9E9E9", "#F3F3F3", "#14000000", "#D1D1D1", "#1A1A1A", "#5F5F5F", "#005FB8", "#F9F9F9");
		public static readonly Palette Dark = new("#2B2B2B", "#202020", "#1AFFFFFF", "#3C3C3C", "#FFFFFF", "#C5C5C5", "#60CDFF", "#2B2B2B");
	}

	private sealed class ModeAppearance : IAppearanceService
	{
		public bool Dark { get; set; }

		public ClientTheme Chosen => Dark ? ClientTheme.Dark : ClientTheme.Light;

		public bool IsDark => Dark;

		public AppearanceSettings Current { get; } = new();

		public IReadOnlyDictionary<string, string> PageColours { get; } = new Dictionary<string, string>();

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public void ToggleTheme() => Dark = !Dark;

		public void AdjustFont(FontTarget target, int delta)
		{
		}
	}

	private static Palette PaletteOf(bool dark) => dark ? Palette.Dark : Palette.Light;

	private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

	private static Color ColourOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	private static void Settle(Window window)
	{
		window.UpdateLayout();
		window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
		window.UpdateLayout();
	}

	/// <summary>The system accent Fluent supplies in the running app (the selected tab's line); a known colour here.</summary>
	private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

	/// <summary>The colour dictionary of the mode in place in <paramref name="resources"/>, put there by the production applier (the swap a day/night switch does).</summary>
	private static AppearanceApplier UseColours(ResourceDictionary resources, bool dark, ModeAppearance mode)
	{
		mode.Dark = dark;
		var applier = new AppearanceApplier(mode, new ImmediateUiDispatcher());
		applier.Apply(resources);
		return applier;
	}

	private sealed record Shell(Window Window, DockingArea Area, LayoutAnchorable First, LayoutAnchorable Second, TextBlock FirstContent, TextBlock SecondContent);

	private static Shell BuildShell(bool dark, ModeAppearance mode, out AppearanceApplier applier)
	{
		var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
		var window = new Window { Content = area, Width = 1200, Height = 700, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
		window.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(Accent);
		applier = UseColours(window.Resources, dark, mode);
		window.Show();
		Settle(window);
		var firstContent = new TextBlock { Text = "first" };
		var secondContent = new TextBlock { Text = "second" };
		var first = new LayoutAnchorable { ContentId = "first", Title = "first", Content = firstContent };
		var second = new LayoutAnchorable { ContentId = "second", Title = "second", Content = secondContent };
		var layout = new PreviewDockLayoutApplier();
		layout.Apply(area.Root, area.DocumentPane, first, null, area.AreaWidth, area.AreaHeight);
		layout.Apply(area.Root, area.DocumentPane, second, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 300, StackedWith = ["first"] }, area.AreaWidth, area.AreaHeight);
		Settle(window);
		return new Shell(window, area, first, second, firstContent, secondContent);
	}

	private static TabItem TabOf(DockingArea area, LayoutAnchorable panel) =>
		Descendants<TabItem>(area).Single(t => ReferenceEquals(t.DataContext, panel) || t.DataContext is LayoutAnchorableItem item && ReferenceEquals(item.Model, panel));

	private static void AssertTabs(DockingArea area, LayoutAnchorable selected, LayoutAnchorable other, Palette p)
	{
		var tab = TabOf(area, selected);
		Assert.True(tab.IsSelected);
		Assert.Equal(Hex(p.Content), ColourOf(tab.Background));
		Assert.Equal(Hex(p.Primary), ColourOf(tab.Foreground));
		Assert.Equal(Accent, ColourOf(tab.BorderBrush));   // the accent line under the selected tab

		var idle = TabOf(area, other);
		Assert.False(idle.IsSelected);
		Assert.Equal(Colors.Transparent, ColourOf(idle.Background));
		Assert.Equal(Hex(p.Secondary), ColourOf(idle.Foreground));
		Assert.Equal(Colors.Transparent, ColourOf(idle.BorderBrush));   // no line under an idle tab
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheTabs_BorderAndSplitter_WearTessynsColours(bool dark)
	{
		StaTestRunner.Run(() =>
		{
			var shell = BuildShell(dark, new ModeAppearance(), out _);
			var p = PaletteOf(dark);
			shell.First.IsSelected = true;
			Settle(shell.Window);

			AssertTabs(shell.Area, shell.First, shell.Second, p);

			var content = Descendants<LayoutAnchorableControl>(shell.Area).First();
			var border = Descendants<Border>(content).First();
			Assert.Equal(Hex(p.Stroke), ColourOf(border.BorderBrush));   // the pane border

			Assert.Equal(Hex(p.Row), ColourOf(shell.Area.Manager.Background));   // the manager's own area: what shows between the panes and in the splitters
			var resizer = Descendants<LayoutGridResizerControl>(shell.Area).First();
			Assert.Equal(Colors.Transparent, ColourOf(resizer.Background));   // a splitter is the library's transparent bar: it shows the manager's area, the row colour
			shell.Window.Close();
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheAutoHideEdgeTab_AndTheSlideOut_WearTessynsColours(bool dark)
	{
		StaTestRunner.Run(() =>
		{
			var shell = BuildShell(dark, new ModeAppearance(), out _);
			var p = PaletteOf(dark);
			shell.First.ToggleAutoHide();
			Settle(shell.Window);

			var anchor = Descendants<LayoutAnchorControl>(shell.Area).First();
			var edge = (Border)VisualTreeHelper.GetChild(anchor, 0);
			Assert.Equal(Hex(p.Row), ColourOf(edge.Background));
			Assert.Equal(Hex(p.Stroke), ColourOf(edge.BorderBrush));   // the thin edge tab, idle

			var show = typeof(AvalonDock.DockingManager).GetMethod("ShowAutoHideWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
			show.Invoke(shell.Area.Manager, [anchor]);
			Settle(shell.Window);
			Assert.True(shell.Area.Chrome.SlideOut.IsOpen);
			Assert.Equal(Hex(p.Row), ColourOf(shell.Area.Manager.AutoHideWindow.Background));   // the slide-out
			shell.Area.Chrome.SlideOut.Close();
			shell.Window.Close();
		});
	}

	/// <summary>
	/// A floating window and the overlay of the drop guides are windows of their own: their lookups end at the application resources, where the mode's colour dictionary is put
	/// (and taken out again: the process-wide application outlives the test).
	/// </summary>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AFloatingWindow_AndTheDropGuides_ReadTessynsColoursFromTheApplicationResources(bool dark)
	{
		StaTestRunner.Run(() =>
		{
			var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
			var colours = new ResourceDictionary { Source = new Uri(dark ? Constants.Appearance.DarkThemeUri : Constants.Appearance.LightThemeUri, UriKind.Absolute) };
			app.Resources.MergedDictionaries.Add(colours);
			try
			{
				var shell = BuildShell(dark, new ModeAppearance(), out _);
				shell.Window.Resources.MergedDictionaries.Clear();   // nothing but the application's resources answers below
				var p = PaletteOf(dark);
				shell.Second.Float();
				Settle(shell.Window);
				var floating = Assert.Single(shell.Area.Manager.FloatingWindows);
				floating.Show();
				Settle(floating);
				Assert.Equal(Hex(p.Row), ColourOf((Brush)floating.FindResource(ResourceKeys.FloatingToolWindowBackground)));
				Assert.Equal(Hex(p.Stroke), ColourOf((Brush)floating.FindResource(ResourceKeys.FloatingToolWindowBorder)));
				floating.Close();

				var overlay = new Window();   // the drop guides
				Assert.Equal(Hex(p.Accent), ColourOf((Brush)overlay.FindResource(ResourceKeys.DockingButtonForegroundBrushKey)));
				Assert.Equal(Hex(p.Accent), ColourOf((Brush)overlay.FindResource(ResourceKeys.PreviewBoxBorderBrushKey)));
				Assert.Equal(Hex(p.Stroke), ColourOf((Brush)overlay.FindResource(ResourceKeys.DockingButtonStarBorderBrushKey)));
				Assert.Equal(Hex(p.Guide), ColourOf((Brush)overlay.FindResource(ResourceKeys.DockingButtonBackgroundBrushKey)));
				shell.Window.Close();
			}
			finally
			{
				app.Resources.MergedDictionaries.Remove(colours);
			}
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ThePin_WearsTheSecondaryTextColour(bool dark)
	{
		StaTestRunner.Run(() =>
		{
			var shell = BuildShell(dark, new ModeAppearance(), out _);
			Settle(shell.Window);
			var pin = Descendants<DockingPinButton>(shell.Area).First();
			Assert.Equal(Hex(PaletteOf(dark).Secondary), ColourOf(pin.Foreground));
			shell.Window.Close();
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheDocumentTabRow_WearsTessynsColours_WithTheAccentLine(bool dark)
	{
		StaTestRunner.Run(() =>
		{
			var shell = BuildShell(dark, new ModeAppearance(), out _);
			var p = PaletteOf(dark);
			var preview = new LayoutAnchorable { ContentId = "preview", Title = "preview", Content = new TextBlock { Text = "preview" } };
			shell.Area.DocumentPane.InsertChildAt(1, preview);   // the document pane now shows its tab row
			Settle(shell.Window);

			var tabs = Descendants<TabItem>(shell.Area).Where(t => t.Parent is DocumentPaneTabPanel || Descendants<LayoutDocumentTabItem>(t).Any()).ToList();
			Assert.NotEmpty(tabs);
			var selected = tabs.Single(t => t.IsSelected);
			Assert.Equal(Hex(p.Content), ColourOf(selected.Background));
			Assert.Equal(Hex(p.Primary), ColourOf(selected.Foreground));
			Assert.Equal(Accent, ColourOf(selected.BorderBrush));
			var idle = tabs.First(t => !t.IsSelected);
			Assert.Equal(Hex(p.Secondary), ColourOf(idle.Foreground));
			shell.Window.Close();
		});
	}

	[Fact]
	public void ADayNightSwitch_RecoloursTheChromeAtOnce_WithTheSameThemeAndNoPanelRecreated()
	{
		StaTestRunner.Run(() =>
		{
			var mode = new ModeAppearance();
			var shell = BuildShell(false, mode, out var applier);
			shell.First.IsSelected = true;
			Settle(shell.Window);
			var theme = shell.Area.Manager.Theme;
			Assert.IsType<TessynDockTheme>(theme);
			var tabs = Descendants<TabItem>(shell.Area).ToList();
			var unloaded = 0;
			shell.FirstContent.Unloaded += (_, _) => unloaded++;
			shell.SecondContent.Unloaded += (_, _) => unloaded++;
			AssertTabs(shell.Area, shell.First, shell.Second, Palette.Light);

			mode.Dark = true;
			applier.Apply(shell.Window.Resources);
			Settle(shell.Window);   // no re-layout is needed for the colours: the resources reach the open chrome themselves

			Assert.Same(theme, shell.Area.Manager.Theme);   // never replaced: nothing is re-templated
			Assert.Equal(tabs, Descendants<TabItem>(shell.Area).ToList());   // the same tab containers
			Assert.Same(shell.FirstContent, shell.First.Content);
			Assert.Same(shell.SecondContent, shell.Second.Content);
			Assert.True(shell.FirstContent.IsLoaded);   // still in the tree
			Assert.Equal(0, unloaded);   // no content unloaded by the switch
			AssertTabs(shell.Area, shell.First, shell.Second, Palette.Dark);

			mode.Dark = false;
			applier.Apply(shell.Window.Resources);
			Settle(shell.Window);
			Assert.Same(theme, shell.Area.Manager.Theme);
			Assert.Equal(0, unloaded);
			AssertTabs(shell.Area, shell.First, shell.Second, Palette.Light);
			shell.Window.Close();
		});
	}

	[Fact]
	public void TheTheme_IsSetOnceByTheArea_AndHasNoBrushesOfItsOwn()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { MinWidth = 100 });
			var theme = Assert.IsType<TessynDockTheme>(area.Manager.Theme);
			Assert.False(theme.ThemeResourceDictionary.Contains(ResourceKeys.PanelBorderBrush));   // the colours are the application's: a switch cannot leave a stale brush in the theme
			Assert.False(theme.ThemeResourceDictionary.Contains(ResourceKeys.TabBackground));
			Assert.True(theme.ThemeResourceDictionary.MergedDictionaries.Count > 0);   // the library's templates are there
		});
	}
}
