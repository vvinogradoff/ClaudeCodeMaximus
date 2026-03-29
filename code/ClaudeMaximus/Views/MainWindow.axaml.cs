using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private bool _closeConfirmed;
	private PixelPoint? _pendingPosition;
	private bool _pendingMaximized;

	// Track normal-state bounds because Avalonia reports Position=(0,0) when maximized
	private PixelPoint _lastNormalPosition;
	private double _lastNormalWidth;
	private double _lastNormalHeight;

	public MainWindow()
	{
		InitializeComponent();
		BuildNumberText.Text = GetBuildNumber();

		// Global hotkey handler
		KeyDown += OnGlobalKeyDown;
		// On macOS, SystemDecorations="BorderOnly" (set in AXAML) does not provide
		// native resize handles. Switch to Full decorations with the client area
		// extended into the title bar so the custom title bar still renders, while
		// the native window frame supplies resize cursors at all edges.
		if (OperatingSystem.IsMacOS())
		{
			SystemDecorations = SystemDecorations.Full;
			ExtendClientAreaToDecorationsHint = true;
			ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
			ExtendClientAreaTitleBarHeightHint = -1;
		}

		var ws = App.Services.GetRequiredService<IAppSettingsService>().Settings.Window;

		Width  = ws.Width;
		Height = ws.Height;
		_pendingMaximized    = ws.IsMaximized;
		_pendingPosition     = new PixelPoint((int)ws.Left, (int)ws.Top);
		_lastNormalPosition  = _pendingPosition.Value;
		_lastNormalWidth     = ws.Width;
		_lastNormalHeight    = ws.Height;

		MainContentGrid.ColumnDefinitions[0].Width = new GridLength(
			Math.Clamp(ws.SplitterPosition, 180, 600));

		PositionChanged += OnPositionChanged;
		Opened += OnWindowOpened;
	}

	private void OnPositionChanged(object? sender, PixelPointEventArgs e)
	{
		if (WindowState == WindowState.Normal)
		{
			_lastNormalPosition = e.Point;
			_lastNormalWidth    = Width;
			_lastNormalHeight   = Height;
		}
	}

	private void OnWindowOpened(object? sender, EventArgs e)
	{
		Opened -= OnWindowOpened;

		Log.Information("WindowOpened: pending=({PosX},{PosY}), pendingMaximized={Max}, " +
			"current Position=({CurX},{CurY}), WindowState={State}",
			_pendingPosition?.X, _pendingPosition?.Y, _pendingMaximized,
			Position.X, Position.Y, WindowState);

		// Validate and apply position now that the window handle exists
		// (Screens.All requires a handle, so this cannot be done in the constructor)
		if (_pendingPosition.HasValue)
		{
			var validated = ValidatePosition(
				_pendingPosition.Value.X, _pendingPosition.Value.Y, Width, Height);
			Log.Information("WindowOpened: validated=({VX},{VY}) from saved=({SX},{SY})",
				validated.X, validated.Y, _pendingPosition.Value.X, _pendingPosition.Value.Y);
			Position = validated;
			_lastNormalPosition = validated;
			_pendingPosition = null;
		}

		Log.Information("WindowOpened: Position after set = ({X},{Y})", Position.X, Position.Y);

		// Restore maximized state after position is set so it maximizes on the correct screen
		if (_pendingMaximized)
		{
			WindowState = WindowState.Maximized;
			Log.Information("WindowOpened: maximized, Position now = ({X},{Y})", Position.X, Position.Y);
		}

		// Restore active session selection now that the tree UI is ready
		if (DataContext is MainWindowViewModel vm)
			vm.RestoreActiveSession();
	}

	/// <summary>
	/// Checks if the saved window center lands on any connected screen.
	/// If not, falls back to primary screen center.
	/// </summary>
	private PixelPoint ValidatePosition(int left, int top, double width, double height)
	{
		var centerX = left + (int)(width / 2);
		var centerY = top + (int)(height / 2);

		Log.Information("ValidatePosition: saved=({Left},{Top}), size=({W}x{H}), center=({CX},{CY}), screenCount={Count}",
			left, top, width, height, centerX, centerY, Screens.All.Count);

		foreach (var screen in Screens.All)
		{
			Log.Information("ValidatePosition: screen bounds={Bounds}, isPrimary={Primary}",
				screen.Bounds, screen.IsPrimary);

			if (screen.Bounds.Contains(new PixelPoint(centerX, centerY)))
			{
				Log.Information("ValidatePosition: center lands on this screen, returning ({Left},{Top})", left, top);
				return new PixelPoint(left, top);
			}
		}

		// Saved position is off-screen — center on primary
		var primary = Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
		var x = primary.X + (primary.Width - (int)width) / 2;
		var y = primary.Y + (primary.Height - (int)height) / 2;
		Log.Warning("ValidatePosition: center is off-screen, falling back to primary center ({X},{Y})", x, y);
		return new PixelPoint(x, y);
	}

	protected override async void OnClosing(WindowClosingEventArgs e)
	{
		base.OnClosing(e);

		if (_closeConfirmed) return;
		if (DataContext is not MainWindowViewModel vm) return;

		var count = vm.ActiveSessionCount;
		if (count == 0) return;

		e.Cancel = true;

		var noun      = count == 1 ? "session is" : "sessions are";
		var message   = $"There are {count} Claude Code {noun} currently active.\nAre you sure you want to terminate them and close?";
		var confirmed = await ShowConfirmOverlayAsync(message, "Yes, close");

		if (!confirmed) return;

		vm.TerminateAllSessions();
		_closeConfirmed = true;
		Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		// Save scroll position of the active session view before closing
		SaveActiveSessionScrollPosition();

		var settings = App.Services.GetRequiredService<IAppSettingsService>();
		var ws       = settings.Settings.Window;

		var isMaximized = WindowState == WindowState.Maximized;
		ws.IsMaximized = isMaximized;

		// When maximized, Avalonia reports Position=(0,0) regardless of screen,
		// so use the last tracked normal-state position instead.
		if (isMaximized)
		{
			ws.Left   = _lastNormalPosition.X;
			ws.Top    = _lastNormalPosition.Y;
			ws.Width  = _lastNormalWidth;
			ws.Height = _lastNormalHeight;
		}
		else
		{
			ws.Left   = Position.X;
			ws.Top    = Position.Y;
			ws.Width  = Width;
			ws.Height = Height;
		}

		Log.Information("OnClosed: WindowState={State}, Position=({PX},{PY}), " +
			"lastNormal=({NX},{NY}), saving Left={Left}, Top={Top}, W={W}, H={H}, Max={Max}",
			WindowState, Position.X, Position.Y,
			_lastNormalPosition.X, _lastNormalPosition.Y,
			ws.Left, ws.Top, ws.Width, ws.Height, ws.IsMaximized);

		ws.SplitterPosition = MainContentGrid.ColumnDefinitions[0].Width.Value;

		settings.Save();

		base.OnClosed(e);
	}

	private void SaveActiveSessionScrollPosition()
	{
		// Find the active SessionView and capture its scroll offset
		var sessionView = this.FindDescendantOfType<SessionView>();
		if (sessionView?.DataContext is SessionViewModel vm)
			vm.ScrollOffset = sessionView.FindDescendantOfType<ScrollViewer>()?.Offset.Y ?? 0;
	}

	// ── Global hotkeys ───────────────────────────────────────────────────────

	private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
	{
		var keyService = App.Services.GetRequiredService<IKeyBindingService>();

		if (keyService.Matches(Constants.KeyBindings.ImportSessions, e))
		{
			e.Handled = true;
			_ = HandleImportSessionsHotkey();
			return;
		}

		if (keyService.Matches(Constants.KeyBindings.AddDirectory, e))
		{
			e.Handled = true;
			_ = HandleAddDirectoryHotkey();
			return;
		}

		if (keyService.Matches(Constants.KeyBindings.OpenSettings, e))
		{
			e.Handled = true;
			if (DataContext is MainWindowViewModel vm)
				vm.OpenSettingsCommand.Execute().Subscribe();
			return;
		}
	}

	private async Task HandleAddDirectoryHotkey()
	{
		if (DataContext is not MainWindowViewModel vm)
			return;

		var folders = await StorageProvider.OpenFolderPickerAsync(
			new Avalonia.Platform.Storage.FolderPickerOpenOptions
			{
				Title = "Select Working Directory",
				AllowMultiple = false,
			});

		if (folders.Count == 0)
			return;

		vm.SessionTree.AddDirectory(folders[0].Path.LocalPath);
	}

	private async Task HandleImportSessionsHotkey()
	{
		if (DataContext is not MainWindowViewModel vm)
			return;

		if (vm.SessionTree.Directories.Count == 0)
		{
			// No directories — offer to add one via folder picker
			var folders = await StorageProvider.OpenFolderPickerAsync(
				new Avalonia.Platform.Storage.FolderPickerOpenOptions
				{
					Title = "Select Working Directory for Import",
					AllowMultiple = false,
				});

			if (folders.Count == 0)
				return;

			vm.SessionTree.AddDirectory(folders[0].Path.LocalPath);
		}

		var importService = App.Services.GetRequiredService<IClaudeSessionImportService>();
		var assistService = App.Services.GetRequiredService<IClaudeAssistService>();
		var daemonService = App.Services.GetRequiredService<ITessynDaemonService>();

		var sourceDirectories = vm.SessionTree.BuildSourceDirectories();
		var importTargets = vm.SessionTree.BuildImportTargets();
		var (initialPath, initialTargetKey) = vm.SessionTree.GetSelectedImportContext();

		var pickerVm = new ImportPickerViewModel(importService, assistService, daemonService);
		var alreadyImportedIds = vm.SessionTree.CollectAllClaudeSessionIds();
		pickerVm.Initialize(sourceDirectories, importTargets, initialPath, initialTargetKey, alreadyImportedIds);

		var picker = new ImportPickerWindow { DataContext = pickerVm };
		await picker.ShowDialog(this);

		if (picker.Result == null || picker.Result.Count == 0)
			return;

		var target = pickerVm.SelectedImportTarget;
		if (target == null)
			return;

		var (dirNode, grpNode) = vm.SessionTree.FindTargetByKey(target.Key);
		var fileService = App.Services.GetRequiredService<ISessionFileService>();

		foreach (var item in picker.Result)
			SessionTreeView.ExecuteImport(vm.SessionTree, fileService, importService, item, dirNode, grpNode);
	}

	// ── Overlay: confirm dialog ───────────────────────────────────────────────

	public async Task<bool> ShowConfirmOverlayAsync(string message, string okLabel = "OK")
	{
		var tcs = new TaskCompletionSource<bool>();

		ConfirmMessage.Text  = message;
		ConfirmOkBtn.Content = okLabel;
		ConfirmCard.IsVisible   = true;
		OverlayPanel.IsVisible  = true;

		void OnOk(object? s, RoutedEventArgs _)    => tcs.TrySetResult(true);
		void OnCancel(object? s, RoutedEventArgs _) => tcs.TrySetResult(false);
		void OnKeyDown(object? s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape) tcs.TrySetResult(false);
		}

		ConfirmOkBtn.Click     += OnOk;
		ConfirmCancelBtn.Click += OnCancel;
		this.KeyDown           += OnKeyDown;

		var result = await tcs.Task;

		ConfirmOkBtn.Click     -= OnOk;
		ConfirmCancelBtn.Click -= OnCancel;
		this.KeyDown           -= OnKeyDown;

		OverlayPanel.IsVisible = false;
		ConfirmCard.IsVisible  = false;

		return result;
	}

	// ── Overlay: input dialog ─────────────────────────────────────────────────

	public async Task<string?> ShowInputOverlayAsync(string title, string prompt, string? initialValue = null)
	{
		var tcs = new TaskCompletionSource<string?>();

		InputCardTitle.Text    = title;
		InputCardPrompt.Text   = prompt;
		InputCardBox.Text      = initialValue ?? string.Empty;
		InputCard.IsVisible    = true;
		OverlayPanel.IsVisible = true;
		InputCardBox.Focus();
		InputCardBox.SelectAll();

		void Submit()
		{
			var text = InputCardBox.Text?.Trim();
			if (!string.IsNullOrEmpty(text))
				tcs.TrySetResult(text);
		}

		void OnOk(object? s, RoutedEventArgs _)    => Submit();
		void OnCancel(object? s, RoutedEventArgs _) => tcs.TrySetResult(null);
		void OnKeyDown(object? s, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)  { e.Handled = true; Submit(); }
			if (e.Key == Key.Escape) { e.Handled = true; tcs.TrySetResult(null); }
		}

		InputOkBtn.Click     += OnOk;
		InputCancelBtn.Click += OnCancel;
		InputCardBox.KeyDown += OnKeyDown;

		var result = await tcs.Task;

		InputOkBtn.Click     -= OnOk;
		InputCancelBtn.Click -= OnCancel;
		InputCardBox.KeyDown -= OnKeyDown;

		OverlayPanel.IsVisible = false;
		InputCard.IsVisible    = false;

		return result;
	}

	// ── Window drag via title bar ─────────────────────────────────────────────

	private void OnMenuBarPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (IsTitleBarControl(e.Source)) return;
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

		if (e.ClickCount == 2)
		{
			WindowState = WindowState == WindowState.Maximized
				? WindowState.Normal
				: WindowState.Maximized;
			e.Handled = true;
			return;
		}

		BeginMoveDrag(e);
	}

	private static bool IsTitleBarControl(object? source)
	{
		var visual = source as Visual;
		while (visual != null)
		{
			if (visual is Button or MenuItem) return true;
			visual = visual.GetVisualParent();
		}
		return false;
	}

	// ── Window control buttons ────────────────────────────────────────────────

	private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
		WindowState = WindowState.Minimized;

	private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) =>
		WindowState = WindowState == WindowState.Maximized
			? WindowState.Normal
			: WindowState.Maximized;

	private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

	private static string GetBuildNumber()
	{
		try
		{
			var assembly = Assembly.GetExecutingAssembly();
			var location = assembly.Location;
			if (!string.IsNullOrEmpty(location) && File.Exists(location))
			{
				var buildTime = File.GetLastWriteTime(location);
				return buildTime.ToString("yyyyMM.dd.HHmm");
			}

			// Fallback for single-file publish: use entry assembly
			var entry = Assembly.GetEntryAssembly();
			if (entry?.Location is { Length: > 0 } entryLoc && File.Exists(entryLoc))
			{
				var buildTime = File.GetLastWriteTime(entryLoc);
				return buildTime.ToString("yyyyMM.dd.HHmm");
			}
		}
		catch
		{
			// Ignore — build number is cosmetic.
		}

		return string.Empty;
	}
}
