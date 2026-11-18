using System.Runtime.InteropServices;
using Microsoft.Win32;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Reads the monitors with <c>EnumDisplayMonitors</c>/<c>GetMonitorInfo</c> (the device name needs <c>MONITORINFOEX</c>) and watches
/// <see cref="SystemEvents.DisplaySettingsChanged"/>, which may fire on any thread. The Win32 calls live here rather than in
/// <see cref="Win32WindowPlacement"/> because that class has no device names; the coordinates follow its convention (origin = the primary work area's
/// top-left), so remembered placements stay comparable with the ones of the main window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class Win32MonitorWatcher : IMonitorWatcher, IDisposable
{
	private const uint PrimaryMonitorFlag = 1;
	private const uint DefaultToNearest = 2;
	private const int DeviceNameLength = 32;

	private readonly IUiDispatcher _dispatcher;
	private readonly Timer _debounce;
	private readonly Timer _settle;
	private IReadOnlyList<MonitorInfo> _monitors;
	private volatile bool _settling;
	private bool _disposed;

	public Win32MonitorWatcher(IUiDispatcher dispatcher)
	{
		_dispatcher = dispatcher;
		_monitors = Read();
		_debounce = new Timer(_ => _dispatcher.Post(Refresh));
		_settle = new Timer(_ => _settling = false);
		SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
	}

	public event EventHandler? Changed;

	public IReadOnlyList<MonitorInfo> Monitors => _monitors;

	public bool IsSettling => _settling;

	public string? MonitorOf(IntPtr hwnd)
	{
		var monitor = MonitorFromWindow(hwnd, DefaultToNearest);
		if (monitor == IntPtr.Zero)
			return null;

		var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
		return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
		_debounce.Dispose();
		_settle.Dispose();
	}

	private void OnDisplaySettingsChanged(object? sender, EventArgs e)
	{
		if (_disposed)
			return;

		_settling = true;
		_settle.Change(Timeout.Infinite, Timeout.Infinite);
		_debounce.Change(Constants.Previews.MonitorDebounce, Timeout.InfiniteTimeSpan);
	}

	/// <summary>On the UI thread, after the debounce: reads the monitors again and raises <see cref="Changed"/> when they differ; the settle period starts now.</summary>
	private void Refresh()
	{
		if (_disposed)
			return;

		var now = Read();
		var changed = !_monitors.SequenceEqual(now);
		_monitors = now;
		if (changed)
			Changed?.Invoke(this, EventArgs.Empty);

		_settle.Change(Constants.Previews.MonitorSettle, Timeout.InfiniteTimeSpan);
	}

	private static IReadOnlyList<MonitorInfo> Read()
	{
		var raw = new List<MONITORINFOEX>();
		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data) =>
		{
			var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
			if (GetMonitorInfo(monitor, ref info))
				raw.Add(info);
			return true;
		}, IntPtr.Zero);

		if (raw.Count == 0)
			return [];

		var primary = raw.FirstOrDefault(m => (m.dwFlags & PrimaryMonitorFlag) != 0);
		if (primary.cbSize == 0)
			primary = raw[0];

		var originX = primary.rcWork.Left;
		var originY = primary.rcWork.Top;
		return raw
			.Select(m => new MonitorInfo(
				m.szDevice,
				new MonitorBounds(m.rcWork.Left - originX, m.rcWork.Top - originY, m.rcWork.Right - m.rcWork.Left, m.rcWork.Bottom - m.rcWork.Top),
				(m.dwFlags & PrimaryMonitorFlag) != 0))
			.ToList();
	}

	private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MONITORINFOEX
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameLength)]
		public string szDevice;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
