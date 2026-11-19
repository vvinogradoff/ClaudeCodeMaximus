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
			new AppearanceService(store, new FixedProbe()), store, new ShellCommands(store, NullLogger<ShellCommands>.Instance), new FakeShellLayout(), new ImmediateUiDispatcher(), "202611.18.1000", false, router);
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
			new StartupOptions { TestWindow = true }, () => IntPtr.Zero, NullLogger.Instance, new ImmediateUiDispatcher());
		return (window, viewModel, content);
	}

	/// <summary>Shows a file and pumps the dispatcher until its load is done: the load's continuations touch the live bindings, as in the app.</summary>
	private static void PumpUntilLoaded(PreviewWindowViewModel viewModel, PreviewItemModel item, PreviewTarget target, FakeTessynClient client)
	{
		var task = Task.CompletedTask;
		System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() =>
		{
			viewModel.Show(item, target, client);
			task = viewModel.File!.WhenIdleAsync();
		});
		var frame = new System.Windows.Threading.DispatcherFrame();
		task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
		System.Windows.Threading.Dispatcher.PushFrame(frame);
		task.GetAwaiter().GetResult();
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
	public void PreviewWindow_InTestMode_ReportsNoPlacement_EvenOnceItHasAHandle()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow(pinned: true);
			using var _ = content;
			new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();   // a handle without showing the window

			Assert.Null(window.CurrentPlacement());   // FR.29.13: the off-screen test rectangle is never stored

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
			var host = (PreviewContentHost)window.FindName("ContentView")!;   // the content shared with the docked panels (FR.29.18)
			var pane = host.FilePane;
			var browserHost = host.BrowserHost;

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

			pin.IsChecked = true;   // what a click and the Toggle pattern of UI Automation both do: the two-way binding pins
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

	/// <summary>TC.PW.68 (FR.29.15): the content keeps off the resize border on the left, right and bottom while the window is normal, and fills the window when it is maximized.</summary>
	[Fact]
	public void PreviewWindow_ContentHost_LeavesTheResizeBorderFree_UnlessMaximized()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow();
			using var _ = content;
			var host = (Grid)window.FindName("ContentHost")!;
			var border = Constants.PreviewWindow.ResizeBorder;

			Assert.Equal(new Thickness(border, 0, border, border), host.Margin);
			Assert.Equal(new Thickness(border), System.Windows.Shell.WindowChrome.GetWindowChrome(window).ResizeBorderThickness);

			window.WindowState = WindowState.Maximized;
			window.ApplyWindowState();   // an unshown window raises no StateChanged
			Assert.Equal(new Thickness(0), host.Margin);

			window.WindowState = WindowState.Normal;
			window.ApplyWindowState();
			Assert.Equal(new Thickness(border, 0, border, border), host.Margin);
			viewModel.Dispose();
		});
	}

	/// <summary>TC.PW.70 (FR.29.14): the window's pane has no header row, and Source and Open in default app are in the title bar, left of the title, only for a file.</summary>
	[Fact]
	public void PreviewWindow_HasNoPaneHeader_AndTheFileControlsLeftOfTheTitle()
	{
		StaTestRunner.Run(() =>
		{
			var (window, viewModel, content) = BuildWindow();
			using var _ = content;
			var pane = ((PreviewContentHost)window.FindName("ContentView")!).FilePane;
			var source = (ToggleButton)window.FindName("SourceButton")!;
			var open = (Button)window.FindName("OpenExternalButton")!;
			var title = (TextBlock)window.FindName("TitleText")!;
			var client = new FakeTessynClient();
			client.Files["h"] = ("note.md", "text/markdown", "# hi"u8.ToArray());

			Assert.False(pane.ShowHeader);
			Assert.Equal(Visibility.Collapsed, ((FrameworkElement)pane.FindName("HeaderRow")!).Visibility);
			Assert.Equal("Show source", AutomationProperties.GetName(source));
			Assert.Equal("Open in default app", AutomationProperties.GetName(open));
			Assert.Equal(Visibility.Collapsed, source.Visibility);   // no file yet
			Assert.Equal(Visibility.Collapsed, open.Visibility);

			client.Files["t"] = ("note.txt", "text/plain", "plain"u8.ToArray());
			client.Files["c"] = ("main.cpp", "text/plain", "int main() {}"u8.ToArray());

			// Source for markdown only; Open in default app for the types on the FR.23.18 list; each shown, not just not collapsed
			foreach (var (handle, name, sourceShown, openShown) in new[] { ("h", "note.md", true, true), ("t", "note.txt", false, true), ("c", "main.cpp", false, false) })
			{
				PumpUntilLoaded(viewModel, new PreviewItemModel { Kind = PreviewItemKind.File, FileName = name }, new PreviewTarget(handle, name), client);
				Layout((FrameworkElement)window.Content);
				Assert.Equal(sourceShown ? Visibility.Visible : Visibility.Collapsed, source.Visibility);
				Assert.Equal(openShown ? Visibility.Visible : Visibility.Collapsed, open.Visibility);
			}

			viewModel.Show(new PreviewItemModel { Kind = PreviewItemKind.Url, Url = "https://example.com/x" }, null, null);
			Layout((FrameworkElement)window.Content);
			Assert.Equal(Visibility.Collapsed, source.Visibility);   // a web page keeps its browser bar only
			Assert.Equal(Visibility.Collapsed, open.Visibility);

			var controls = (FrameworkElement)source.Parent;
			Assert.True(Grid.GetColumn(controls) < Grid.GetColumn(title));   // left of the title
			Assert.Same(controls, open.Parent);
			viewModel.Dispose();
		});
	}

	[Fact]
	public void FilePreviewPane_ShowHeader_False_CollapsesTheHeaderRow_AndTheSidePaneKeepsIt()
	{
		StaTestRunner.Run(() =>
		{
			var pane = new FilePreviewPane();
			Layout(pane, 600, 400);
			var header = (FrameworkElement)pane.FindName("HeaderRow")!;
			Assert.True(pane.ShowHeader);
			Assert.Equal(Visibility.Visible, header.Visibility);

			pane.ShowHeader = false;
			Assert.Equal(Visibility.Collapsed, header.Visibility);
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

			toggle.IsChecked = false;   // what a click and the Toggle pattern of UI Automation both do: the two-way binding drives the router
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
			new AppearanceService(store, new FixedProbe()), store, new ShellCommands(store, NullLogger<ShellCommands>.Instance), new FakeShellLayout(), new ImmediateUiDispatcher(), "1", false, router);
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
