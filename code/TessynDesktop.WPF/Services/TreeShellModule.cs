using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;
using static TessynDesktop.WPF.Constants;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The tree's entries in the shell (W2): the commands (add directory, new group, rename, delete, move to, refresh git origins) with their keys and the
/// context menu <see cref="Menus.TreeNode"/>, and the toolbar in the <see cref="Slots.TreeToolbar"/> slot. A command's parameter is the node the menu was
/// opened on; from a key it is the selected node, and only while the tree has the focus (so F2 or Delete in another part of the window do nothing).
/// A key never fires while the user types in a text box (<see cref="ShellKeyPolicy"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeShellModule : IShellModule
{
	private readonly TreeContext _context;

	public TreeShellModule(TreeContext context)
	{
		_context = context;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.AddDirectory,
			Title = "Add directory…",
			CanExecute = _ => Operations != null,
			ExecuteAsync = _ => Operations!.AddDirectoryAsync(),
		});
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.AddSession,
			Title = "New session…",
			CanExecute = node => Operations != null && TreeOperations.ContainerOf(node as TreeNodeViewModel) != null,
			ExecuteAsync = node => Operations!.AddSessionAsync(node as TreeNodeViewModel),
		});
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.AddGroup,
			Title = "New group…",
			CanExecute = node => Operations != null && TreeOperations.ContainerOf(node as TreeNodeViewModel) != null,
			ExecuteAsync = node => Operations!.AddGroupAsync(node as TreeNodeViewModel),
		});
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.Rename,
			Title = "Rename…",
			DefaultGesture = "F2",
			IgnoreRepeat = true,
			CanExecute = node => Operations != null && TreeOperations.CanRename(node as TreeNodeViewModel),
			ExecuteAsync = node => Operations!.RenameAsync(node as TreeNodeViewModel),
			ResolveParameter = SelectedNodeWhenTreeHasFocus,
		});
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.Delete,
			Title = "Delete…",
			DefaultGesture = "Delete",
			IgnoreRepeat = true,
			CanExecute = node => Operations != null && node is TreeNodeViewModel,
			ExecuteAsync = node => Operations!.DeleteAsync(node as TreeNodeViewModel),
			ResolveParameter = SelectedNodeWhenTreeHasFocus,
		});
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.MoveTo,
			Title = "Move to…",
			CanExecute = node => Operations != null && TreeOperations.IsMovable(node as TreeNodeViewModel),
			ExecuteAsync = node => Operations!.MoveToAsync(node as TreeNodeViewModel),
		});
		commands.Register(new ShellCommand
		{
			Id = TreeCommands.RefreshGitOrigins,
			Title = "Refresh git origins",
			CanExecute = _ => Operations != null,
			ExecuteAsync = _ => Operations!.RefreshGitOriginsAsync(),
		});

		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.AddSession, order: 9, group: "add");
		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.AddGroup, order: 10, group: "add");
		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.AddDirectory, order: 11, group: "add");
		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.Rename, order: 20, group: "edit");
		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.MoveTo, order: 21, group: "edit");
		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.Delete, order: 22, group: "edit");
		commands.AddMenuEntry(Menus.TreeNode, TreeCommands.RefreshGitOrigins, order: 30, group: "git");

		slots.Add(new ShellSlotContribution { SlotId = Slots.TreeToolbar, Order = 10, Create = () => new TreeToolbar() });
	}

	private TreeOperations? Operations => _context.Current?.Operations;

	private object? SelectedNodeWhenTreeHasFocus() => TreeFocus.HasFocus() ? _context.Current?.SelectedNode : null;
}
