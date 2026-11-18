namespace TessynDesktop.WPF.Services;

/// <summary>A preview window closed: by the user, or by the router (auto-close, mode off, group removed, app exit).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewWindowClosedEventArgs : EventArgs
{
	public PreviewWindowClosedEventArgs(bool byUser)
	{
		ByUser = byUser;
	}

	/// <summary>True when the user closed the window (its close button, Alt+F4, the taskbar).</summary>
	public bool ByUser { get; }
}
