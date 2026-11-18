using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>Creates preview windows for the router; the WPF implementation lives in the view layer, tests use a fake.</summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewWindowFactory
{
	/// <summary>
	/// Creates a window for <paramref name="group"/> without showing it. A group window (<paramref name="pinned"/> false) opens at the group's remembered
	/// placement (FR.29.5, FR.29.6) and remembers its moves; a pinned window reopened after a restart (FR.29.11) opens at <paramref name="ownPlacement"/>
	/// and never writes the group's placement.
	/// </summary>
	IPreviewWindowHandle Create(PreviewGroupModel group, bool pinned, PreviewPlacementModel? ownPlacement);
}
