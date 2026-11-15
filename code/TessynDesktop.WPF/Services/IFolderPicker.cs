namespace TessynDesktop.WPF.Services;

/// <summary>The client's own folder dialog (the daemon never opens a dialog on the user's behalf).</summary>
/// <remarks>Created by Claude</remarks>
public interface IFolderPicker
{
	/// <summary>The chosen folder, or null when the user cancelled. Called on the UI thread.</summary>
	Task<string?> PickAsync(string title);
}
