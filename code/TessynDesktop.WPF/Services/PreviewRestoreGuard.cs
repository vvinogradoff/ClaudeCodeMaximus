using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Keeps a restored preview window at its remembered pixels (FR.29.16). Placing the window on a monitor of another scale makes Windows send <c>WM_DPICHANGED</c>
/// with a rectangle scaled by the ratio of the scales, and WPF applies it; during the restore phase (from the window handle to the first frame) the remembered
/// rectangle is applied again instead, a few times at most. After <see cref="End"/> nothing is done, so dragging the window across monitors later rescales normally.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRestoreGuard
{
	private readonly IWindowPlacementApi _api;
	private WindowPlacementModel? _planned;
	private int _applied;

	public PreviewRestoreGuard(IWindowPlacementApi api)
	{
		_api = api;
	}

	/// <summary>True from <see cref="Restore"/> until <see cref="End"/>.</summary>
	public bool Active => _planned != null;

	/// <summary>
	/// Starts the restore phase and then applies <paramref name="planned"/>. The phase begins first because placing the window on a monitor of another scale
	/// raises <c>WM_DPICHANGED</c> synchronously inside the apply, and that is the change the guard exists for.
	/// </summary>
	public void Restore(IntPtr hwnd, WindowPlacementModel planned)
	{
		_planned = planned.Clone();
		_applied = 0;
		_api.Apply(hwnd, planned);
	}

	/// <summary>The window's DPI changed; applies the planned rectangle again while the restore phase lasts. Returns whether it did.</summary>
	public bool OnDpiChanged(IntPtr hwnd)
	{
		if (_planned == null || _applied >= Constants.PreviewWindow.MaxRestoreReapplies)
			return false;

		_applied++;
		_api.Apply(hwnd, _planned);
		return true;
	}

	/// <summary>The restore phase is over (the first frame is up, or the user started to move or size the window).</summary>
	public void End() => _planned = null;
}
