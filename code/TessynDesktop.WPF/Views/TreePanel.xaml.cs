using System.Windows.Controls;
using System.Windows.Input;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The search box and the virtualized session tree.</summary>
/// <remarks>Created by Claude</remarks>
public partial class TreePanel : UserControl
{
	public TreePanel()
	{
		InitializeComponent();
	}

	/// <summary>
	/// TreeView keyboard behaviour on the flat list: Right expands a collapsed node or steps to its first child, Left collapses
	/// an expanded node or steps to its parent, Enter/Space toggle. Up/Down/Home/End come from the ListBox.
	/// </summary>
	private void OnTreeKeyDown(object sender, KeyEventArgs e)
	{
		if (sender is not ListBox list || list.DataContext is not TreeViewModel tree || list.SelectedItem is not TreeNodeViewModel node)
			return;

		switch (e.Key)
		{
			case Key.Right when node.CanExpand && !node.IsExpanded:
				tree.ToggleExpandCommand.Execute(node);
				break;
			case Key.Right when node.CanExpand && node.IsExpanded:
				SelectRelative(list, tree, node, 1);
				break;
			case Key.Left when node.CanExpand && node.IsExpanded:
				tree.ToggleExpandCommand.Execute(node);
				break;
			case Key.Left when node.Parent != null:
				SelectNode(list, tree, node.Parent);
				break;
			case Key.Enter or Key.Space when node.CanExpand:
				tree.ToggleExpandCommand.Execute(node);
				break;
			default:
				return;
		}

		e.Handled = true;
	}

	private static void SelectRelative(ListBox list, TreeViewModel tree, TreeNodeViewModel node, int offset)
	{
		var index = tree.Rows.IndexOf(node) + offset;
		if (index >= 0 && index < tree.Rows.Count)
			SelectNode(list, tree, tree.Rows[index]);
	}

	private static void SelectNode(ListBox list, TreeViewModel tree, TreeNodeViewModel target)
	{
		if (!tree.Rows.Contains(target))
			return;

		tree.SelectedNode = target;
		list.ScrollIntoView(target);
		(list.ItemContainerGenerator.ContainerFromItem(target) as ListBoxItem)?.Focus();
	}
}
