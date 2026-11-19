namespace TessynDesktop.WPF.Services;

/// <summary>
/// What the docking test hook can do to the docking area (FR.6.22, FR.29.26), for the five shell panels and the docked preview panels by their content id. Called on the UI
/// thread. A refused call changes nothing and says why. Implemented by the main window's docking area; tests use a fake.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDockingTestOperations
{
	/// <summary>Every panel's state, the Session view included.</summary>
	IReadOnlyList<DockingPanelState> State();

	/// <summary>Docks the panel to a side of the session area (<c>left</c>, <c>right</c>, <c>top</c>, <c>bottom</c>).</summary>
	DockingTestResult Dock(string panel, string side);

	/// <summary>Stacks the panel as a tab with another panel, or with the session area (<paramref name="withPanel"/> is <c>session</c>).</summary>
	DockingTestResult Tab(string panel, string withPanel);

	/// <summary>Floats the panel; <paramref name="rect"/> null keeps the library's default rectangle.</summary>
	DockingTestResult Float(string panel, DockingFloatRect? rect);

	/// <summary>Auto-hides the pane that holds the panel, or restores it.</summary>
	DockingTestResult AutoHide(string panel, bool on);

	/// <summary>Slides an auto-hidden panel out.</summary>
	DockingTestResult SlideOut(string panel);

	/// <summary>Closes the panel (the Session view is refused).</summary>
	DockingTestResult Close(string panel);

	/// <summary>Shows the panel the way the Panels menu does: at its last place, or forward when open.</summary>
	DockingTestResult Show(string panel);

	/// <summary>Selects the panel's tab, slides it out when auto-hidden, raises it when floating.</summary>
	DockingTestResult BringForward(string panel);

	/// <summary>Reset layout's action, after the confirmation (which the test skips).</summary>
	DockingTestResult Reset();
}
