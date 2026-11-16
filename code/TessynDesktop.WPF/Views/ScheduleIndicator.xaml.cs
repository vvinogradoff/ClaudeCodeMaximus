using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The schedule indicator in the session header (W8): a clock with the number of schedules of the open session; it opens the list with Cancel per schedule and the
/// read-only mailbox deliveries. Hidden while the session has none. Its DataContext is the session pane's view model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ScheduleIndicator : UserControl
{
	public ScheduleIndicator()
	{
		InitializeComponent();
	}
}
