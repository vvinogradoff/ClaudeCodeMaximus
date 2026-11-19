using System.Windows.Controls;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The pin at the right end of a pane's tab row (FR.6.13): toggles the auto-hide of the pane of the selected panel through the library's own auto-hide command (so it
/// is only taken where the panel allows auto-hide).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingPinButton : Button
{
	protected override void OnClick()
	{
		base.OnClick();
		var command = FindCommand();
		if (command != null && command.CanExecute(null))
			command.Execute(null);
	}

	private System.Windows.Input.ICommand? FindCommand()
	{
		var pane = FindPane(this);
		if (pane?.Model is not LayoutAnchorablePane { SelectedContent: LayoutAnchorable selected })
			return null;

		var manager = selected.Root?.Manager;
		return (manager?.GetLayoutItemFromModel(selected) as LayoutAnchorableItem)?.AutoHideCommand;
	}

	private static LayoutAnchorablePaneControl? FindPane(System.Windows.DependencyObject start)
	{
		var current = System.Windows.Media.VisualTreeHelper.GetParent(start);
		while (current != null && current is not LayoutAnchorablePaneControl)
			current = System.Windows.Media.VisualTreeHelper.GetParent(current);

		return current as LayoutAnchorablePaneControl;
	}
}
