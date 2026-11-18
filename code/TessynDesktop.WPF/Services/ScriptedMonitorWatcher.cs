using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>The E2E seam of <c>--test-monitors &lt;n&gt;</c>: n monitors of 1920×1080, side by side from the primary; it never changes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptedMonitorWatcher : IMonitorWatcher
{
	public ScriptedMonitorWatcher(int count)
	{
		Monitors = Enumerable.Range(0, Math.Max(1, count))
			.Select(i => new MonitorInfo(
				Constants.Previews.TestMonitorDevicePrefix + (i + 1),
				new MonitorBounds(i * Constants.Previews.TestMonitorWidth, 0, Constants.Previews.TestMonitorWidth, Constants.Previews.TestMonitorHeight),
				i == 0))
			.ToList();
	}

	public IReadOnlyList<MonitorInfo> Monitors { get; }

	public bool IsSettling => false;

	// The list never changes, so the event never fires.
	public event EventHandler? Changed
	{
		add { }
		remove { }
	}

	public string? MonitorOf(IntPtr hwnd) => Monitors[0].DeviceName;
}
