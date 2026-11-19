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

/// <summary>The main window: banner, tree, the docking area (session pane and docked preview panels) and status bar. Restores and remembers its placement and splitter.</summary>
/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(500);

	private readonly MainViewModel _viewModel;
	private readonly IClientStateStore _stateStore;
	private readonly Win32WindowPlacement _placement;
	private readonly WindowPlacementClamper _clamper;
	private readonly IShellCommands _commands;
	private readonly TitleBarViewModel _titleBar;
	private readonly SettingsHost _settings;
	private readonly IPreviewRouter? _previews;
	private bool _closeApproved;
	private double _treeWidth;
	private readonly bool _testWindow;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly DispatcherTimer _placementTimer;

	public MainWindow(MainViewModel viewModel, IClientStateStore stateStore, Win32WindowPlacement placement, WindowPlacementClamper clamper, StartupOptions options, IShellCommands commands, TitleBarViewModel titleBar, ScreensaverController screensaver, SettingsHost settings, ILogger<MainWindow> logger, DockingSurfaceHost dockingSurface, IAppearanceService appearance, IPreviewRouter? previews = null)
	{
		_previews = previews;
		_settings = settings;
		_titleBar = titleBar;
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

		Docking.SessionView.DataContext = viewModel.Session;
		dockingSurface.Register(Docking);   // the docked preview panels are placed in this docking area (FR.29.19)
		Docking.ApplyTheme(appearance.IsDark);
		appearance.Changed += (_, _) => Dispatcher.BeginInvoke(() => Docking.ApplyTheme(appearance.IsDark));
		TitleBarView.DataContext = titleBar;
		Screensaver.Covered = ContentGrid;   // the WebView2 windows cannot be painted over, so the content under the cover is hidden while it is up
		Screensaver.Attach(screensaver, logger);
		_treeWidth = JsonClientStateStore.ClampTreeWidth(stateStore.State.TreeWidth);
		titleBar.TreeVisibilityChanged += (_, _) => ApplyTreeVisibility();
		ApplyTreeVisibility();
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
		Splitter.DragCompleted += (_, _) =>
		{
			_treeWidth = TreeColumn.ActualWidth;
			_stateStore.Update(state => state.TreeWidth = _treeWidth);
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
			return;
		}

		e.Cancel = true;
		if (await _settings.ConfirmCloseAsync())
		{
			_closeApproved = true;
			Dispatcher.BeginInvoke(new Action(Close));   // not inside the Closing event
		}
	}

	/// <summary>Shows or hides the left panel (the title bar's chevron); the width it had comes back when it is shown again.</summary>
	private void ApplyTreeVisibility()
	{
		var visible = _titleBar.IsTreeVisible;
		if (!visible && TreeColumn.ActualWidth > 0)
			_treeWidth = TreeColumn.ActualWidth;

		LeftTabs.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
		Splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
		TreeColumn.MinWidth = visible ? Constants.Limits.MinTreeWidth : 0;
		TreeColumn.Width = visible ? new GridLength(_treeWidth) : new GridLength(0);
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
