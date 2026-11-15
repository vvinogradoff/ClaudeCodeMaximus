using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>The title-bar Recent dropdown (W3, FR.9.6): the same list as the Recent tab; opening a session closes the popup, hide and favourite keep it open.</summary>
/// <remarks>Created by Claude</remarks>
public partial class RecentDropdown : UserControl
{
	public RecentDropdown()
	{
		InitializeComponent();
	}

	private void OnRowClick(object sender, RoutedEventArgs e)
	{
		if (e.OriginalSource is FrameworkElement { Tag: Constants.SessionLists.OpenTag })
			Menu.IsOpen = false;
	}
}
