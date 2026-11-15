using System.Windows;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The modal window of every <see cref="DialogViewModel"/>; closing it any other way than answering cancels the dialog.</summary>
/// <remarks>Created by Claude</remarks>
public partial class DialogWindow : Window
{
	private readonly DialogViewModel _dialog;

	public DialogWindow(DialogViewModel dialog)
	{
		_dialog = dialog;
		DataContext = dialog;
		InitializeComponent();

		// Enter answers the default button: Confirm, except for a destructive question where it is Cancel.
		AcceptButton.IsDefault = !dialog.IsDestructive;
		CancelButton.IsDefault = dialog.IsDestructive;
		Loaded += (_, _) =>
		{
			if (!dialog.IsInput)
				return;

			InputBox.Focus();
			InputBox.SelectAll();
		};
		Closed += (_, _) => _dialog.CancelCommand.Execute(null);
	}
}
