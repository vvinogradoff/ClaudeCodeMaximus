namespace TessynDesktop.WPF.Views;

/// <summary>
/// Where the main window's docking area registers itself (FR.29.19). The window is created after the services, so the panel factory asks this holder for the surface
/// when a panel is created, never at construction: DI needs no window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingSurfaceHost
{
	/// <summary>The docking area of the main window; null before the window exists.</summary>
	public IDockingSurface? Surface { get; private set; }

	public void Register(IDockingSurface surface) => Surface = surface;
}
