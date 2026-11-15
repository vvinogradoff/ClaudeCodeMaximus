using Microsoft.Win32;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IFolderPicker"/> with the system folder dialog, owned by the main window.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfFolderPicker : IFolderPicker
{
	public Task<string?> PickAsync(string title)
	{
		var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
		var owner = System.Windows.Application.Current?.MainWindow;
		var accepted = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
		return Task.FromResult(accepted == true ? dialog.FolderName : null);
	}
}
