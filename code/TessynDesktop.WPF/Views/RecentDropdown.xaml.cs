using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The title-bar Recent dropdown (W3, FR.9.6, FR.1.15): the daemon's recent list. It reloads each time it opens; picking a session closes the
/// popup and brings the Sessions panel forward (hide and favourite keep it open).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class RecentDropdown : UserControl
{
	public RecentDropdown()
	{
		InitializeComponent();
	}

	/// <summary>The shell layout whose Sessions panel comes forward after a pick; the running app supplies it, tests set it.</summary>
	public IShellLayout? Layout { get; set; }

	private void OnOpened(object? sender, EventArgs e) => (DataContext as RecentViewModel)?.RefreshCommand.Execute(null);

	private void OnRowClick(object sender, RoutedEventArgs e)
	{
		if (e.OriginalSource is not FrameworkElement { Tag: Constants.SessionLists.OpenTag })
			return;

		Menu.IsOpen = false;
		(Layout ?? new ShellServiceProvider().Get<IShellLayout>())?.BringForward(Constants.DockedShell.SessionsPanelId);   // the Sessions panel comes forward (FR.1.15, FR.6.14)
	}
}
