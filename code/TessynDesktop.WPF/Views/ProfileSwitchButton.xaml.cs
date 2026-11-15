using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The "Switch profile…" button under the tree rows (W5): runs the switch command on the selected node and shows the switch's status line. It is hidden
/// unless the daemon announces <c>profiles.admin</c>; its DataContext is the tree, so the switch's own state is bound by source.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ProfileSwitchButton : UserControl
{
	private readonly IShellCommands _commands;

	public ProfileSwitchButton(IShellCommands commands, ProfileSwitchViewModel switcher)
	{
		_commands = commands;
		InitializeComponent();
		SetBinding(VisibilityProperty, new Binding(nameof(ProfileSwitchViewModel.IsAvailable)) { Source = switcher, Converter = new BooleanToVisibilityConverter() });
		Status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProfileSwitchViewModel.StatusText)) { Source = switcher });
	}

	private async void OnClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is TreeViewModel { SelectedNode: { } node })
			await _commands.ExecuteAsync(Constants.Profiles.SwitchCommandId, node);
	}
}
