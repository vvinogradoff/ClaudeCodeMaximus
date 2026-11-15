using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The title-bar Recent dropdown (W3, FR.9.6, FR.1.15): the daemon's recent list. It reloads each time it opens; picking a session closes the
/// popup and shows the Tree tab (hide and favourite keep it open).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class RecentDropdown : UserControl
{
	public RecentDropdown()
	{
		InitializeComponent();
	}

	private void OnOpened(object? sender, EventArgs e) => (DataContext as RecentViewModel)?.RefreshCommand.Execute(null);

	private void OnRowClick(object sender, RoutedEventArgs e)
	{
		if (e.OriginalSource is not FrameworkElement { Tag: Constants.SessionLists.OpenTag })
			return;

		Menu.IsOpen = false;
		if (Window.GetWindow(this)?.FindName(Constants.SessionLists.LeftTabsName) is TabControl tabs)
			tabs.SelectedIndex = 0;   // the Tree tab is the first
	}
}
