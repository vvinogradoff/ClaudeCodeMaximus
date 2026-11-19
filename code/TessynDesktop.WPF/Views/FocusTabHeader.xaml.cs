using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>The content of the Focus panel's tab header (FR.6.16): the title and the badge. Pure view; the data context is the pane's <c>FocusViewModel</c>.</summary>
/// <remarks>Created by Claude</remarks>
public partial class FocusTabHeader : UserControl
{
	public FocusTabHeader()
	{
		InitializeComponent();
	}
}
