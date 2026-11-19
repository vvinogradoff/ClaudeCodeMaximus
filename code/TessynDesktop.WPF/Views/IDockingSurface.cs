using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// What the docked preview panels need of the main window's docking area (FR.29.19): the layout model they are placed in, the session pane's document pane every side is
/// relative to, the size of the area (for the first-time 40 %) and a notice when the layout changes. The main window's <see cref="DockingArea"/> implements it; tests use a
/// model-only fake, so the panels are tested without a window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDockingSurface
{
	/// <summary>AvalonDock's layout model.</summary>
	LayoutRoot Root { get; }

	/// <summary>The document pane that holds the session pane, the one non-closable document.</summary>
	LayoutDocumentPane DocumentPane { get; }

	/// <summary>The width of the docking area in device-independent pixels; 0 before it is laid out.</summary>
	double AreaWidth { get; }

	/// <summary>The height of the docking area in device-independent pixels; 0 before it is laid out.</summary>
	double AreaHeight { get; }

	/// <summary>Hides or shows the floating panels' windows (the screensaver cover does not reach them); a window created while hidden starts hidden.</summary>
	void SetFloatingVisible(bool visible);

	/// <summary>Something in the layout changed (a dock move, a resize, a tab move, auto-hide, float or dock).</summary>
	event EventHandler? LayoutChanged;
}
