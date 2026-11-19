using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>Creates docked panels for the router (FR.29.17–FR.29.20); the WPF implementation lives in the view layer (AvalonDock), tests use a fake.</summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewDockFactory
{
	/// <summary>
	/// Creates a docked panel without showing it. <paramref name="group"/> null creates the default Preview panel (FR.29.18; no Pin, no Auto-close). A group
	/// panel (<paramref name="pinned"/> false) opens at the group's remembered layout (FR.29.21) and remembers its changes; a pinned panel reopened after a
	/// restart (FR.29.22) opens at <paramref name="ownLayout"/> and never writes the group's layout; it keeps its saved <paramref name="panelId"/> (a new id when null). The handle is the same interface as an external
	/// window's, so the router treats both alike; its <c>CurrentPlacement</c> is null (a panel's layout is saved by the dock layer itself).
	/// </summary>
	IPreviewWindowHandle Create(PreviewGroupModel? group, bool pinned, PreviewDockLayoutModel? ownLayout, string? panelId = null);
}