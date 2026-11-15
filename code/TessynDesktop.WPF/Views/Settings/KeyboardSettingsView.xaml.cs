using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>The Keyboard shortcuts tab (DataContext: <c>KeyboardSectionViewModel</c>); every edit re-checks the rows for invalid or shared combinations.</summary>
/// <remarks>Created by Claude</remarks>
public partial class KeyboardSettingsView : UserControl
{
	public KeyboardSettingsView()
	{
		InitializeComponent();
	}

	private void OnTextChanged(object sender, TextChangedEventArgs e) => (DataContext as KeyboardSectionViewModel)?.Validate();
}
