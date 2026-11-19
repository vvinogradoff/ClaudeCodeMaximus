using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IFloatingPanelPlacement"/> over <see cref="PreviewWindowPlacementCapture"/> and <see cref="PreviewPlacementPlanner"/>, the placement services of the preview windows
/// (FR.29.16): a floating panel is captured, remembered and restored by the same rules, near the primary monitor instead of the largest non-main one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FloatingPanelPlacement : IFloatingPanelPlacement
{
	private readonly IWindowPlacementApi _api;
	private readonly IMonitorWatcher _monitors;
	private readonly PreviewPlacementPlanner _planner;

	public FloatingPanelPlacement(IWindowPlacementApi api, IMonitorWatcher monitors, PreviewPlacementPlanner planner)
	{
		_api = api;
		_monitors = monitors;
		_planner = planner;
	}

	public bool IsSettling => _monitors.IsSettling;

	public PreviewPlacementModel? Capture(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
			return null;

		var capture = new PreviewWindowPlacementCapture(_api, h => _monitors.Monitors.FirstOrDefault(m => m.DeviceName == _monitors.MonitorOf(h)));
		var captured = capture.Capture(hwnd);
		return captured == null ? null : _planner.Remember(captured.Placement, captured.Monitor, captured.Dpi);
	}

	public WindowPlacementModel Plan(PreviewPlacementModel saved)
	{
		var monitors = _monitors.Monitors;
		var pixels = _planner.Plan(saved, monitors, mainWindowMonitor: null, nearMainWindow: true);
		if (saved.Dpi <= 0)
			return pixels;   // older data: device-independent already, only clamped

		var dpi = DpiAt(pixels, monitors) ?? saved.Dpi;
		return Scale(pixels, dpi);
	}

	public WindowPlacementModel ToLibraryUnits(PreviewPlacementModel pixels) => pixels.Dpi <= 0 ? pixels.ToWindowPlacement() : Scale(pixels.ToWindowPlacement(), pixels.Dpi);

	private int? DpiAt(WindowPlacementModel rectangle, IReadOnlyList<MonitorInfo> monitors)
	{
		var x = rectangle.Left + rectangle.Width / 2;
		var y = rectangle.Top + rectangle.Height / 2;
		var monitor = monitors.FirstOrDefault(m => x >= m.WorkArea.Left && x < m.WorkArea.Right && y >= m.WorkArea.Top && y < m.WorkArea.Bottom);
		return monitor is { Dpi: > 0 } ? monitor.Dpi : null;
	}

	/// <summary>Pixels to device-independent units at <paramref name="dpi"/> (96 = 100 %).</summary>
	private WindowPlacementModel Scale(WindowPlacementModel pixels, int dpi)
	{
		var factor = (double)Constants.Previews.BaseDpi / dpi;
		return new WindowPlacementModel
		{
			Left = (int)Math.Round(pixels.Left * factor),
			Top = (int)Math.Round(pixels.Top * factor),
			Width = (int)Math.Round(pixels.Width * factor),
			Height = (int)Math.Round(pixels.Height * factor),
		};
	}
}
