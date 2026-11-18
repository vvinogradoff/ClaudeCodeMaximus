using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>The monitors connected now, and a debounced notice when they change (FR.29.1, FR.29.10).</summary>
/// <remarks>Created by Claude</remarks>
public interface IMonitorWatcher
{
	IReadOnlyList<MonitorInfo> Monitors { get; }

	/// <summary>On the UI thread, debounced (<see cref="Constants.Previews.MonitorDebounce"/>), and only when the list really changed.</summary>
	event EventHandler? Changed;

	/// <summary>True from a display change until <see cref="Constants.Previews.MonitorSettle"/> after the debounced update: windows must not save their placement meanwhile.</summary>
	bool IsSettling { get; }

	/// <summary>Device name of the monitor holding most of the window; null when it cannot be told.</summary>
	string? MonitorOf(IntPtr hwnd);
}
