using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The toolbar commands of the tree: they act on the selected node (the menu and the keys act on the node under the mouse or the selection, see <c>TreeShellModule</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TreeViewModel
{
	partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
	{
		AddSessionCommand.NotifyCanExecuteChanged();
		AddGroupCommand.NotifyCanExecuteChanged();
		RenameCommand.NotifyCanExecuteChanged();
		DeleteCommand.NotifyCanExecuteChanged();
		MoveToCommand.NotifyCanExecuteChanged();
		OnSelectedNodeChangedCore(value);
	}

	[RelayCommand(CanExecute = nameof(HasOperations))]
	private Task AddDirectoryAsync() => Operations!.AddDirectoryAsync();

	[RelayCommand(CanExecute = nameof(CanAddGroup))]
	private Task AddSessionAsync() => Operations!.AddSessionAsync(SelectedNode);

	[RelayCommand(CanExecute = nameof(CanAddGroup))]
	private Task AddGroupAsync() => Operations!.AddGroupAsync(SelectedNode);

	[RelayCommand(CanExecute = nameof(CanRename))]
	private Task RenameAsync() => Operations!.RenameAsync(SelectedNode);

	[RelayCommand(CanExecute = nameof(CanDelete))]
	private Task DeleteAsync() => Operations!.DeleteAsync(SelectedNode);

	[RelayCommand(CanExecute = nameof(CanMoveTo))]
	private Task MoveToAsync() => Operations!.MoveToAsync(SelectedNode);

	[RelayCommand(CanExecute = nameof(HasOperations))]
	private Task RefreshGitOriginsAsync() => Operations!.RefreshGitOriginsAsync();

	private bool HasOperations() => Operations != null;

	private bool CanAddGroup() => Operations != null && TreeOperations.ContainerOf(SelectedNode) != null;

	private bool CanRename() => Operations != null && TreeOperations.CanRename(SelectedNode);

	private bool CanDelete() => Operations != null && SelectedNode != null;

	private bool CanMoveTo() => Operations != null && TreeOperations.IsMovable(SelectedNode);
}
