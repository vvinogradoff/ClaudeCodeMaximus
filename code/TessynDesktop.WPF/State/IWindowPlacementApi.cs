namespace TessynDesktop.WPF.State;

/// <summary>The Win32 calls behind a window's placement (FR.29.16), so that the decisions on top of them can be tested with a fake. Rectangles are physical pixels in workspace coordinates (origin = top-left of the primary work area).</summary>
/// <remarks>Created by Claude</remarks>
public interface IWindowPlacementApi
{
	/// <summary>The window's normal (restored) rectangle as <c>GetWindowPlacement</c> keeps it and whether it is maximized; null when it cannot be read. After a Snap this is the rectangle from before the snap.</summary>
	WindowPlacementModel? Capture(IntPtr hwnd);

	/// <summary>True when the window is neither minimized nor maximized.</summary>
	bool IsRestored(IntPtr hwnd);

	/// <summary>Where the window actually is now (also when Windows arranged it); null when it cannot be read.</summary>
	WindowPlacementModel? GetActualRectangle(IntPtr hwnd);

	/// <summary>The DPI of the monitor the window is on; 0 when unknown.</summary>
	int GetDpi(IntPtr hwnd);

	/// <summary>Moves and sizes the window to <paramref name="model"/> (maximized when so remembered).</summary>
	void Apply(IntPtr hwnd, WindowPlacementModel model);
}
