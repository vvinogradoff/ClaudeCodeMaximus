namespace TessynDesktop.WPF.State;

/// <summary>Where the main window was, in physical pixels (the unit of <c>GetWindowPlacement</c>), and whether it was maximized.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WindowPlacementModel
{
	public int Left { get; set; }

	public int Top { get; set; }

	public int Width { get; set; }

	public int Height { get; set; }

	public bool Maximized { get; set; }

	public WindowPlacementModel Clone() => (WindowPlacementModel)MemberwiseClone();
}
