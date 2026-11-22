using Microsoft.Win32;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IFilePicker"/> with the system open-file dialog, owned by the active window.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfFilePicker : IFilePicker
{
	public Task<string?> PickAsync(string title, string filter, string? initialPath = null)
	{
		var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false, CheckFileExists = true };
		var folder = string.IsNullOrWhiteSpace(initialPath) ? null : Path.GetDirectoryName(initialPath);
		if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
			dialog.InitialDirectory = folder;

		var owner = System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
		            ?? System.Windows.Application.Current?.MainWindow;
		var accepted = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
		return Task.FromResult(accepted == true ? dialog.FileName : null);
	}
}
