using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The title of the session header: Delete icon, name, and the double-click rename box (DataContext: the session pane).</summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionTitle : UserControl
{
	public SessionTitle()
	{
		InitializeComponent();
	}

	/// <summary>The command registry and the tree the Delete icon acts through; the running app supplies them, view tests set them (null: the icon does nothing).</summary>
	public IShellCommands? Commands { get; set; }

	public TreeContext? Tree { get; set; }

	private SessionPaneViewModel? Pane => DataContext as SessionPaneViewModel;

	private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount != 2 || Pane is not { } pane || !pane.BeginRenameCommand.CanExecute(null))
			return;

		e.Handled = true;
		pane.BeginRenameCommand.Execute(null);
	}

	private void OnRenameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (e.NewValue is not true)
			return;

		// The box shows with the name selected, ready to be typed over.
		Dispatcher.BeginInvoke(() =>
		{
			RenameBox.Focus();
			Keyboard.Focus(RenameBox);
			RenameBox.SelectAll();
		}, System.Windows.Threading.DispatcherPriority.Input);
	}

	private void OnRenameKeyDown(object sender, KeyEventArgs e)
	{
		if (Pane is not { } pane)
			return;

		switch (e.Key)
		{
			case Key.Enter:
				e.Handled = true;
				pane.CommitRenameCommand.Execute(null);
				break;
			case Key.Escape:
				e.Handled = true;
				pane.CancelRenameCommand.Execute(null);
				break;
		}
	}

	/// <summary>Leaving the box any other way (a click elsewhere) is a cancel: only Enter accepts.</summary>
	private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (Pane is { IsRenaming: true } pane)
			pane.CancelRenameCommand.Execute(null);
	}

	private async void OnDeleteClick(object sender, RoutedEventArgs e)
	{
		var commands = Commands ?? new ShellServiceProvider().Get<IShellCommands>();
		var tree = (Tree ?? new ShellServiceProvider().Get<TreeContext>())?.Current;
		if (commands == null || Pane is not { NodeId: { } nodeId } || tree?.FindNode(nodeId) is not { } node)
			return;

		await commands.ExecuteAsync(Constants.TreeCommands.Delete, node);
	}
}
