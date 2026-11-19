namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>A floating panel's rectangle as the docking test hook reports it (device-independent pixels).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record DockingHookRect(double Left, double Top, double Width, double Height);
