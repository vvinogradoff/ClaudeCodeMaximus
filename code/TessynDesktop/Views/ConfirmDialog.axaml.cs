using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TessynDesktop.Views;

/// <summary>
/// Generic Yes/No confirmation dialog, usable from any owner <see cref="Window"/> — unlike
/// <c>MainWindow.ShowConfirmOverlayAsync</c>, which renders into <c>MainWindow</c>'s own visual tree
/// and can't be reused from an independent top-level window such as <c>SettingsWindow</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ConfirmDialog : Window
{
	private bool _confirmed;

	public ConfirmDialog()
	{
		InitializeComponent();
	}

	public static async Task<bool> ShowAsync(Window owner, string message, string okLabel = "OK", string cancelLabel = "Cancel")
	{
		var dialog = new ConfirmDialog();
		dialog.MessageText.Text    = message;
		dialog.OkButton.Content    = okLabel;
		dialog.CancelButton.Content = cancelLabel;
		await dialog.ShowDialog(owner);
		return dialog._confirmed;
	}

	private void OnOk(object? sender, RoutedEventArgs e)
	{
		_confirmed = true;
		Close();
	}

	private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
