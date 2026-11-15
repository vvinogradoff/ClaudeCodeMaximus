using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Drag and drop of a session onto a directory or group in the tree list. The rules are the view model's (<see cref="TreeOperations"/>): when the pointer goes
/// down on a session the daemon is asked where it may go (<c>tree.moveTargets</c>), so by the time it travels the feedback is ready: the row under the
/// pointer lights up as a target, or as refused (with the reason in the status line, and the "no" cursor). A drop on a refused row never happens.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeDragDrop
{
	/// <summary>The data format of a dragged tree node (in-process only).</summary>
	public const string DataFormat = "TessynDesktop.TreeNode";

	private const double AutoScrollEdge = 24;

	private readonly ListBox _list;
	private readonly Func<TreeViewModel?> _tree;
	private TreeNodeViewModel? _pressed;
	private Point _pressedAt;
	private TreeNodeViewModel? _hot;
	private string? _statusBefore;
	private bool _statusSaved;

	public TreeDragDrop(ListBox list, Func<TreeViewModel?> tree)
	{
		_list = list;
		_tree = tree;
		list.AllowDrop = true;
		list.PreviewMouseLeftButtonDown += OnMouseDown;
		list.PreviewMouseMove += OnMouseMove;
		list.PreviewMouseLeftButtonUp += (_, _) => _pressed = null;
		list.DragOver += OnDragOver;
		list.DragLeave += (_, _) => ClearFeedback();
		list.Drop += OnDrop;
	}

	private void OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		_pressed = NodeAt(e.OriginalSource as DependencyObject);
		_pressedAt = e.GetPosition(_list);
		var operations = _tree()?.Operations;
		if (_pressed != null && operations != null && TreeOperations.IsMovable(_pressed))
			_ = operations.BeginDrag(_pressed);
	}

	private void OnMouseMove(object sender, MouseEventArgs e)
	{
		if (_pressed == null || e.LeftButton != MouseButtonState.Pressed)
			return;

		var delta = e.GetPosition(_list) - _pressedAt;
		if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;

		var node = _pressed;
		_pressed = null;
		var operations = _tree()?.Operations;
		if (operations == null || !TreeOperations.IsMovable(node))
			return;

		try
		{
			DragDrop.DoDragDrop(_list, new DataObject(DataFormat, node), DragDropEffects.Move);
		}
		finally
		{
			ClearFeedback();
			operations.EndDrag();
		}
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		var operations = _tree()?.Operations;
		var source = e.Data.GetData(DataFormat) as TreeNodeViewModel;
		if (operations == null || source == null)
		{
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		AutoScroll(e);
		var over = NodeAt(e.OriginalSource as DependencyObject);
		var decision = operations.Evaluate(source, over);
		e.Effects = decision.Allowed ? DragDropEffects.Move : DragDropEffects.None;
		e.Handled = true;

		var highlighted = decision.Allowed ? decision.Target : over;
		if (!ReferenceEquals(highlighted, _hot))
			ClearHot();

		if (highlighted != null)
		{
			_hot = highlighted;
			highlighted.DropFeedback = decision.Allowed ? DropFeedback.Allowed : DropFeedback.Refused;
		}

		ShowReason(decision);
	}

	private async void OnDrop(object sender, DragEventArgs e)
	{
		var tree = _tree();
		var operations = tree?.Operations;
		var source = e.Data.GetData(DataFormat) as TreeNodeViewModel;
		var over = NodeAt(e.OriginalSource as DependencyObject);
		ClearFeedback();
		e.Handled = true;
		if (operations == null || source == null)
			return;

		await operations.DropAsync(source, over);
	}

	private void ShowReason(DropDecision decision)
	{
		var tree = _tree();
		if (tree == null)
			return;

		if (!_statusSaved)
		{
			_statusBefore = tree.StatusText;
			_statusSaved = true;
		}

		tree.StatusText = decision.Allowed ? _statusBefore : decision.Reason;
	}

	private void ClearFeedback()
	{
		ClearHot();
		var tree = _tree();
		if (_statusSaved && tree != null)
			tree.StatusText = _statusBefore;

		_statusSaved = false;
		_statusBefore = null;
	}

	private void ClearHot()
	{
		if (_hot != null)
			_hot.DropFeedback = DropFeedback.None;

		_hot = null;
	}

	/// <summary>Scrolls the list while the pointer is near its top or bottom edge, so a far group can be reached.</summary>
	private void AutoScroll(DragEventArgs e)
	{
		var viewer = FindScrollViewer(_list);
		if (viewer == null)
			return;

		var y = e.GetPosition(_list).Y;
		if (y < AutoScrollEdge)
			viewer.LineUp();
		else if (y > _list.ActualHeight - AutoScrollEdge)
			viewer.LineDown();
	}

	private static ScrollViewer? FindScrollViewer(DependencyObject root)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is ScrollViewer viewer)
				return viewer;

			var found = FindScrollViewer(child);
			if (found != null)
				return found;
		}

		return null;
	}

	/// <summary>The tree node whose row is under an element of the list (null on empty space or a button such as the expander).</summary>
	private static TreeNodeViewModel? NodeAt(DependencyObject? source)
	{
		for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
		{
			if (node is Button)
				return null;

			if (node is ListBoxItem item)
				return item.DataContext as TreeNodeViewModel;
		}

		return null;
	}
}
