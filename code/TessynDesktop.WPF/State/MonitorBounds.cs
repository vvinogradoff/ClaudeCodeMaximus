namespace TessynDesktop.WPF.State;

/// <summary>A monitor's work area in physical pixels.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record MonitorBounds(int Left, int Top, int Width, int Height)
{
	public int Right => Left + Width;

	public int Bottom => Top + Height;
}
