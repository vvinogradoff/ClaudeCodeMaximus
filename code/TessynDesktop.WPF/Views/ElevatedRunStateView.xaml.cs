using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>The buttons and states of an elevated-run card (FR.23.32). Pure view; its DataContext is the card's view model.</summary>
/// <remarks>Created by Claude</remarks>
public partial class ElevatedRunStateView : UserControl
{
	public ElevatedRunStateView()
	{
		InitializeComponent();
	}

	public Button RunAsAdministratorButton => RunButton;

	public Button DeclineActionButton => DeclineButton;

	public Button StopActionButton => StopButton;

	public TextBox NoteTextBox => NoteBox;

	public Expander OutputExpanderControl => OutputExpander;
}
