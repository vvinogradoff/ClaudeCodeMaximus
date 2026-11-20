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
	/// <summary>The name is being edited in this view (not in any other header showing the same session).</summary>
	public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(SessionTitle), new PropertyMetadata(false));

	public SessionTitle()
	{
		InitializeComponent();
	}

	public bool IsEditing
	{
		get => (bool)GetValue(IsEditingProperty);
		private set => SetValue(IsEditingProperty, value);
	}

	/// <summary>The command registry and the tree the Delete icon acts through; the running app supplies them, view tests set them (null: the icon does nothing).</summary>
	public IShellCommands? Commands { get; set; }

	public TreeContext? Tree { get; set; }

	private SessionPaneViewModel? Pane => DataContext as SessionPaneViewModel;

	private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount != 2 || Pane is not { HasSession: true } pane)
			return;

		e.Handled = true;
		BeginEdit(pane);
	}

	/// <summary>Shows the box with the current name; only this view opens it.</summary>
	public void BeginEdit(SessionPaneViewModel pane)
	{
		RenameBox.Text = pane.Title;
		IsEditing = true;
	}

	/// <summary>Enter: hands the typed name to the pane's rename command (an empty or unchanged name only closes the box, a refusal shows as the pane's notice).</summary>
	public void CommitEdit()
	{
		if (!IsEditing)
			return;

		IsEditing = false;
		if (Pane is not { } pane)
			return;

		pane.RenameText = RenameBox.Text;
		pane.CommitRenameCommand.Execute(null);
	}

	public void CancelEdit() => IsEditing = false;

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
		switch (e.Key)
		{
			case Key.Enter:
				e.Handled = true;
				CommitEdit();
				break;
			case Key.Escape:
				e.Handled = true;
				CancelEdit();
				break;
		}
	}

	/// <summary>Leaving the box any other way (a click elsewhere) is a cancel: only Enter accepts.</summary>
	private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		CancelEdit();
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
