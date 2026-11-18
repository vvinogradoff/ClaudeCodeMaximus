using System.Windows;
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
		_dragDrop = new TreeDragDrop(RowList, () => DataContext as TreeViewModel);
		DataContextChanged += OnDataContextChanged;
	}

	private TreeViewModel? _observed;

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_observed != null)
			_observed.PropertyChanged -= OnTreePropertyChanged;

		_observed = e.NewValue as TreeViewModel;
		if (_observed != null)
			_observed.PropertyChanged += OnTreePropertyChanged;
	}

	/// <summary>
	/// A selection made from code (a session picked in the Recent dropdown or a list, a jump from a reference) scrolls the tree to the row, as a click on a
	/// visible row never needs to. Deferred until the rows the selection expanded are in the list.
	/// </summary>
	private void OnTreePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(TreeViewModel.SelectedNode))
			return;

		Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
		{
			if (_observed?.SelectedNode is { } node && _observed.Rows.Contains(node))
				RowList.ScrollIntoView(node);
		});
	}

	private readonly TreeDragDrop _dragDrop;

	/// <summary>The modifier keys held now; a seam so a view test can press Ctrl+Up without a real keyboard (the system key state cannot be set from a test).</summary>
	public Func<ModifierKeys> CurrentModifiers { get; set; } = () => Keyboard.Modifiers;

	/// <summary>A group's or a session's tooltip shows its path on disk and git origin: asked once, when the tooltip first opens.</summary>
	private async void OnRowToolTipOpening(object sender, ToolTipEventArgs e)
	{
		if (e.OriginalSource is FrameworkElement { DataContext: TreeNodeViewModel node } && DataContext is TreeViewModel tree)
			await tree.EnsureInfoAsync(node);
	}

	/// <summary>
	/// TreeView keyboard behaviour on the flat list: Right expands a collapsed node or steps to its first child, Left collapses
	/// an expanded node or steps to its parent, Enter/Space toggle. Up/Down/Home/End come from the ListBox.
	/// </summary>
	private void OnTreeKeyDown(object sender, KeyEventArgs e)
	{
		if (sender is not ListBox list || list.DataContext is not TreeViewModel tree || list.SelectedItem is not TreeNodeViewModel node)
			return;

		// FR.1.31: Ctrl+Up / Ctrl+Down move a top-level session one place. Handled here (tunnelling) so the ListBox does not take it as a focus move.
		if (e.Key is Key.Up or Key.Down && CurrentModifiers() == ModifierKeys.Control)
		{
			if (tree.Operations != null && TreeOperations.IsMovable(node))
			{
				e.Handled = true;
				_ = MoveStepAsync(list, tree, node, e.Key == Key.Up ? -1 : 1);
			}

			return;
		}

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

	/// <summary>Moves the session, then keeps it selected, in view, and the keyboard focus in the tree (the rows were rebuilt, so the old row is gone).</summary>
	private static async Task MoveStepAsync(ListBox list, TreeViewModel tree, TreeNodeViewModel node, int direction)
	{
		await tree.Operations!.MoveStepAsync(node, direction);

		var id = tree.SelectedNodeId;
		if (id != null && tree.FindNode(id) is { } current && tree.Rows.Contains(current))
		{
			tree.SelectedNode = current;
			list.ScrollIntoView(current);
			list.UpdateLayout();
			if (list.ItemContainerGenerator.ContainerFromItem(current) is ListBoxItem item)
			{
				item.Focus();
				return;
			}
		}

		list.Focus();
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
