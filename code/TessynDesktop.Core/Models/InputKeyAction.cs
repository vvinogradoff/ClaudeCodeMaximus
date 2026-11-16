namespace TessynDesktop.Models;

/// <summary>The outcome of <see cref="TessynDesktop.Services.InputNavigator.KeyAction"/>.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Direction">-1 (previous) or 1 (next) for <see cref="InputKeyActionKind.Navigate"/>; 0 otherwise.</param>
/// <param name="ScrollDelta">Pixels to scroll (negative = up) for <see cref="InputKeyActionKind.Scroll"/>; 0 otherwise.</param>
/// <remarks>Created by Claude</remarks>
public sealed record InputKeyAction(InputKeyActionKind Kind, int Direction = 0, double ScrollDelta = 0)
{
	public static InputKeyAction None { get; } = new(InputKeyActionKind.None);
}
