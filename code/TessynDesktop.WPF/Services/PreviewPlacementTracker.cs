using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The placement rules of one preview window (FR.29.6, FR.29.8, FR.29.11, FR.29.13), apart from the WPF window: a move, resize or state change is acted on
/// <see cref="Constants.PreviewWindow.PlacementSaveDelay"/> after the last one. A group window saves its place as its group's remembered placement; a pinned window
/// never writes the group's, and raises <see cref="PinnedPlacementChanged"/> instead, so the router saves the open-window list. Nothing is done while the window
/// is minimized, while Windows is moving windows about after a display change, before the first frame is up, or in test mode. A change still waiting is
/// acted on when the window closes (<see cref="Flush"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewPlacementTracker : IDisposable
{
	private readonly string _groupId;
	private readonly IPreviewSettingsService _settings;
	private readonly IMonitorWatcher _monitors;
	private readonly IUiDispatcher _ui;
	private readonly Func<bool> _isPinned;
	private readonly Func<bool> _isMinimized;
	private readonly Func<PreviewPlacementModel?> _capture;
	private readonly bool _testWindow;
	private readonly TimeSpan _delay;
	private readonly ITimer _timer;
	private bool _pending;
	private bool _disposed;

	public PreviewPlacementTracker(
		string groupId, IPreviewSettingsService settings, IMonitorWatcher monitors, IUiDispatcher ui, Func<bool> isPinned, Func<bool> isMinimized,
		Func<PreviewPlacementModel?> capture, bool testWindow, TimeProvider? time = null, TimeSpan? delay = null)
	{
		_groupId = groupId;
		_settings = settings;
		_monitors = monitors;
		_ui = ui;
		_isPinned = isPinned;
		_isMinimized = isMinimized;
		_capture = capture;
		_testWindow = testWindow;
		_delay = delay ?? Constants.PreviewWindow.PlacementSaveDelay;
		_timer = (time ?? TimeProvider.System).CreateTimer(_ => _ui.Post(OnTimer), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	/// <summary>True once the window's first frame is up; moves before that are the window being placed, not the user moving it.</summary>
	public bool Tracking { get; set; }

	/// <summary>A pinned window settled at a new place; the router saves the list.</summary>
	public event EventHandler? PinnedPlacementChanged;

	/// <summary>The window moved, was resized or changed state: restarts the wait.</summary>
	public void Changed()
	{
		if (_testWindow || !Tracking || _disposed)
			return;

		_pending = true;
		_timer.Change(_delay, Timeout.InfiniteTimeSpan);
	}

	/// <summary>A change still waiting is acted on now (the window is closing).</summary>
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
		if (_testWindow || _isMinimized() || _monitors.IsSettling)
			return;

		if (_isPinned())
		{
			PinnedPlacementChanged?.Invoke(this, EventArgs.Empty);
			return;
		}

		if (_capture() is { } current)
			_settings.SavePlacement(_groupId, current);
	}
}
