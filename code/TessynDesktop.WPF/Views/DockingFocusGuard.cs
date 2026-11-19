using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Keeps the keyboard focus where it is while the layout changes (FR.6.20). The library gives the focus to the last focused element of whatever content becomes active
/// (<c>FocusElementManager</c>); a focus move from one panel into another within a short time of a layout change and without any input of the user is cancelled. A click or key
/// press into a panel always passes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingFocusGuard
{
	public DockingFocusGuard(DockingManager manager, DockingFocusPolicy? policy = null)
	{
		Policy = policy ?? new DockingFocusPolicy();
		manager.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Policy.NoteInput()), true);
		manager.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, _) => Policy.NoteInput()), true);
		manager.ActiveContentChanged += (_, _) => Policy.NoteLayoutChange();
		manager.LayoutUpdated += (_, _) => Policy.NoteLayoutChange();
		manager.PreviewLostKeyboardFocus += OnPreviewLostKeyboardFocus;
	}

	public DockingFocusPolicy Policy { get; }

	private void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (e.OldFocus is not DependencyObject oldFocus || e.NewFocus is not DependencyObject newFocus)
			return;

		var from = PanelOf(oldFocus);
		var to = PanelOf(newFocus);
		if (Policy.IsFocusSteal(from != null, to != null && !ReferenceEquals(from, to)))
			e.Handled = true;
	}

	/// <summary>The panel (anchorable or document control) an element sits in; tab items are no panel content.</summary>
	private static DependencyObject? PanelOf(DependencyObject element)
	{
		for (var current = element; current != null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
		{
			if (current is LayoutAnchorableTabItem or LayoutDocumentTabItem)
				return null;

			if (current is LayoutAnchorableControl or LayoutDocumentControl)
				return current;
		}

		return null;
	}
}
