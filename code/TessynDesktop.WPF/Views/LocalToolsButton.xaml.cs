using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The Tools button (FR.12.23.2): a short click flips the master switch, holding for <see cref="Constants.ToolbarLimits.LocalToolsHold"/> opens the
/// checklist instead. A press while the checklist is open (or right after it closed by a click away) only closes it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class LocalToolsButton : UserControl
{
	private readonly DispatcherTimer _hold = new() { Interval = Constants.ToolbarLimits.LocalToolsHold };
	private bool _swallow;
	private bool _pressed;
	private DateTime _closedAt = DateTime.MinValue;

	public LocalToolsButton()
	{
		InitializeComponent();
		_hold.Tick += OnHoldElapsed;
		DataContextChanged += (_, e) =>
		{
			if (e.OldValue is LocalToolsViewModel old)
				old.PropertyChanged -= OnModelChanged;
			if (e.NewValue is LocalToolsViewModel model)
				model.PropertyChanged += OnModelChanged;
		};
	}

	/// <summary>Clicking away closes the popup on the mouse-down; when that click lands on this button it must not also flip the switch.</summary>
	private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(LocalToolsViewModel.IsPopupOpen) && Model is { IsPopupOpen: false })
			_closedAt = DateTime.UtcNow;
	}

	private LocalToolsViewModel? Model => DataContext as LocalToolsViewModel;

	private void OnPress(object sender, MouseButtonEventArgs e)
	{
		var model = Model;
		if (model == null)
			return;

		e.Handled = true;   // the ToggleButton must not flip by itself: the view model decides
		_swallow = model.IsPopupOpen || DateTime.UtcNow - _closedAt < TimeSpan.FromMilliseconds(250);
		if (model.IsPopupOpen)
			model.ClosePopupCommand.Execute(null);

		_pressed = true;
		ToolsButton.CaptureMouse();
		if (!_swallow)
			_hold.Start();
	}

	private void OnHoldElapsed(object? sender, EventArgs e)
	{
		_hold.Stop();
		Model?.TogglePopupCommand.Execute(null);
		_swallow = true;   // the release that follows a hold is not a click
	}

	private void OnRelease(object sender, MouseButtonEventArgs e)
	{
		if (!_pressed)
			return;

		e.Handled = true;
		_pressed = false;
		var wasHolding = _hold.IsEnabled;
		_hold.Stop();
		ToolsButton.ReleaseMouseCapture();
		if (wasHolding && !_swallow)
			Model?.ToggleEnabledCommand.Execute(null);
	}

	private void OnLostCapture(object sender, MouseEventArgs e)
	{
		_hold.Stop();
		_pressed = false;
	}

	/// <summary>Keyboard: Space or Enter is the short click, Alt+Down opens the checklist.</summary>
	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key is Key.Space or Key.Enter)
		{
			Model?.ToggleEnabledCommand.Execute(null);
			e.Handled = true;
		}
		else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)
		{
			Model?.TogglePopupCommand.Execute(null);
			e.Handled = true;
		}
	}
}
