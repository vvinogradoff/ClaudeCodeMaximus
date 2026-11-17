using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// W7: the commands <c>app.importSessions</c> (Ctrl+I by default, editable in the key bindings; the Avalonia "ImportSessions" action) and <c>tree.importSessions</c> (the tree's
/// context menu: import into the directory or group under the mouse), and its entry in the hamburger menu. No existing view is edited.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportShellModule : IShellModule
{
	private readonly ImportPickerHost _host;
	private readonly TreeContext _tree;

	public ImportShellModule(ImportPickerHost host, TreeContext tree)
	{
		_host = host;
		_tree = tree;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		commands.Register(new ShellCommand
		{
			Id = Constants.Import.OpenCommandId,
			Title = Constants.Import.OpenCommandTitle,
			DefaultGesture = Constants.Import.OpenCommandGesture,
			AllowInTextInput = true,
			IgnoreRepeat = true,
			ExecuteAsync = _ => _host.OpenAsync(_tree.Current?.SelectedNode),
		});
		commands.Register(new ShellCommand
		{
			Id = Constants.Import.TreeCommandId,
			Title = Constants.Import.TreeCommandTitle,
			CanExecute = node => TreeOperations.ContainerOf(node as TreeNodeViewModel) != null,
			ExecuteAsync = node => _host.OpenAsync(node as TreeNodeViewModel),
		});
		commands.AddMenuEntry(Constants.Menus.TreeNode, Constants.Import.TreeCommandId, Constants.Import.TreeMenuOrder, Constants.Import.TreeMenuGroup);

		commands.AddMenuEntry(Constants.Menus.Main, Constants.Import.OpenCommandId, Constants.Import.OpenButtonOrder);
	}
}
