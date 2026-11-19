using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Keeps a move that Windows forces out of what is remembered for a floating panel (FR.29.21, FR.6.18, FR.29.23): a place read while Windows rearranges windows after a display change
/// (<see cref="IFloatingPanelPlacement.IsSettling"/>) is not remembered, and neither is the same place read again afterwards, until the user really moves the window. One instance
/// per panel; contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FloatingPlacementGuard
{
	private PreviewPlacementModel? _good;
	private PreviewPlacementModel? _forced;

	/// <summary>The place to remember for what was read now (<paramref name="read"/> null: the window cannot be read, nothing is remembered); the last good one while the read is forced.</summary>
	public PreviewPlacementModel? Filter(PreviewPlacementModel? read, bool settling)
	{
		if (read == null)
			return _good;

		if (settling)
		{
			_forced = read;
			return _good ?? read;
		}

		if (_forced != null && SameRectangle(read, _forced))
			return _good ?? read;   // still where Windows put it

		_forced = null;
		_good = read;
		return read;
	}

	private static bool SameRectangle(PreviewPlacementModel a, PreviewPlacementModel b) => a.Left == b.Left && a.Top == b.Top && a.Width == b.Width && a.Height == b.Height;
}
