namespace TessynDesktop.WPF.State;

/// <summary>A window's placement as captured (FR.29.16): the rectangle to remember and the DPI of the monitor it was on (0 = unknown) and that monitor (null when unknown); rectangle, DPI and monitor always belong together.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CapturedPlacement(WindowPlacementModel Placement, int Dpi, MonitorInfo? Monitor = null);
