using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Closes the slide-out of an auto-hidden panel (FR.6.13, FR.29 decision 4): Esc while the keyboard focus is in it, and a click on its edge tab while it is open (the library
/// ignores that click). The library offers no public way to hide the slide-out, so its internal <c>DockingManager.HideAutoHideWindow</c> is called; where it is missing
/// (another library version) nothing is closed and <see cref="Available"/> is false.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutoHideWindowCloser
{
	private readonly DockingManager _manager;
	private readonly MethodInfo? _hide;

	public AutoHideWindowCloser(DockingManager manager)
	{
		_manager = manager;
		_hide = typeof(DockingManager).GetMethod(Constants.DockingChrome.HideAutoHideWindowMethod, BindingFlags.Instance | BindingFlags.NonPublic);
		manager.PreviewKeyDown += OnPreviewKeyDown;
		manager.PreviewMouseDown += OnPreviewMouseDown;
	}

	public bool Available => _hide != null;

	/// <summary>True while a slide-out is open.</summary>
	public bool IsOpen => _manager.AutoHideWindow is { Model: not null, Visibility: Visibility.Visible };

	/// <summary>Closes the open slide-out; false when none is open or the library has no such method.</summary>
	public bool Close()
	{
		if (_hide == null || !IsOpen)
			return false;

		_hide.Invoke(_manager, [null]);
		return true;
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Escape || e.Handled || !IsOpen)
			return;

		if (e.OriginalSource is DependencyObject source && IsInside(source, _manager.AutoHideWindow) && Close())
			e.Handled = true;
	}

	private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.Handled || !IsOpen || e.OriginalSource is not DependencyObject source)
			return;

		var anchor = Ancestor<LayoutAnchorControl>(source);
		if (anchor != null && ReferenceEquals(anchor.Model, _manager.AutoHideWindow.Model) && Close())
			e.Handled = true;   // the click on the edge tab of the open slide-out closes it
	}

	private static bool IsInside(DependencyObject element, DependencyObject container)
	{
		for (var current = element; current != null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
		{
			if (ReferenceEquals(current, container))
				return true;
		}

		return false;
	}

	private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
	{
		for (var current = element; current != null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
		{
			if (current is T match)
				return match;
		}

		return null;
	}
}
