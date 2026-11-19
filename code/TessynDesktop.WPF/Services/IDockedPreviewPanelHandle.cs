using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>A docked panel's handle (FR.29.22): besides what the router sees of a window, the layout the panel has now, to save a pinned panel's own place with the open-panel list.</summary>
/// <remarks>Created by Claude</remarks>
public interface IDockedPreviewPanelHandle : IPreviewWindowHandle
{
	/// <summary>The panel's layout now; null before it is shown and after it closed.</summary>
	PreviewDockLayoutModel? CurrentLayout { get; }
}
