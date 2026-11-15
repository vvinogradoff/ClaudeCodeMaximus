using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TessynDesktop.WPF.Views;

/// <summary>Whether the keyboard focus is inside the tree panel: the tree's key gestures (F2, Delete) act only then.</summary>
/// <remarks>Created by Claude</remarks>
public static class TreeFocus
{
	public static bool HasFocus() => IsInTree(Keyboard.FocusedElement as DependencyObject);

	public static bool IsInTree(DependencyObject? element)
	{
		for (var node = element; node != null; node = Parent(node))
		{
			if (node is TreePanel)
				return true;
		}

		return false;
	}

	/// <summary>The visual parent, else the logical one (an element not laid out yet has no visual parent).</summary>
	private static DependencyObject? Parent(DependencyObject node) =>
		(node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
}
