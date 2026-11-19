using System.Runtime.InteropServices;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Win32 reads of a top-level window for the E2E tests: where it is, whether it has a taskbar button, who owns it, and which window the user has in front (FR.6.22, FR.29.26).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class WindowProbe
{
	private const int GwlExStyle = -20;
	private const int GwOwner = 4;
	private const long WsExToolWindow = 0x80;
	private const long WsExAppWindow = 0x40000;
	private const long WsExNoActivate = 0x08000000;

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left, Top, Right, Bottom;
	}

	public IntPtr Foreground() => GetForegroundWindow();

	public (int Left, int Top, int Right, int Bottom) Bounds(IntPtr hwnd)
	{
		GetWindowRect(hwnd, out var rect);
		return (rect.Left, rect.Top, rect.Right, rect.Bottom);
	}

	/// <summary>True when the window's rectangle touches none of the real monitors.</summary>
	public bool IsOffEveryMonitor(IntPtr hwnd)
	{
		var (left, top, right, bottom) = Bounds(hwnd);
		var monitors = new List<Rect>();
		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Rect monitor, IntPtr _) =>
		{
			monitors.Add(monitor);
			return true;
		}, IntPtr.Zero);
		return monitors.All(m => right <= m.Left || left >= m.Right || bottom <= m.Top || top >= m.Bottom);
	}

	/// <summary>A taskbar button: a window without WS_EX_TOOLWINDOW and without an owner, or with WS_EX_APPWINDOW.</summary>
	public bool HasTaskbarButton(IntPtr hwnd)
	{
		var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
		if ((style & WsExAppWindow) != 0)
			return true;

		return (style & WsExToolWindow) == 0 && GetWindow(hwnd, GwOwner) == IntPtr.Zero;
	}

	public bool IsNoActivate(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExNoActivate) != 0;

	public bool HasOwner(IntPtr hwnd) => GetWindow(hwnd, GwOwner) != IntPtr.Zero;

	public int ProcessOf(IntPtr hwnd)
	{
		GetWindowThreadProcessId(hwnd, out var pid);
		return (int)pid;
	}

	private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr hwnd, int command);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
}
