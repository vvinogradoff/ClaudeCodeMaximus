using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>The identity part of a session header: Delete icon, name (double-click rename) and Running / Idle text (DataContext: the session pane).</summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionIdentity : UserControl
{
	public SessionIdentity()
	{
		InitializeComponent();
	}

	/// <summary>The title part (Delete icon and name), for tests that script the confirmation dialog or the rename.</summary>
	public SessionTitle TitleView => Title;
}
