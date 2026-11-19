namespace TessynDesktop.WPF.State;

/// <summary>The docked shell's saved arrangement (section <c>shellLayout</c>, FR.6.18): the serialized docking layout of the five shell panels and its version.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.ShellLayout)]
public sealed class DockedShellLayoutSettings
{
	/// <summary><see cref="Constants.DockedShell.LayoutVersion"/> of the layout below; another value means the layout is ignored.</summary>
	public int Version { get; set; }

	/// <summary>The docking layout as the docking library serializes it (XML); null = never saved (the default layout, with the old tree width and collapse taken over once).</summary>
	public string? Layout { get; set; }
}
