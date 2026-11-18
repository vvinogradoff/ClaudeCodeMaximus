namespace TessynDesktop.WPF.State;

/// <summary>A monitor: its device name, its work area in workspace coordinates (as <see cref="Win32WindowPlacement.GetMonitors"/>) and whether it is the primary one.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record MonitorInfo(string DeviceName, MonitorBounds WorkArea, bool IsPrimary);
