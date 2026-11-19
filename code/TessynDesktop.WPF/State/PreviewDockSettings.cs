namespace TessynDesktop.WPF.State;

/// <summary>
/// The docked panels' state (section <c>previewDock</c>, FR.29.21, FR.29.22): the layout per group id (and for the default Preview panel) and the panels
/// open now. Independent of <see cref="PreviewWindowsSettings"/>: one monitor and external monitors are remembered separately.
/// </summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.PreviewDock)]
public sealed class PreviewDockSettings
{
	public Dictionary<string, PreviewDockLayoutModel> Layouts { get; set; } = new();

	public List<OpenPreviewPanelModel> OpenPanels { get; set; } = [];
}