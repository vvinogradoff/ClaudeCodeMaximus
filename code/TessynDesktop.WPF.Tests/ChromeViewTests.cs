using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The W10a views on an STA thread: title bar, status bar items, screensaver cover and the theme dictionaries.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ChromeViewTests
{
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

	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight { get; set; } = true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private sealed class Rig
	{
		public Rig()
		{
			Store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
			Appearance = new AppearanceService(Store, new FixedProbe());
			Commands = new ShellCommands(Store, NullLogger<ShellCommands>.Instance);
		}

		public ClientSettingsStore Store { get; }

		public AppearanceService Appearance { get; }

		public ShellCommands Commands { get; }

		public TitleBarViewModel TitleBar(bool buildOutput = false) => new(Appearance, Store, Commands, new ImmediateUiDispatcher(), "202611.16.0930", buildOutput);
	}

	private static Color ColorOf(ResourceDictionary resources, string key) => ((SolidColorBrush)resources[key]).Color;

	[Fact]
	public void TitleBar_BindsWithoutErrors_AndItsButtonsDriveTheViewModel()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var rig = new Rig();
			var vm = rig.TitleBar(buildOutput: true);
			var bar = new TitleBar { DataContext = vm };
			bar.Measure(new Size(1200, 40));
			bar.Arrange(new Rect(0, 0, 1200, 40));
			bar.UpdateLayout();

			var tree = (Button)bar.FindName("TreeToggle")!;
			var theme = (Button)bar.FindName("ThemeToggle")!;
			Assert.Equal("202611.16.0930", ((TextBlock)bar.FindName("BuildNumberText")!).Text);
			Assert.True(tree.Command.CanExecute(null));

			tree.Command.Execute(null);
			Assert.False(vm.IsTreeVisible);
			Assert.True(rig.Store.Get<ShellLayoutSettings>().TreeCollapsed);

			theme.Command.Execute(null);
			Assert.True(vm.IsDark);
			Assert.Equal(ClientTheme.Dark, rig.Appearance.Chosen);
			PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void TitleBar_HasTheChevronMenuThemeAndWindowButtons_WithAccessibleNames()
	{
		StaTestRunner.Run(() =>
		{
			var bar = new TitleBar { DataContext = new Rig().TitleBar() };
			bar.Measure(new Size(1200, 40));
			bar.Arrange(new Rect(0, 0, 1200, 40));

			foreach (var (name, automation) in new[]
			{
				("TreeToggle", Constants.TitleBar.TreeToggleAutomationName), ("MenuButton", Constants.TitleBar.MenuAutomationName), ("ThemeToggle", Constants.TitleBar.ThemeToggleAutomationName),
				("MinimizeButton", Constants.TitleBar.MinimizeAutomationName), ("MaximizeButton", Constants.TitleBar.MaximizeAutomationName), ("CloseButton", Constants.TitleBar.CloseAutomationName),
			})
			{
				var button = Assert.IsType<Button>(bar.FindName(name));
				Assert.Equal(automation, System.Windows.Automation.AutomationProperties.GetName(button));
			}
		});
	}

	[Fact]
	public void TitleBar_ShowsTheBuildOutputWarningOnlyWhenRunningFromBuildOutput()
	{
		StaTestRunner.Run(() =>
		{
			foreach (var buildOutput in new[] { true, false })
			{
				var bar = new TitleBar { DataContext = new Rig().TitleBar(buildOutput) };
				bar.Measure(new Size(1200, 40));
				bar.Arrange(new Rect(0, 0, 1200, 40));
				bar.UpdateLayout();
				var warning = LogicalTree(bar).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Running from build output");
				Assert.Equal(buildOutput ? Visibility.Visible : Visibility.Collapsed, warning.Visibility);
			}
		});
	}

	private static IEnumerable<DependencyObject> LogicalTree(DependencyObject root)
	{
		yield return root;
		foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
			foreach (var nested in LogicalTree(child))
				yield return nested;
	}

	[Fact]
	public void TitleBarViewModel_RestoresTheCollapsedPanel_AndRaisesTheChange()
	{
		var rig = new Rig();
		rig.Store.Update<ShellLayoutSettings>(s => s.TreeCollapsed = true);
		var vm = rig.TitleBar();
		var raised = 0;
		vm.TreeVisibilityChanged += (_, _) => raised++;

		Assert.False(vm.IsTreeVisible);

		vm.SetTreeVisible(true);
		vm.SetTreeVisible(true);   // no change, no event

		Assert.Equal(1, raised);
		Assert.False(rig.Store.Get<ShellLayoutSettings>().TreeCollapsed);
	}

	[Fact]
	public void ThemeDictionaries_SwapOnTheToggle_KeepingExactlyOne_AndSetTheInputFontSize()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig();
			var applier = new AppearanceApplier(rig.Appearance, new ImmediateUiDispatcher());
			var resources = new ResourceDictionary();

			applier.Apply(resources);
			Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), ColorOf(resources, "TitleBarBackgroundBrush"));
			Assert.Equal(13.0, resources[Constants.Appearance.InputFontSizeKey]);

			rig.Appearance.ToggleTheme();
			rig.Appearance.AdjustFont(FontTarget.Input, +3);
			applier.Apply(resources);

			Assert.Equal(Color.FromRgb(0x2B, 0x2B, 0x2B), ColorOf(resources, "TitleBarBackgroundBrush"));
			Assert.Single(resources.MergedDictionaries);
			Assert.Equal(16.0, resources[Constants.Appearance.InputFontSizeKey]);

			rig.Appearance.ToggleTheme();
			applier.Apply(resources);
			Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), ColorOf(resources, "TitleBarBackgroundBrush"));
			Assert.Single(resources.MergedDictionaries);
		});
	}

	[Fact]
	public void ThemeDictionaries_DefineTheSameKeys()
	{
		StaTestRunner.Run(() =>
		{
			var light = new ResourceDictionary { Source = new Uri(Constants.Appearance.LightThemeUri) };
			var dark = new ResourceDictionary { Source = new Uri(Constants.Appearance.DarkThemeUri) };

			Assert.Equal(light.Keys.Cast<object>().Select(k => k.ToString()).Order(), dark.Keys.Cast<object>().Select(k => k.ToString()).Order());
			Assert.NotEmpty(light.Keys);
		});
	}

	[Fact]
	public void StatusBarItems_ShowTheMetersWithTheirValues_AndHideThemWithoutUsage()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var vm = NewStatusBar();
			var view = new StatusBarItems { DataContext = vm };
			view.Measure(new Size(900, 40));
			view.Arrange(new Rect(0, 0, 900, 40));
			view.UpdateLayout();   // attaches the bindings of a view that is not in a window

			Assert.True(Visibility.Collapsed == ((FrameworkElement)view.FindName("Meters")!).Visibility, string.Join(" | ", listener.Messages));

			vm.HasUsage = true;
			vm.FiveHourPercent = 42.0;
			vm.ModelText = "claude-opus-5-5";
			view.UpdateLayout();

			Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("Meters")!).Visibility);
			Assert.Equal(42.0, ((ProgressBar)view.FindName("FiveHourMeter")!).Value);
			Assert.Equal("claude-opus-5-5", ((TextBlock)view.FindName("ModelLine")!).Text);
			PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
		});

		Assert.Empty(listener.Messages);
	}

	private static StatusBarViewModel NewStatusBar()
	{
		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance);
		var state = new MemoryClientState();
		var main = new MainViewModel(connection, _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), state, NullLogger<MainViewModel>.Instance);
		return new StatusBarViewModel(connection, main, ui, NullLogger<StatusBarViewModel>.Instance);
	}

	[Fact]
	public void ScreensaverCover_ShowsTheImage_ReleasesTheFile_AndHidesOnInput()
	{
		var dir = Path.Combine(Path.GetTempPath(), "tessyn-cover-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			var file = Path.Combine(dir, "a.png");
			WritePng(file);

			StaTestRunner.Run(() =>
			{
				var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
				store.Update<ScreensaverSettings>(s =>
				{
					s.Directory = dir;
					s.TimeoutSeconds = 5;
				});
				var now = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
				var controller = new ScreensaverController(store, new FolderScreensaverImages(), now);
				var cover = new ScreensaverOverlay();
				cover.Attach(controller, clock: () => now);

				Assert.Equal(Visibility.Collapsed, cover.Visibility);

				controller.Tick(now.AddSeconds(5));
				cover.Refresh();

				Assert.Equal(Visibility.Visible, cover.Visibility);
				var picture = (Image)cover.FindName("Picture")!;
				Assert.NotNull(picture.Source);
				File.Delete(file);   // decoded on load: the cover holds no handle on the file
				Assert.False(File.Exists(file));

				controller.NoteInput(now.AddSeconds(6));
				cover.Refresh();

				Assert.Equal(Visibility.Collapsed, cover.Visibility);
				Assert.Null(picture.Source);
			});
		}
		finally
		{
			TestDirectory.Delete(dir);
		}
	}

	private static void WritePng(string path)
	{
		var pixels = new byte[4 * 4 * 4];
		var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
		var encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(source));
		using var stream = File.Create(path);
		encoder.Save(stream);
	}
}
