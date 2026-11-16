using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shell;

namespace TessynDesktop.WPF.Views;

/// <summary>The custom title bar (FR.6.3). The window buttons act on the window it sits in; the empty middle is the caption.</summary>
/// <remarks>Created by Claude</remarks>
public partial class TitleBar : UserControl
{
	private Window? _window;

	public TitleBar()
	{
		InitializeComponent();
		Loaded += (_, _) => Hook();
		Unloaded += (_, _) =>
		{
			if (_window != null)
				_window.StateChanged -= OnStateChanged;

			_window = null;
		};
	}

	private void Hook()
	{
		_window = Window.GetWindow(this);
		if (_window == null)
			return;

		_window.StateChanged -= OnStateChanged;
		_window.StateChanged += OnStateChanged;
		OnStateChanged(_window, EventArgs.Empty);
	}

	/// <summary>The maximize button shows "restore" while the window is maximized.</summary>
	private void OnStateChanged(object? sender, EventArgs e)
	{
		var maximized = _window?.WindowState == WindowState.Maximized;
		MaximizeGlyph.Text = maximized ? "" : "";
		MaximizeButton.ToolTip = maximized ? Constants.TitleBar.RestoreAutomationName : Constants.TitleBar.MaximizeAutomationName;
		AutomationProperties.SetName(MaximizeButton, maximized ? Constants.TitleBar.RestoreAutomationName : Constants.TitleBar.MaximizeAutomationName);
	}

	private void OnMenuClick(object sender, RoutedEventArgs e)
	{
		if (MenuButton.ContextMenu is not { } menu)
			return;

		menu.PlacementTarget = MenuButton;
		menu.DataContext = DataContext;
		menu.IsOpen = true;
	}

	private void OnMinimizeClick(object sender, RoutedEventArgs e)
	{
		if (_window != null)
			SystemCommands.MinimizeWindow(_window);
	}

	private void OnMaximizeClick(object sender, RoutedEventArgs e)
	{
		if (_window == null)
			return;

		if (_window.WindowState == WindowState.Maximized)
			SystemCommands.RestoreWindow(_window);
		else
			SystemCommands.MaximizeWindow(_window);
	}

	private void OnCloseClick(object sender, RoutedEventArgs e) => _window?.Close();

	private void OnExitClick(object sender, RoutedEventArgs e) => _window?.Close();
}
