using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Monitors a test sets, with a <see cref="Changed"/> it raises.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeMonitorWatcher : IMonitorWatcher
{
	public FakeMonitorWatcher(int count) => Set(count);

	public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];

	public bool IsSettling => Settling;

	/// <summary>Makes <see cref="IsSettling"/> true, as after a display change.</summary>
	public bool Settling { get; set; }

	public event EventHandler? Changed;

	public string? MonitorOf(IntPtr hwnd) => Monitors.FirstOrDefault()?.DeviceName;

	/// <summary>Connects <paramref name="count"/> monitors side by side and raises <see cref="Changed"/>.</summary>
	public void Set(int count)
	{
		Monitors = Enumerable.Range(0, count)
			.Select(i => new MonitorInfo(@"\\.\TESTDISPLAY" + (i + 1), new MonitorBounds(i * 1920, 0, 1920, 1080), i == 0))
			.ToList();
		Changed?.Invoke(this, EventArgs.Empty);
	}
}
