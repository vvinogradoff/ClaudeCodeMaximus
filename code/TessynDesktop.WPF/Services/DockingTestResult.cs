namespace TessynDesktop.WPF.Services;

/// <summary>The outcome of one hook operation: done, or refused with the reason (the layout is unchanged then).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DockingTestResult(bool Ok, string? Error = null)
{
	public static readonly DockingTestResult Success = new(true);
}
