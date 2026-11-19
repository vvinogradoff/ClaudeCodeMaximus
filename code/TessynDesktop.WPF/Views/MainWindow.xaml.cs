using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The main window: banner, the docking area (the whole shell layout: Sessions, Recent, Tags, the session pane, Focus and the docked preview panels) and status bar. Restores and remembers its placement.</summary>
/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(500);

	private readonly MainViewModel _viewModel;
	private readonly IClientStateStore _stateStore;
	private readonly Win32WindowPlacement _placement;
	private readonly WindowPlacementClamper _clamper;
	private readonly IShellCommands _commands;
	private readonly SettingsHost _settings;
	private readonly IPreviewRouter? _previews;
	private bool _closeApproved;
	private readonly bool _testWindow;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly DispatcherTimer _placementTimer;

	public MainWindow(MainViewModel viewModel, IClientStateStore stateStore, Win32WindowPlacement placement, WindowPlacementClamper clamper, StartupOptions options, IShellCommands commands, TitleBarViewModel titleBar, ScreensaverController screensaver, SettingsHost settings, ILogger<MainWindow> logger, DockingSurfaceHost dockingSurface, IAppearanceService appearance, ShellLayoutMemory layoutMemory, ShellLayoutHost layoutHost, IShellSlots slots, IPreviewRouter? previews = null)
	{
		_previews = previews;
		_settings = settings;
		_commands = commands;
		_testWindow = options.TestWindow;
		_viewModel = viewModel;
		_stateStore = stateStore;
		_placement = placement;
		_clamper = clamper;
		DataContext = viewModel;
		InitializeComponent();
		if (_testWindow)
			PrepareTestWindow();

		Docking.SessionView.SetBinding(DataContextProperty, new System.Windows.Data.Binding(nameof(MainViewModel.Session)) { Source = viewModel });   // the session pane view model exists once the client is connected
		dockingSurface.Register(Docking);   // the docked preview panels are placed in this docking area (FR.29.19)
		Docking.BuildShell(new ShellPanelCatalog().Create(viewModel, slots, logger), layoutMemory, logger, () => placement.GetMonitors());   // the whole shell layout, saved or default (FR.6.11, FR.6.18)
		layoutHost.Register(Docking);   // the title bar's chevron and the Panels menu drive it through IShellLayout (FR.6.14, FR.6.15)
		Docking.ApplyTheme(appearance.IsDark);   // the one place the theme is first set
		appearance.Changed += (_, _) => Dispatcher.BeginInvoke(() => Docking.ApplyTheme(appearance.IsDark));
		TitleBarView.DataContext = titleBar;
		Screensaver.Covered = ContentGrid;   // the WebView2 windows cannot be painted over, so the content under the cover is hidden while it is up
		Screensaver.CoverChanged += (_, covered) => Docking.SetFloatingVisible(!covered);   // the floating panels are windows of their own: the cover does not reach them
		Screensaver.Attach(screensaver, logger);
		_placementTimer = new DispatcherTimer { Interval = PlacementSaveDelay };
		_placementTimer.Tick += (_, _) =>
		{
			_placementTimer.Stop();
			CapturePlacement();
		};

		SourceInitialized += OnSourceInitialized;
		Loaded += OnLoaded;
		LocationChanged += (_, _) => RestartPlacementTimer();
		SizeChanged += (_, _) => RestartPlacementTimer();
		StateChanged += (_, _) =>
		{
			RestartPlacementTimer();
			FitMaximized();
		};
		DpiChanged += (_, _) => FitMaximized();   // dragged to a monitor with another scale while maximised
		Closing += OnClosing;
		Closed += (_, _) =>
		{
			_previews?.CloseAllForExit();   // the app really closes now (the Settings guard allowed it): the preview windows go with it (FR.29.11)
			_lifetime.Cancel();
		};
	}

	/// <summary>
	/// FR.4.7: an open Settings window with unsaved changes asks first. The close is held back while it asks; Save or Discard lets it through (the main window closes again), Cancel keeps
	/// both windows open.
	/// </summary>
	private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		if (_closeApproved || !_settings.HasUnsavedChanges)
		{
			CapturePlacement();
			Docking.FlushLayout();   // a layout change that still waits for its 500 ms is saved now (FR.6.18)
			return;
		}

		e.Cancel = true;
		if (await _settings.ConfirmCloseAsync())
		{
			_closeApproved = true;
			Dispatcher.BeginInvoke(new Action(Close));   // not inside the Closing event
		}
	}

	/// <summary>A maximized window with a custom frame reaches over the screen edge by the resize border; pull the content back in.</summary>
	private void FitMaximized() => Root.Margin = WindowState == WindowState.Maximized ? FrameMetrics.MaximizedMargin(VisualTreeHelper.GetDpi(this).DpiScaleX) : new Thickness(0);

	/// <summary>Test mode: shown but never activated, off every monitor, hidden from the taskbar.</summary>
	private void PrepareTestWindow()
	{
		ShowActivated = false;
		ShowInTaskbar = false;
		WindowStartupLocation = WindowStartupLocation.Manual;
		Left = Constants.Limits.OffScreenCoordinate;
		Top = Constants.Limits.OffScreenCoordinate;
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		if (_testWindow)
		{
			// Never take the user's foreground or keyboard focus, also when WebView2 activates the window later.
			TestWindowFocusGuard.Apply(new WindowInteropHelper(this).Handle);
			return;
		}

		var (monitors, primary) = _placement.GetMonitors();
		var clamped = _clamper.Clamp(_stateStore.State.Window, monitors, primary);
		if (clamped != null)
			_placement.Apply(new WindowInteropHelper(this).Handle, clamped);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		_ = _viewModel.StartAsync(_lifetime.Token);
	}

	/// <summary>Runs the command bound to the pressed key combination, if any (a registered command's gesture, with the user's overrides).</summary>
	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		base.OnPreviewKeyDown(e);
		if (e.Handled)
			return;

		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (_commands.TryExecuteGesture(key, Keyboard.Modifiers, ShellKeyPolicy.IsTextInput(Keyboard.FocusedElement), e.IsRepeat, Keyboard.FocusedElement))
			e.Handled = true;
	}

	private void RestartPlacementTimer()
	{
		_placementTimer.Stop();
		_placementTimer.Start();
	}

	private void CapturePlacement()
	{
		if (_testWindow)
			return;

		if (WindowState == WindowState.Minimized)
			return;

		var captured = _placement.Capture(new WindowInteropHelper(this).Handle);
		if (captured != null)
			_stateStore.Update(state => state.Window = captured);
	}
}
