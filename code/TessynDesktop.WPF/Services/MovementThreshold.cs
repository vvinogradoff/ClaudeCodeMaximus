using System.Windows;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Tells a real mouse movement from jitter: a position counts only when it is at least <c>threshold</c> pixels away from the one that last counted
/// (the first position seen only sets the starting point). Used by the screensaver, where a resting hand must not keep the screen awake and a real move must.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MovementThreshold
{
	private readonly double _threshold;
	private Point? _anchor;

	public MovementThreshold(double threshold) => _threshold = threshold;

	/// <summary>True when <paramref name="position"/> is far enough from the last counted position; that position then becomes the new anchor.</summary>
	public bool IsMovement(Point position)
	{
		if (_anchor is not { } anchor)
		{
			_anchor = position;
			return false;
		}

		if ((position - anchor).Length < _threshold)
			return false;

		_anchor = position;
		return true;
	}
}
