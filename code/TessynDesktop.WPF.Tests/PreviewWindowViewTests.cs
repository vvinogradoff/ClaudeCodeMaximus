using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.7, FR.6.10: the preview window, the shared caption styles, the pane without its Close and the title-bar Monitor toggle on an STA thread; the surface's disposal.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewWindowViewTests
{
	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight => true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

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

	private static TitleBarViewModel TitleBarOf(FakePreviewRouter? router)
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		return new TitleBarViewModel(
			new AppearanceService(store, new FixedProbe()), store, new ShellCommands(store, NullLogger<ShellCommands>.Instance), new ImmediateUiDispatcher(), "202611.18.1000", false, router);
	}

	private static (PreviewWindow Window, PreviewWindowViewModel ViewModel, FakePreviewContentFactory Content) BuildWindow(bool pinned = false)
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		var settings = new FakePreviewSettingsService(new PreviewGroupModel { Id = "g", Name = "Group" });
		var content = new FakePreviewContentFactory();
		var viewModel = new PreviewWindowViewModel("g", "Group", pinned, settings, content, new ImmediateUiDispatcher());
		var appearance = new AppearanceApplier(new AppearanceService(store, new FixedProbe()), new ImmediateUiDispatcher());
		var window = new PreviewWindow(
			viewModel, null, settings, new ScriptedMonitorWatcher(2), new PreviewPlacementPlanner(), new Win32WindowPlacement(), appearance,
			new StartupOptions { TestWindow = true }, () => IntPtr.Zero, NullLogger.Instance);
		return (window, viewModel, content);
	}

	private static void Layout(FrameworkElement element, double width = 1000, double height = 700)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	[Fact]
	public void PreviewWindow_HasItsCaptionButtons_WithUniqueAutomationNames_AndNoWebView2BeforeContent()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow();
			using var _ = content;
			Layout((FrameworkElement)window.Content);

			var names = Descendants<ButtonBase>((DependencyObject)window.Content).Select(AutomationProperties.GetName).ToList();
			foreach (var expected in new[] { "Pin preview", "Auto-close preview", "Minimize preview", "Maximize preview", "Close preview" })
				Assert.Single(names, expected);

			Assert.Empty(Descendants<WebView2>((DependencyObject)window.Content));
			Assert.False(window.ShowInTaskbar);   // test mode (FR.29.13)
			Assert.False(window.ShowActivated);
			Assert.Equal(Constants.Limits.OffScreenCoordinate, window.Left);
			Assert.Null(window.Owner);   // its own taskbar button, not owned by the main window (FR.29.5)
			Assert.Equal("Group", window.Title);

			viewModel.Dispose();
		});
	}

	[Fact]
	public void PreviewWindow_ShowsTheFileRenderer_OrTheBrowser_OfItsViewModel_AndTheTitleFollows()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow();
			using var _ = content;
			var client = new FakeTessynClient();
			client.Files["h"] = ("note.md", "text/markdown", "# hi"u8.ToArray());
			var pane = (FilePreviewPane)window.FindName("FilePane")!;
			var browserHost = (Border)window.FindName("BrowserHost")!;

			viewModel.Show(new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "note.md" }, new PreviewTarget("h", "note.md"), client);
			Layout((FrameworkElement)window.Content);
			Assert.Equal(Visibility.Visible, pane.Visibility);
			Assert.Same(viewModel.File, pane.DataContext);
			Assert.Equal(Visibility.Collapsed, browserHost.Visibility);
			Assert.Equal("note.md - Group", window.Title);

			viewModel.Show(new PreviewItemModel { Kind = PreviewItemKind.Url, Url = "https://example.com/x" }, null, null);
			Layout((FrameworkElement)window.Content);
			Assert.Equal(Visibility.Collapsed, pane.Visibility);
			Assert.Null(pane.DataContext);
			Assert.Equal(Visibility.Visible, browserHost.Visibility);
			var browser = Assert.IsType<BrowserPreviewView>(browserHost.Child);
			Assert.Same(viewModel.Browser, browser.DataContext);
			Assert.Null(browser.Page);   // a fake surface cannot drive a WebView2: none was created
			Assert.Equal("example.com - Group", window.Title);

			viewModel.Dispose();
			Assert.Null(browserHost.Child);
		});
	}

	[Fact]
	public void PinnedPreviewWindow_ShowsPinnedState_AndHidesAutoClose()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow();
			using var _ = content;
			Layout((FrameworkElement)window.Content);
			var pin = (ToggleButton)window.FindName("PinButton")!;
			var autoClose = (ToggleButton)window.FindName("AutoCloseButton")!;
			Assert.True(pin.IsEnabled);
			Assert.False(pin.IsChecked);
			Assert.Equal(Visibility.Visible, autoClose.Visibility);

			pin.Command.Execute(null);
			Layout((FrameworkElement)window.Content);

			Assert.True(pin.IsChecked);
			Assert.False(pin.IsEnabled);   // one-way
			Assert.Equal(Constants.PreviewWindow.PinnedToolTip, pin.ToolTip);
			Assert.Equal(Visibility.Collapsed, autoClose.Visibility);
			viewModel.Dispose();
		});
	}

	[Fact]
	public void HandleClosedBeforeItWasShown_RaisesClosedNotByUser_AndDisposesTheContent()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow();
			using var _ = content;
			var handle = new PreviewWindowHandle(viewModel, window);
			var closed = new List<bool>();
			handle.Closed += (_, e) => closed.Add(e.ByUser);
			viewModel.Show(new PreviewItemModel { Kind = PreviewItemKind.Url, Url = "https://example.com/" }, null, null);

			handle.Close();
			handle.Close();

			Assert.Equal([false], closed);
			Assert.True(content.BrowserSurfaces.Single().Disposed);
			Assert.Null(handle.CurrentPlacement);
		});
	}

	[Fact]
	public void CaptionStyles_ResolveInTheMainTitleBar_AfterTheMoveToTheSharedDictionary()
	{
		StaTestRunner.Run(() =>
		{
			var bar = new TitleBar { DataContext = TitleBarOf(null) };
			Layout(bar, 1200, 40);

			foreach (var key in new[] { "CaptionButton", "ToolButton", "CloseButton" })
				Assert.IsType<Style>(bar.TryFindResource(key));

			Assert.Equal(46, ((Button)bar.FindName("MinimizeButton")!).ActualWidth);
			Assert.Equal(36, ((Button)bar.FindName("TreeToggle")!).ActualWidth);
			var shared = new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/CaptionButtons.xaml", UriKind.Relative) };
			Assert.Contains("CaptionButton", shared.Keys.Cast<object>().Select(k => k.ToString()));
		});
	}

	[Fact]
	public void FilePreviewPane_ShowCloseButton_HidesItsCloseButton()
	{
		StaTestRunner.Run(() =>
		{
			var pane = new FilePreviewPane();
			Layout(pane, 600, 400);
			var close = Descendants<Button>(pane).Single(b => AutomationProperties.GetName(b) == "Close preview");
			Assert.True(pane.ShowCloseButton);
			Assert.Equal(Visibility.Visible, close.Visibility);

			pane.ShowCloseButton = false;
			Layout(pane, 600, 400);

			Assert.Equal(Visibility.Collapsed, close.Visibility);
			Assert.Null(pane.Page);   // no WebView2 until a preview is opened
		});
	}

	[Fact]
	public void TitleBar_MonitorToggle_IsHiddenWithoutARouter()
	{
		StaTestRunner.Run(() =>
		{
			var bar = new TitleBar { DataContext = TitleBarOf(null) };
			Layout(bar, 1200, 40);

			Assert.Equal(Visibility.Collapsed, ((ToggleButton)bar.FindName("PreviewWindowsToggle")!).Visibility);
		});
	}

	[Fact]
	public void TitleBar_MonitorToggle_FollowsTheRouter_AndDrivesIt()
	{
		StaTestRunner.Run(() =>
		{
			var router = new FakePreviewRouter();
			var bar = new TitleBar { DataContext = TitleBarOf(router) };
			Layout(bar, 1200, 40);
			var toggle = (ToggleButton)bar.FindName("PreviewWindowsToggle")!;

			Assert.Equal(Visibility.Visible, toggle.Visibility);
			Assert.Equal("Preview windows", AutomationProperties.GetName(toggle));
			Assert.False(toggle.IsEnabled);
			Assert.Equal(Constants.PreviewWindow.MonitorDisabledToolTip, toggle.ToolTip);
			Assert.True(ToolTipService.GetShowOnDisabled(toggle));

			router.SetCanToggle(true);
			Layout(bar, 1200, 40);
			Assert.True(toggle.IsEnabled);
			Assert.True(toggle.IsChecked);
			Assert.Equal(Constants.PreviewWindow.MonitorOnToolTip, toggle.ToolTip);

			toggle.Command.Execute(null);
			Layout(bar, 1200, 40);
			Assert.Equal([false], router.EnabledWrites);
			Assert.False(toggle.IsChecked);
			Assert.Equal(Constants.PreviewWindow.MonitorOffToolTip, toggle.ToolTip);
		});
	}

	[Fact]
	public void FilePreviewSurface_Dispose_UnsubscribesFromTheAppearanceService()
	{
		StaTestRunner.Run(() =>
		{
			using var temp = new TempAppPaths();
			var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
			var appearance = new CountingAppearanceService();
			var options = new StartupOptions();
			var surface = new FilePreviewSurface(
				new WebViewEnvironmentProvider(temp.Paths, options), temp.Paths, new UrlPolicy(), new WebRequestPolicy(new MediaProxyUrl()), appearance,
				new ScreensaverController(store, new FolderScreensaverImages(), DateTime.UtcNow), NullLogger<FilePreviewSurface>.Instance);
			Assert.Equal(1, appearance.Subscribers);

			surface.Dispose();
			surface.Dispose();   // twice is fine

			Assert.Equal(0, appearance.Subscribers);
			appearance.Raise();   // nothing left to react
		});
	}
}

