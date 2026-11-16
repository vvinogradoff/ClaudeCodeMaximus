namespace TessynDesktop.Models;

/// <summary>Where one tag chip (with its margin) sits inside the chip panel after layout.</summary>
/// <remarks>Created by Claude</remarks>
public readonly record struct TagChipBox(double Top, double Height, double Width = 0);
