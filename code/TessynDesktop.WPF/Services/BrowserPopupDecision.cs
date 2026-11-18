namespace TessynDesktop.WPF.Services;

/// <summary>What the browser does with a page's request for a new window.</summary>
/// <remarks>Created by Claude</remarks>
public enum BrowserPopupDecision
{
	/// <summary>Too many popups are open: the request is handled and dropped.</summary>
	Drop,

	/// <summary>Opens the popup without taking the focus (a script or timer asked for it).</summary>
	ShowInactive,

	/// <summary>Opens the popup and activates it (the user clicked something, e.g. "Sign in").</summary>
	ShowActive,
}
