namespace TessynDesktop.WPF.State;

/// <summary>Layout choices of the main window (settings section <c>layout</c>); the title bar's chevron changes them.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.Layout)]
public sealed class ShellLayoutSettings
{
	/// <summary>The left panel (tree, recent, tags) is collapsed (FR.6.3 chevron).</summary>
	public bool TreeCollapsed { get; set; }
}
