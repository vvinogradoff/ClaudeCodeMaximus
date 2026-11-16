using System.Windows;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The import picker window (W7): the project, the Global toggle and the search box on top, the sessions in the middle (greyed when empty or already imported), the
/// target and the Import button below. It closes itself when the view model asks to, and ends the title polling when it closes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ImportPickerWindow : Window
{
	private readonly ImportPickerViewModel _viewModel;

	public ImportPickerWindow(ImportPickerViewModel viewModel)
	{
		_viewModel = viewModel;
		DataContext = viewModel;
		InitializeComponent();
		viewModel.CloseRequested += (_, _) => Close();
		Closed += (_, _) => _viewModel.Dispose();
		// IsCancel closes only a dialog; this window is modeless, so Close (mouse, keyboard or UI Automation Invoke) must close it itself.
		CloseButton.Click += (_, _) => Close();
	}
}
