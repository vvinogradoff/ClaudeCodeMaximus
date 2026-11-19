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
	private CapturedPlacement? _lastRestored;

	public PreviewWindowPlacementCapture(IWindowPlacementApi api)
	{
		_api = api;
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
			return new CapturedPlacement(behind, _api.GetDpi(hwnd));

		var rectangle = _lastRestored.Placement.Clone();
		rectangle.Maximized = behind.Maximized;
		return new CapturedPlacement(rectangle, _lastRestored.Dpi);
	}

	private CapturedPlacement? ReadRestored(IntPtr hwnd)
	{
		if (!_api.IsRestored(hwnd))
			return null;

		var actual = _api.GetActualRectangle(hwnd);
		if (actual == null)
			return null;

		actual.Maximized = false;
		_lastRestored = new CapturedPlacement(actual, _api.GetDpi(hwnd));
		return _lastRestored;
	}
}
