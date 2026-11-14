namespace TessynDesktop.WPF.Transcript;

/// <summary>Position of the current find match.</summary>
/// <param name="Current">1-based index of the current match; 0 when none.</param>
/// <param name="Count">Number of matching entries.</param>
/// <remarks>Created by Claude</remarks>
public sealed record FindStatusEventArgs(int Current, int Count);
