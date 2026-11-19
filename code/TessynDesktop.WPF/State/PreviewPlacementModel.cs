namespace TessynDesktop.WPF.State;

/// <summary>A group window's remembered placement: the normal rectangle in physical pixels (workspace coordinates), whether it was maximized, and the monitor it was on.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewPlacementModel
{
	public int Left { get; set; }

	public int Top { get; set; }

	public int Width { get; set; }

	public int Height { get; set; }

	public bool Maximized { get; set; }

	/// <summary>Device name of the monitor the window was on, e.g. <c>\\.\DISPLAY2</c>.</summary>
	public string? MonitorDeviceName { get; set; }

	/// <summary>Origin of that monitor's work area when saved, to follow a monitor that moved in the layout.</summary>
	public int MonitorLeft { get; set; }

	public int MonitorTop { get; set; }

	/// <summary>DPI of that monitor when saved (96 = 100 %); 0 for a placement saved before it was kept: the size is then used as it is (FR.29.16).</summary>
	public int Dpi { get; set; }

	public WindowPlacementModel ToWindowPlacement() => new() { Left = Left, Top = Top, Width = Width, Height = Height, Maximized = Maximized };
}
