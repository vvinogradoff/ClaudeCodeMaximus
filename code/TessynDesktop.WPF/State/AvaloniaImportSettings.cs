namespace TessynDesktop.WPF.State;

/// <summary>Remembers that the one-time import of the Avalonia client's settings was done (settings section <c>avaloniaImport</c>); the Settings window stops offering it.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.Settings.AvaloniaImportSection)]
public sealed class AvaloniaImportSettings
{
	public bool Done { get; set; }

	public DateTime? ImportedAtUtc { get; set; }
}
