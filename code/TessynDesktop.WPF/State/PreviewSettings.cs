namespace TessynDesktop.WPF.State;

/// <summary>The preview groups (settings section <c>previews</c>); edited by Settings → Previews and by a window's Auto-close button.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.Previews)]
public sealed class PreviewSettings
{
	/// <summary>Null = never edited: the default groups apply.</summary>
	public List<PreviewGroupModel>? Groups { get; set; }
}
