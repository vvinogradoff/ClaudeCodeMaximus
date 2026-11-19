using System.Windows;
using System.Windows.Media;

namespace TessynDesktop.WPF.Views;

/// <summary>Walks the visual tree of the docking manager for the controls the library builds from its templates (the tab strip, the edge tabs).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingVisualTree
{
	public IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}
}
