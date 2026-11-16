using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// W7: the commands <c>app.importSessions</c> (Ctrl+I by default, editable in the key bindings; the Avalonia "ImportSessions" action) and <c>tree.importSessions</c> (the tree's
/// context menu: import into the directory or group under the mouse), and an Import button in the title-bar toolbar. No existing view is edited.
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

		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.TitleBarToolbar,
			Order = Constants.Import.OpenButtonOrder,
			Create = () => CreateButton(commands),
		});
	}

	private static Button CreateButton(IShellCommands commands)
	{
		var button = new Button
		{
			Content = Constants.Import.OpenButtonText,
			Margin = new Thickness(0, 4, 8, 4),
			Padding = new Thickness(10, 2, 10, 2),
			ToolTip = Constants.Import.OpenCommandTitle,
		};
		System.Windows.Automation.AutomationProperties.SetName(button, Constants.Import.OpenButtonAutomationName);
		button.Click += (_, _) => _ = commands.ExecuteAsync(Constants.Import.OpenCommandId, null);
		return button;
	}
}
