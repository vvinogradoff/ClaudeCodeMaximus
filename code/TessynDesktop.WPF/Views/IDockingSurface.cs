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

	/// <summary>
	/// Brings a panel forward for a new item without taking the keyboard focus (FR.29.17, decision 3): selects a tab that sits behind another, slides an auto-hidden panel out over the
	/// session pane, lifts a floating panel's window without activating it.
	/// </summary>
	void Reveal(LayoutAnchorable panel);

	/// <summary>The window handle of the floating window that holds <paramref name="panel"/>; <see cref="IntPtr.Zero"/> when it does not float or has no window yet.</summary>
	IntPtr FloatingWindowHandle(LayoutAnchorable panel);

	/// <summary>Something in the layout changed (a dock move, a resize, a tab move, auto-hide, float or dock).</summary>
	event EventHandler? LayoutChanged;
}
