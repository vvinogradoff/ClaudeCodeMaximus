namespace TessynDesktop.WPF.State;

/// <summary>
/// Pure logic that makes a remembered window placement safe for the monitors present now: a window that is off-screen
/// (a monitor was unplugged or rearranged) moves to the primary monitor, a window larger than its monitor shrinks to it, and a
/// partly hidden window is shifted fully into the monitor it overlaps most.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WindowPlacementClamper
{
	/// <summary>The clamped placement; null when there is nothing to restore.</summary>
	/// <param name="saved">The remembered placement.</param>
	/// <param name="monitors">Work areas of the monitors present; empty means "unknown", the placement is kept as is.</param>
	/// <param name="primary">The primary monitor's work area.</param>
	public WindowPlacementModel? Clamp(WindowPlacementModel? saved, IReadOnlyList<MonitorBounds> monitors, MonitorBounds primary)
	{
		if (saved == null || saved.Width <= 0 || saved.Height <= 0)
			return null;

		if (monitors.Count == 0)
			return saved.Clone();

		var overlapped = BestMonitor(saved, monitors);
		var target = overlapped ?? primary;
		var width = Math.Clamp(saved.Width, Math.Min(Constants.Limits.MinWindowWidth, target.Width), target.Width);
		var height = Math.Clamp(saved.Height, Math.Min(Constants.Limits.MinWindowHeight, target.Height), target.Height);

		int left;
		int top;
		if (overlapped == null)
		{
			left = target.Left + (target.Width - width) / 2;
			top = target.Top + (target.Height - height) / 2;
		}
		else
		{
			left = Math.Clamp(saved.Left, target.Left, target.Right - width);
			top = Math.Clamp(saved.Top, target.Top, target.Bottom - height);
		}

		return new WindowPlacementModel { Left = left, Top = top, Width = width, Height = height, Maximized = saved.Maximized };
	}

	/// <summary>The monitor sharing the largest visible area with the window, if enough of it is visible on both axes.</summary>
	private static MonitorBounds? BestMonitor(WindowPlacementModel window, IReadOnlyList<MonitorBounds> monitors)
	{
		MonitorBounds? best = null;
		long bestArea = 0;
		foreach (var monitor in monitors)
		{
			var overlapWidth = Math.Min(window.Left + window.Width, monitor.Right) - Math.Max(window.Left, monitor.Left);
			var overlapHeight = Math.Min(window.Top + window.Height, monitor.Bottom) - Math.Max(window.Top, monitor.Top);
			if (overlapWidth < Constants.Limits.MinWindowVisiblePixels || overlapHeight < Constants.Limits.MinWindowVisiblePixels)
				continue;

			var area = (long)overlapWidth * overlapHeight;
			if (area > bestArea)
			{
				bestArea = area;
				best = monitor;
			}
		}

		return best;
	}
}
