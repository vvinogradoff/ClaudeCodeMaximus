using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The remembering rules of one docked panel's layout (FR.29.20, FR.29.21), apart from the docking library: a change is acted on
/// <see cref="Constants.PreviewDock.LayoutSaveDelay"/> after the last one, and only when the layout really differs from what was saved last. A group panel saves its
/// group's layout (<see cref="IPreviewSettingsService.SaveDockLayout"/>); a pinned panel never writes the group's layout and raises <see cref="PinnedLayoutChanged"/>
/// instead, so the router can save the open-panel list. A change still waiting is acted on when the panel closes (<see cref="Flush"/>). Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewDockLayoutTracker : IDisposable
{
	private readonly string _groupId;
	private readonly IPreviewSettingsService _settings;
	private readonly IUiDispatcher _ui;
	private readonly Func<bool> _isPinned;
	private readonly Func<PreviewDockLayoutModel?> _capture;
	private readonly TimeSpan _delay;
	private readonly ITimer _timer;
	private PreviewDockLayoutModel? _lastSeen;
	private bool _pending;
	private bool _disposed;

	public PreviewDockLayoutTracker(
		string groupId, IPreviewSettingsService settings, IUiDispatcher ui, Func<bool> isPinned, Func<PreviewDockLayoutModel?> capture, TimeProvider? time = null, TimeSpan? delay = null)
	{
		_groupId = groupId;
		_settings = settings;
		_ui = ui;
		_isPinned = isPinned;
		_capture = capture;
		_delay = delay ?? Constants.PreviewDock.LayoutSaveDelay;
		_timer = (time ?? TimeProvider.System).CreateTimer(_ => _ui.Post(OnTimer), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	/// <summary>True once the panel is placed; changes before that are the layout being built, not the user's.</summary>
	public bool Tracking { get; set; }

	/// <summary>A pinned panel settled at a new place; the router saves the list.</summary>
	public event EventHandler? PinnedLayoutChanged;

	/// <summary>The layout may have changed (a dock move, a resize, a tab move, auto-hide, float or dock): restarts the wait when it really did.</summary>
	public void Changed()
	{
		if (!Tracking || _disposed)
			return;

		var current = _capture();
		if (current == null || Same(current, _lastSeen))
			return;

		_lastSeen = current;
		_pending = true;
		_timer.Change(_delay, Timeout.InfiniteTimeSpan);
	}

	/// <summary>The layout the panel opened with counts as seen, so that opening is not a change.</summary>
	public void Settle() => _lastSeen = _capture();

	/// <summary>A change still waiting is acted on now (the panel is closing).</summary>
	public void Flush()
	{
		if (_pending)
			OnTimer();
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_timer.Dispose();
	}

	private void OnTimer()
	{
		if (_disposed)
			return;

		_pending = false;
		_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		if (_isPinned())
		{
			PinnedLayoutChanged?.Invoke(this, EventArgs.Empty);
			return;
		}

		if (_capture() is { } current)
			_settings.SaveDockLayout(_groupId, current);
	}

	private bool Same(PreviewDockLayoutModel a, PreviewDockLayoutModel? b) =>
		b != null
		&& a.Side == b.Side
		&& a.TabIndex == b.TabIndex
		&& Math.Abs(a.Size - b.Size) < 1
		&& a.AutoHide == b.AutoHide
		&& a.Floating == b.Floating
		&& a.Shown == b.Shown
		&& a.StackedWith.SequenceEqual(b.StackedWith)
		&& SamePlacement(a.FloatingPlacement, b.FloatingPlacement);

	private bool SamePlacement(PreviewPlacementModel? a, PreviewPlacementModel? b) =>
		a == null || b == null
			? a == null && b == null
			: a.Left == b.Left && a.Top == b.Top && a.Width == b.Width && a.Height == b.Height;
}
