using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The "Switch profile…" button under the tree rows (W5): runs the switch command on the selected node.</summary>
/// <remarks>Created by Claude</remarks>
public partial class ProfileSwitchButton : UserControl
{
	private readonly IShellCommands _commands;

	public ProfileSwitchButton(IShellCommands commands)
	{
		_commands = commands;
		InitializeComponent();
	}

	private async void OnClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is TreeViewModel { SelectedNode: { } node })
			await _commands.ExecuteAsync(Constants.Profiles.SwitchCommandId, node);
	}
}
