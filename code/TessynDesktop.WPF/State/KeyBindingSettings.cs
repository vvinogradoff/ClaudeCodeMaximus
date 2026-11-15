namespace TessynDesktop.WPF.State;

/// <summary>User overrides of command gestures (settings section <c>keyBindings</c>): command id to a gesture such as <c>Ctrl+Shift+N</c>; an empty text unbinds the command.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.KeyBindings)]
public sealed class KeyBindingSettings
{
	public Dictionary<string, string> Gestures { get; set; } = new();
}
