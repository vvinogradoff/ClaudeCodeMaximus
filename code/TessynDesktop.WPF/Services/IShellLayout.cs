namespace TessynDesktop.WPF.Services;

/// <summary>
/// The docked shell as view models and shell commands see it (FR.6.14–FR.6.16, FR.6.19): which panels are open, bringing one forward, closing, auto-hide and
/// Reset layout. The WPF implementation lives in the view layer (the docking area); a null-safe host forwards to it once the main window exists. Called on the UI thread.
/// None of these calls moves the keyboard focus (FR.6.20).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IShellLayout
{
	/// <summary>The panel is part of the layout (docked, tabbed, auto-hidden or floating), not closed.</summary>
	bool IsOpen(string panelId);

	/// <summary>Shows a closed panel at its last place, or brings an open one forward: selects its tab, slides it out when auto-hidden, raises it when floating.</summary>
	void BringForward(string panelId);

	/// <summary>Closes a closable panel (Sessions, Recent, Tags, Focus); the Session view is never closed.</summary>
	void Close(string panelId);

	/// <summary>True when the pane that holds the panel can be auto-hidden now (the panel is open, docked and not floating).</summary>
	bool CanToggleAutoHide(string panelId);

	/// <summary>True when the pane that holds the panel is auto-hidden.</summary>
	bool IsAutoHidden(string panelId);

	/// <summary>Toggles auto-hide of the pane that holds the panel; does nothing when <see cref="CanToggleAutoHide"/> is false.</summary>
	void ToggleAutoHide(string panelId);

	/// <summary>Restores the default layout (FR.6.12, FR.6.19). The caller asks the user first.</summary>
	void ResetLayout();

	/// <summary>Raised after any change of the layout (a panel opened, closed, moved, tabbed, floated, auto-hidden).</summary>
	event EventHandler? Changed;
}
