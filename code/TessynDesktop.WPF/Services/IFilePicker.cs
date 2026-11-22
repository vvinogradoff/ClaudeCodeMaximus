namespace TessynDesktop.WPF.Services;

/// <summary>The client's own open-file dialog (the daemon never opens a dialog on the user's behalf).</summary>
/// <remarks>Created by Claude</remarks>
public interface IFilePicker
{
	/// <summary>The chosen file, or null when the user cancelled. <paramref name="filter"/> is a dialog filter (<c>Label|pattern|...</c>). Called on the UI thread.</summary>
	Task<string?> PickAsync(string title, string filter, string? initialPath = null);
}
