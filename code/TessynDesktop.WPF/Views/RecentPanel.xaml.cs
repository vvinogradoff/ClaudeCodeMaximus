using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The Recent tab of the left panel (W3, FR.1.16): every top-level session per directory; each time the tab is shown it rebuilds and collapses all groups but the selected session's.</summary>
/// <remarks>Created by Claude</remarks>
public partial class RecentPanel : UserControl
{
	public RecentPanel()
	{
		InitializeComponent();
		Loaded += (_, _) => (DataContext as RecentTabViewModel)?.Activate();
	}
}
