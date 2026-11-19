using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>Pure logic of where a preview group's window opens and what is remembered about it (FR.29.5, FR.29.6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewPlacementPlanner
{
	private static readonly MonitorBounds FallbackBounds = new(0, 0, Constants.Previews.TestMonitorWidth, Constants.Previews.TestMonitorHeight);

	private readonly WindowPlacementClamper _clamper = new();

	/// <summary>Where a group window opens.</summary>
	/// <param name="remembered">The group's remembered placement; null when none.</param>
	/// <param name="monitors">The monitors present.</param>
	/// <param name="mainWindowMonitor">Device name of the monitor holding the main window.</param>
	public WindowPlacementModel Plan(PreviewPlacementModel? remembered, IReadOnlyList<MonitorInfo> monitors, string? mainWindowMonitor)
	{
		var target = TargetOf(monitors, mainWindowMonitor);
		var targetDpi = monitors.FirstOrDefault(m => m.WorkArea == target)?.Dpi ?? 0;
		if (remembered == null || remembered.Width <= 0 || remembered.Height <= 0)
			return First(target);

		var rectangle = remembered.ToWindowPlacement();
		var home = remembered.MonitorDeviceName == null ? null : monitors.FirstOrDefault(m => m.DeviceName == remembered.MonitorDeviceName);
		if (remembered.MonitorDeviceName != null && home == null)
		{
			// Windows may have renamed the monitor: the one whose work area starts where the remembered one did is the same monitor (FR.29.6).
			home = monitors.FirstOrDefault(m => m.WorkArea.Left == remembered.MonitorLeft && m.WorkArea.Top == remembered.MonitorTop);
			if (home == null)
				return Centred(Scaled(rectangle, remembered.Dpi, targetDpi), target);
		}

		if (home != null)
		{
			// The monitor moved in the layout since the window was saved: the window moves with it.
			rectangle.Left += home.WorkArea.Left - remembered.MonitorLeft;
			rectangle.Top += home.WorkArea.Top - remembered.MonitorTop;

			// The same monitor now has another scale than when it was saved: the pixels follow it (FR.29.16).
			if (remembered.Dpi > 0 && home.Dpi > 0 && remembered.Dpi != home.Dpi)
			{
				rectangle = Scaled(rectangle, remembered.Dpi, home.Dpi);
				rectangle.Left = home.WorkArea.Left + (int)Math.Round((double)(remembered.Left - remembered.MonitorLeft) * home.Dpi / remembered.Dpi);
				rectangle.Top = home.WorkArea.Top + (int)Math.Round((double)(remembered.Top - remembered.MonitorTop) * home.Dpi / remembered.Dpi);
			}
		}

		var workAreas = monitors.Select(m => m.WorkArea).ToList();
		return _clamper.Clamp(rectangle, workAreas, target, Constants.Previews.MinWindowWidth, Constants.Previews.MinWindowHeight) ?? First(target);
	}

	/// <summary>What to remember for a window now at <paramref name="current"/> on <paramref name="monitor"/>.</summary>
	public PreviewPlacementModel Remember(WindowPlacementModel current, MonitorInfo? monitor, int dpi = 0) => new()
	{
		Left = current.Left,
		Top = current.Top,
		Width = current.Width,
		Height = current.Height,
		Maximized = current.Maximized,
		MonitorDeviceName = monitor?.DeviceName,
		MonitorLeft = monitor?.WorkArea.Left ?? 0,
		MonitorTop = monitor?.WorkArea.Top ?? 0,
		Dpi = dpi > 0 ? dpi : monitor?.Dpi ?? 0,
	};

	/// <summary>The size converted by the ratio of two scales; as it is when either is unknown or they are equal. The position is the caller's to set (it centres or clamps).</summary>
	private static WindowPlacementModel Scaled(WindowPlacementModel rectangle, int fromDpi, int toDpi)
	{
		if (fromDpi <= 0 || toDpi <= 0 || fromDpi == toDpi)
			return rectangle;

		var scaled = rectangle.Clone();
		scaled.Width = (int)Math.Round((double)rectangle.Width * toDpi / fromDpi);
		scaled.Height = (int)Math.Round((double)rectangle.Height * toDpi / fromDpi);
		return scaled;
	}

	/// <summary>The largest monitor that does not hold the main window (the first on a tie); else the main window's monitor, else the primary, else the first.</summary>
	private static MonitorBounds TargetOf(IReadOnlyList<MonitorInfo> monitors, string? mainWindowMonitor)
	{
		MonitorInfo? best = null;
		foreach (var monitor in monitors)
		{
			if (monitor.DeviceName == mainWindowMonitor)
				continue;

			if (best == null || Area(monitor.WorkArea) > Area(best.WorkArea))
				best = monitor;
		}

		best ??= monitors.FirstOrDefault(m => m.DeviceName == mainWindowMonitor) ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
		return best?.WorkArea ?? FallbackBounds;
	}

	private static long Area(MonitorBounds bounds) => (long)bounds.Width * bounds.Height;

	/// <summary>The first window of a group: 60 % of the target's work area each way (not below the minimum), centred.</summary>
	private static WindowPlacementModel First(MonitorBounds target)
	{
		var width = Math.Clamp(target.Width * Constants.Previews.FirstWindowPercent / 100, Math.Min(Constants.Previews.MinWindowWidth, target.Width), target.Width);
		var height = Math.Clamp(target.Height * Constants.Previews.FirstWindowPercent / 100, Math.Min(Constants.Previews.MinWindowHeight, target.Height), target.Height);
		return new WindowPlacementModel { Left = target.Left + (target.Width - width) / 2, Top = target.Top + (target.Height - height) / 2, Width = width, Height = height };
	}

	/// <summary>The remembered size clamped to the target and centred on it.</summary>
	private static WindowPlacementModel Centred(WindowPlacementModel size, MonitorBounds target)
	{
		var width = Math.Clamp(size.Width, Math.Min(Constants.Previews.MinWindowWidth, target.Width), target.Width);
		var height = Math.Clamp(size.Height, Math.Min(Constants.Previews.MinWindowHeight, target.Height), target.Height);
		return new WindowPlacementModel { Left = target.Left + (target.Width - width) / 2, Top = target.Top + (target.Height - height) / 2, Width = width, Height = height, Maximized = size.Maximized };
	}
}
