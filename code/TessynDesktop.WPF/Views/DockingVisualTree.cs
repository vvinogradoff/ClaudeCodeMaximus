using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TessynDesktop.WPF.Views;

/// <summary>Walks the visual tree of the docking manager for the controls the library builds from its templates (the tab strip, the edge tabs).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingVisualTree
{
	/// <summary>The parent of an element for any kind of node: <see cref="VisualTreeHelper.GetParent"/> throws for a content element (a Hyperlink, a Run, a Span), so those go through their own parent chain.</summary>
	public DependencyObject? ParentOf(DependencyObject node)
	{
		switch (node)
		{
			case Visual or Visual3D:
				return VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
			case FrameworkContentElement framework:
				return framework.Parent ?? ContentOperations.GetParent(framework);
			case ContentElement content:
				return ContentOperations.GetParent(content) ?? LogicalTreeHelper.GetParent(content);
			default:
				return LogicalTreeHelper.GetParent(node);
		}
	}

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
