namespace TessynDesktop.WPF.State;

/// <summary>Runtime state of the preview windows (settings section <c>previewWindows</c>); written by the app, never by the Settings window.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.PreviewWindows)]
public sealed class PreviewWindowsSettings
{
	/// <summary>The title-bar toggle (FR.29.1); on by default.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Remembered placements by group id.</summary>
	public Dictionary<string, PreviewPlacementModel> Placements { get; set; } = new();
}
