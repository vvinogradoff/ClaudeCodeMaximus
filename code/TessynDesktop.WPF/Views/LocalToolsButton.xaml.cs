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
				old.Opened -= OnModelOpened;
			if (e.NewValue is LocalToolsViewModel model)
				model.Opened += OnModelOpened;
		};
	}

	/// <summary>A session opened and the checklist data is gone: this view's popup closes (the open state belongs to each view, so the other bar is untouched).</summary>
	private void OnModelOpened(object? sender, EventArgs e) => Checklist.IsOpen = false;

	/// <summary>Clicking away closes the popup on the mouse-down; when that click lands on this button it must not also flip the switch.</summary>
	private void OnChecklistClosed(object? sender, EventArgs e) => _closedAt = DateTime.UtcNow;

	private void ToggleChecklist() => Checklist.IsOpen = !Checklist.IsOpen;

	private LocalToolsViewModel? Model => DataContext as LocalToolsViewModel;

	private void OnPress(object sender, MouseButtonEventArgs e)
	{
		var model = Model;
		if (model == null)
			return;

		e.Handled = true;   // the ToggleButton must not flip by itself: the view model decides
		_swallow = Checklist.IsOpen || DateTime.UtcNow - _closedAt < TimeSpan.FromMilliseconds(250);
		if (Checklist.IsOpen)
			Checklist.IsOpen = false;

		_pressed = true;
		ToolsButton.CaptureMouse();
		if (!_swallow)
			_hold.Start();
	}

	private void OnHoldElapsed(object? sender, EventArgs e)
	{
		_hold.Stop();
		if (Model != null)
			ToggleChecklist();
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

	/// <summary>Keyboard: Space and Enter are the button's own Click (which runs the command); Alt+Down opens the checklist.</summary>
	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)
		{
			if (Model != null)
				ToggleChecklist();
			e.Handled = true;
		}
	}
}
