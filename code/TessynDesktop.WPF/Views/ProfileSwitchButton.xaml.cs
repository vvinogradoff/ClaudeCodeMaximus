using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The "Switch profile…" button of the session header, right of "Send later" (W5): runs the switch command on the open session and shows the switch's status line. It is
/// hidden unless the daemon announces <c>profiles.admin</c>; its DataContext is the session pane, so the switch's own state is bound by source.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ProfileSwitchButton : UserControl
{
	private readonly IShellCommands _commands;
	private readonly TreeContext _tree;

	public ProfileSwitchButton(IShellCommands commands, TreeContext tree, ProfileSwitchViewModel switcher)
	{
		_commands = commands;
		_tree = tree;
		InitializeComponent();
		SetBinding(VisibilityProperty, new Binding(nameof(ProfileSwitchViewModel.IsAvailable)) { Source = switcher, Converter = new BooleanToVisibilityConverter() });
		Status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProfileSwitchViewModel.StatusText)) { Source = switcher });
	}

	private async void OnClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is SessionPaneViewModel { NodeId: { } nodeId } && _tree.Current?.FindNode(nodeId) is { } node)
			await _commands.ExecuteAsync(Constants.Profiles.SwitchCommandId, node);
	}
}
