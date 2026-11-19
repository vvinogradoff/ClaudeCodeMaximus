using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// What to remember about one preview window's place (FR.29.16). A restored window is remembered by its <b>actual</b> rectangle, so a window Windows arranged
/// (Snap to a half or a quarter, Snap layouts) is remembered where the user sees it, not where it was before the snap. A maximized (or minimized) window is
/// remembered by the last actual rectangle it had while restored; <see cref="Observe"/> keeps that current, and a window that was never seen restored falls
/// back to the rectangle behind its state. One instance per window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewWindowPlacementCapture
{
	private readonly IWindowPlacementApi _api;
	private readonly Func<IntPtr, MonitorInfo?> _monitorOf;
	private CapturedPlacement? _lastRestored;

	/// <param name="api">The Win32 calls.</param>
	/// <param name="monitorOf">The monitor a window is on now; null (the default) when monitors are not tracked.</param>
	public PreviewWindowPlacementCapture(IWindowPlacementApi api, Func<IntPtr, MonitorInfo?>? monitorOf = null)
	{
		_api = api;
		_monitorOf = monitorOf ?? (_ => null);
	}

	/// <summary>Notes the window's rectangle when it is restored; call it whenever the window moved or changed size.</summary>
	public void Observe(IntPtr hwnd) => ReadRestored(hwnd);

	/// <summary>The placement to remember now; null when the window cannot be read.</summary>
	public CapturedPlacement? Capture(IntPtr hwnd)
	{
		var restored = ReadRestored(hwnd);
		if (restored != null)
			return restored;

		var behind = _api.Capture(hwnd);
		if (behind == null)
			return null;

		if (_lastRestored == null)
			return new CapturedPlacement(behind, _api.GetDpi(hwnd), _monitorOf(hwnd));

		var rectangle = _lastRestored.Placement.Clone();
		rectangle.Maximized = behind.Maximized;
		var now = _monitorOf(hwnd);
		if (!behind.Maximized || now == null || _lastRestored.Monitor == null || now.DeviceName == _lastRestored.Monitor.DeviceName)
			return new CapturedPlacement(rectangle, _lastRestored.Dpi, _lastRestored.Monitor);

		// Maximized on another monitor than the rectangle belongs to (Win+Shift+Arrow): the rectangle moves along, so it reopens maximized there.
		return new CapturedPlacement(Translate(rectangle, _lastRestored.Monitor, _lastRestored.Dpi, now), now.Dpi > 0 ? now.Dpi : _api.GetDpi(hwnd), now);
	}

	/// <summary>The rectangle on <paramref name="to"/>: the same offset from the work-area origin, the size converted by the ratio of the scales when both are known, clamped into the work area.</summary>
	private static WindowPlacementModel Translate(WindowPlacementModel rectangle, MonitorInfo from, int fromDpi, MonitorInfo to)
	{
		var area = to.WorkArea;
		var ratio = fromDpi > 0 && to.Dpi > 0 ? (double)to.Dpi / fromDpi : 1.0;
		var width = Math.Min((int)Math.Round(rectangle.Width * ratio), area.Width);
		var height = Math.Min((int)Math.Round(rectangle.Height * ratio), area.Height);
		var left = Math.Clamp(area.Left + rectangle.Left - from.WorkArea.Left, area.Left, area.Right - width);
		var top = Math.Clamp(area.Top + rectangle.Top - from.WorkArea.Top, area.Top, area.Bottom - height);
		return new WindowPlacementModel { Left = left, Top = top, Width = width, Height = height, Maximized = rectangle.Maximized };
	}

	private CapturedPlacement? ReadRestored(IntPtr hwnd)
	{
		if (!_api.IsRestored(hwnd))
			return null;

		var actual = _api.GetActualRectangle(hwnd);
		if (actual == null)
			return null;

		actual.Maximized = false;
		_lastRestored = new CapturedPlacement(actual, _api.GetDpi(hwnd), _monitorOf(hwnd));
		return _lastRestored;
	}
}