/// <summary>The title bar's Monitor toggle without WPF (FR.6.10, FR.29.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TitleBarMonitorToggleTests
{
	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight => true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private static TitleBarViewModel Build(IPreviewRouter? router)
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		return new TitleBarViewModel(
			new AppearanceService(store, new FixedProbe()), store, new ShellCommands(store, NullLogger<ShellCommands>.Instance), new ImmediateUiDispatcher(), "1", false, router);
	}

	[Fact]
	public void WithoutARouter_TheToggleIsHiddenAndDisabled()
	{
		var vm = Build(null);

		Assert.False(vm.IsMonitorToggleVisible);
		Assert.False(vm.IsMonitorToggleEnabled);
		Assert.False(vm.IsPreviewWindowsOn);
		Assert.False(vm.TogglePreviewWindowsCommand.CanExecute(null));
	}

	[Fact]
	public void WithOneMonitor_TheToggleIsDisabled_WithTheDocumentedTooltip()
	{
		var vm = Build(new FakePreviewRouter());

		Assert.True(vm.IsMonitorToggleVisible);
		Assert.False(vm.IsMonitorToggleEnabled);
		Assert.False(vm.IsPreviewWindowsOn);
		Assert.Equal("Connect a second monitor to open previews in their own windows", vm.MonitorToggleToolTip);
		Assert.False(vm.TogglePreviewWindowsCommand.CanExecute(null));
	}

	[Fact]
	public void WithTwoMonitors_TheToggleTurnsTheModeOnAndOff_AndTheTooltipFollows()
	{
		var router = new FakePreviewRouter();
		var vm = Build(router);
		var changed = new List<string?>();
		vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		router.SetCanToggle(true);

		Assert.True(vm.IsMonitorToggleEnabled);
		Assert.True(vm.IsPreviewWindowsOn);
		Assert.Equal("Previews open in their own windows. Click to show them beside the session.", vm.MonitorToggleToolTip);
		Assert.Contains(nameof(TitleBarViewModel.IsPreviewWindowsOn), changed);
		Assert.True(vm.TogglePreviewWindowsCommand.CanExecute(null));

		vm.TogglePreviewWindowsCommand.Execute(null);

		Assert.Equal([false], router.EnabledWrites);
		Assert.False(vm.IsPreviewWindowsOn);
		Assert.Equal("Previews open beside the session. Click to open them in their own windows.", vm.MonitorToggleToolTip);

		vm.TogglePreviewWindowsCommand.Execute(null);
		Assert.Equal([false, true], router.EnabledWrites);
		Assert.True(vm.IsPreviewWindowsOn);
	}

	[Fact]
	public void LosingTheSecondMonitor_DisablesTheToggle()
	{
		var router = new FakePreviewRouter();
		router.SetCanToggle(true);
		var vm = Build(router);
		Assert.True(vm.TogglePreviewWindowsCommand.CanExecute(null));

		router.SetCanToggle(false);

		Assert.False(vm.IsMonitorToggleEnabled);
		Assert.False(vm.IsPreviewWindowsOn);
		Assert.False(vm.TogglePreviewWindowsCommand.CanExecute(null));
		Assert.Equal(Constants.PreviewWindow.MonitorDisabledToolTip, vm.MonitorToggleToolTip);
	}
}
