using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private bool _closeConfirmed;

	public MainWindow()
	{
		InitializeComponent();
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		var ws = App.Services.GetRequiredService<IAppSettingsService>().Settings.Window;

		Width    = ws.Width;
		Height   = ws.Height;
		Position = new PixelPoint((int)ws.Left, (int)ws.Top);

		MainContentGrid.ColumnDefinitions[0].Width = new GridLength(
			Math.Clamp(ws.SplitterPosition, 180, 600));
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
		var settings = App.Services.GetRequiredService<IAppSettingsService>();
		var ws       = settings.Settings.Window;

		ws.Width             = Width;
		ws.Height            = Height;
		ws.Left              = Position.X;
		ws.Top               = Position.Y;
		ws.SplitterPosition  = MainContentGrid.ColumnDefinitions[0].Width.Value;

		settings.Save();

		base.OnClosed(e);
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
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
}
