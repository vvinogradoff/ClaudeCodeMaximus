using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// A preview window (FR.29.5–FR.29.8): its own top-level window with a custom title bar (Pin, Auto-close, Minimize, Maximize/Restore, Close) and the content of its
/// <see cref="PreviewWindowViewModel"/>: a <see cref="FilePreviewPane"/> or a <see cref="BrowserPreviewView"/>. A group window opens at the group's remembered
/// placement and remembers its moves 500 ms after the last change; a pinned window never writes the group's placement but tells the router (<see cref="PreviewPlacementTracker"/>). It is shown and raised without taking
/// the keyboard focus. In test mode it is off-screen, not in the taskbar and never activated (FR.29.13).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class PreviewWindow : Window
{
	private const int SwShowNoActivate = 4;
	private const uint SwpNoSize = 0x0001;
	private const uint SwpNoMove = 0x0002;
	private const uint SwpNoActivate = 0x0010;

	private readonly PreviewWindowViewModel _viewModel;
	private readonly IPreviewSettingsService _settings;
	private readonly IMonitorWatcher _monitors;
	private readonly PreviewPlacementPlanner _planner;
	private readonly Win32WindowPlacement _placement;
	private readonly AppearanceApplier _appearance;
	private readonly Func<IntPtr> _mainWindow;
	private readonly PreviewPlacementModel? _ownPlacement;
	private readonly ILogger _logger;
	private readonly bool _testWindow;
	private readonly PreviewPlacementTracker _tracker;
	private BrowserPreviewView? _browserView;

	public PreviewWindow(
		PreviewWindowViewModel viewModel,
		PreviewPlacementModel? ownPlacement,
		IPreviewSettingsService settings,
		IMonitorWatcher monitors,
		PreviewPlacementPlanner planner,
		Win32WindowPlacement placement,
		AppearanceApplier appearance,
		StartupOptions options,
		Func<IntPtr> mainWindow,
		ILogger logger,
		IUiDispatcher ui,
		TimeProvider? time = null,
		TimeSpan? placementDelay = null)
	{
		_viewModel = viewModel;
		_ownPlacement = ownPlacement;
		_settings = settings;
		_monitors = monitors;
		_planner = planner;
		_placement = placement;
		_appearance = appearance;
		_mainWindow = mainWindow;
		_logger = logger;
		_testWindow = options.TestWindow;
		DataContext = viewModel;
		InitializeComponent();
		WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(Constants.PreviewWindow.ResizeBorder);   // one source for the chrome and the content inset (FR.29.15)
		FitMaximized();
		if (_testWindow)
			PrepareTestWindow();

		_appearance.ApplyToWindow(this);   // FR.9.14: styled for the stored theme before it is shown ...
		ContentRendered += (_, _) =>
		{
			_appearance.ApplyToWindow(this);   // ... and again once its first frame is up
			_tracker.Tracking = true;
		};

		_tracker = new PreviewPlacementTracker(
			viewModel.GroupId, settings, monitors, ui, () => viewModel.IsPinned, () => WindowState == WindowState.Minimized, CurrentPlacement, _testWindow, time, placementDelay);
		SourceInitialized += OnSourceInitialized;
		LocationChanged += (_, _) => _tracker.Changed();
		SizeChanged += (_, _) => _tracker.Changed();
		StateChanged += (_, _) =>
		{
			_tracker.Changed();
			ApplyWindowState();
		};
		DpiChanged += (_, _) => FitMaximized();
		Closing += (_, _) => _tracker.Flush();
		Closed += (_, _) => _tracker.Dispose();
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
		ApplyContent();
	}

	/// <summary>A pinned window settled at a new place (500 ms after its last move, resize or state change).</summary>
	public event EventHandler? PinnedPlacementChanged
	{
		add => _tracker.PinnedPlacementChanged += value;
		remove => _tracker.PinnedPlacementChanged -= value;
	}

	/// <summary>The window's placement now, remembered the way a group window remembers it; null before it has a window handle, when it cannot be read, and in test mode (FR.29.13).</summary>
	public PreviewPlacementModel? CurrentPlacement()
	{
		if (_testWindow)
			return null;

		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero)
			return null;

		var captured = _placement.Capture(hwnd);
		if (captured == null)
			return null;

		var device = _monitors.MonitorOf(hwnd);
		return _planner.Remember(captured, device == null ? null : _monitors.Monitors.FirstOrDefault(m => m.DeviceName == device));
	}

	/// <summary>Restores the window when it is minimized and brings it to the top, without activating it (FR.29.5).</summary>
	public void RaiseWithoutActivation()
	{
		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero)
			return;

		if (WindowState == WindowState.Minimized)
			ShowWindow(hwnd, SwShowNoActivate);

		SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);   // HWND_TOP
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(PreviewWindowViewModel.File) or nameof(PreviewWindowViewModel.Browser))
			ApplyContent();
	}

	/// <summary>Shows the pane of the content the view model has: a file renderer or a browser (a new one for every browser content, so it never inherits a disposed WebView2).</summary>
	private void ApplyContent()
	{
		FilePane.DataContext = _viewModel.File;
		FilePane.Visibility = _viewModel.File == null ? Visibility.Collapsed : Visibility.Visible;
		if (_viewModel.Browser == null)
		{
			BrowserHost.Child = null;
			_browserView = null;
			BrowserHost.Visibility = Visibility.Collapsed;
			return;
		}

		if (_browserView != null && ReferenceEquals(_browserView.DataContext, _viewModel.Browser))
			return;

		_browserView = new BrowserPreviewView { DataContext = _viewModel.Browser };
		BrowserHost.Child = _browserView;
		BrowserHost.Visibility = Visibility.Visible;
		if (_viewModel.BrowserContent?.Surface is BrowserSurface surface)
			_ = _browserView.AttachAsync(surface, _logger);
	}

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
		var hwnd = new WindowInteropHelper(this).Handle;
		if (_testWindow)
		{
			TestWindowFocusGuard.Apply(hwnd);   // never take the user's foreground or keyboard focus
			return;
		}

		var remembered = _viewModel.IsPinned ? _ownPlacement : _settings.GetPlacement(_viewModel.GroupId);
		var planned = _planner.Plan(remembered, _monitors.Monitors, _monitors.MonitorOf(_mainWindow()));
		_placement.Apply(hwnd, planned);
	}

	/// <summary>Called when the window state changes (and by tests, since an unshown window raises no state change). A maximized window with a custom frame reaches over the screen edge by the resize border; pull the content back in. The maximize button shows "restore" while maximized.</summary>
	public void ApplyWindowState()
	{
		FitMaximized();
		var maximized = WindowState == WindowState.Maximized;
		MaximizeGlyph.Text = maximized ? Constants.PreviewWindow.RestoreGlyph : Constants.PreviewWindow.MaximizeGlyph;
		var name = maximized ? Constants.PreviewWindow.RestoreAutomationName : Constants.PreviewWindow.MaximizeAutomationName;
		MaximizeButton.ToolTip = maximized ? Constants.PreviewWindow.RestoreToolTip : Constants.PreviewWindow.MaximizeToolTip;
		AutomationProperties.SetName(MaximizeButton, name);
	}

	private void FitMaximized()
	{
		var maximized = WindowState == WindowState.Maximized;
		Root.Margin = maximized ? FrameMetrics.MaximizedMargin(VisualTreeHelper.GetDpi(this).DpiScaleX) : new Thickness(0);
		ContentHost.Margin = maximized ? new Thickness(0) : ResizeInset();   // FR.29.15: a maximized window has no border
	}

	/// <summary>The resize border the content must not cover: left, right and bottom (the top is the title bar).</summary>
	private static Thickness ResizeInset() => new(Constants.PreviewWindow.ResizeBorder, 0, Constants.PreviewWindow.ResizeBorder, Constants.PreviewWindow.ResizeBorder);

	private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

	private void OnMaximizeClick(object sender, RoutedEventArgs e)
	{
		if (WindowState == WindowState.Maximized)
			SystemCommands.RestoreWindow(this);
		else
			SystemCommands.MaximizeWindow(this);
	}

	private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
