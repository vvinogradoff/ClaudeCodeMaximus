namespace TessynDesktop.WPF.Views;

/// <summary>
/// Content of a docked panel that reacts to its panel becoming the selected tab of its pane (FR.6.11, FR.1.16): the layout core calls <see cref="OnPanelSelected"/>
/// when the panel is brought forward or its tab is selected, besides the first load.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IPanelSelectionAware
{
	void OnPanelSelected();
}
