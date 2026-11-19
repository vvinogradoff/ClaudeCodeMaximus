namespace TessynDesktop.WPF.Services;

/// <summary>
/// Decides whether a keyboard focus move is the library's own focus following a layout change rather than the user's doing (FR.6.20): opening, closing, docking, floating,
/// tabbing or auto-hiding a panel must never move the focus away from the composer or the transcript; a click or a key press into a panel does.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingFocusPolicy
{
	private readonly Func<long> _nowMs;
	private long _lastInputMs = long.MinValue / 2;
	private long _lastLayoutChangeMs = long.MinValue / 2;

	public DockingFocusPolicy(Func<long>? nowMs = null)
	{
		_nowMs = nowMs ?? (() => Environment.TickCount64);
	}

	/// <summary>The user pressed a mouse button or a key.</summary>
	public void NoteInput() => _lastInputMs = _nowMs();

	/// <summary>The layout changed (a panel became active, was added, removed or moved).</summary>
	public void NoteLayoutChange() => _lastLayoutChangeMs = _nowMs();

	/// <summary>True when the focus move out of one panel into another is a consequence of a layout change and no input of the user lies in the window before it.</summary>
	public bool IsFocusSteal(bool leavesAPanel, bool entersAnotherPanel)
	{
		if (!leavesAPanel || !entersAnotherPanel)
			return false;

		var now = _nowMs();
		var window = (long)Constants.DockingChrome.FocusStealWindow.TotalMilliseconds;
		var layoutRecent = now - _lastLayoutChangeMs <= window;
		var inputRecent = now - _lastInputMs <= window;
		return layoutRecent && !inputRecent;
	}
}
