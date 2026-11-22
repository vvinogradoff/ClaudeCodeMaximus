using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>The facts of an elevated-run card (FR.23.32). Pure view; its DataContext is the card's view model.</summary>
/// <remarks>Created by Claude</remarks>
public partial class ElevatedRunFactsView : UserControl
{
	public ElevatedRunFactsView()
	{
		InitializeComponent();
	}

	/// <summary>The command block, for tests.</summary>
	public TextBox CommandTextBox => CommandBox;
}
