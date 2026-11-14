using System.Runtime.InteropServices;

namespace TessynDesktop.WPF.State;

/// <summary>
/// Win32 access for window placement: <c>GetWindowPlacement</c>/<c>SetWindowPlacement</c> and the monitor work areas. All
/// coordinates are physical pixels in workspace coordinates (origin = top-left of the primary work area), the unit
/// <c>GetWindowPlacement</c> uses, so remembered and clamped values are directly comparable.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class Win32WindowPlacement
{
	private const int ShowNormal = 1;
	private const int ShowMaximized = 3;
	private const int ShowMinimized = 2;
	private const uint PrimaryMonitorFlag = 1;

	/// <summary>The window's normal (restored) placement and whether it is maximized; null when it cannot be read.</summary>
	public WindowPlacementModel? Capture(IntPtr hwnd)
	{
		var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
		if (!GetWindowPlacement(hwnd, ref placement))
			return null;

		var rect = placement.rcNormalPosition;
		return new WindowPlacementModel
		{
			Left = rect.Left,
			Top = rect.Top,
			Width = rect.Right - rect.Left,
			Height = rect.Bottom - rect.Top,
			Maximized = placement.showCmd == ShowMaximized,
		};
	}

	/// <summary>Moves and sizes the window to <paramref name="model"/> (maximized when remembered so).</summary>
	public void Apply(IntPtr hwnd, WindowPlacementModel model)
	{
		var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
		if (!GetWindowPlacement(hwnd, ref placement))
			return;

		placement.showCmd = model.Maximized ? ShowMaximized : ShowNormal;
		placement.rcNormalPosition = new RECT { Left = model.Left, Top = model.Top, Right = model.Left + model.Width, Bottom = model.Top + model.Height };
		SetWindowPlacement(hwnd, ref placement);
	}

	/// <summary>Work areas of the monitors present and the primary one, in workspace coordinates.</summary>
	public (IReadOnlyList<MonitorBounds> Monitors, MonitorBounds Primary) GetMonitors()
	{
		var raw = new List<MONITORINFO>();
		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data) =>
		{
			var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
			if (GetMonitorInfo(monitor, ref info))
				raw.Add(info);
			return true;
		}, IntPtr.Zero);

		if (raw.Count == 0)
			return ([], new MonitorBounds(0, 0, 1920, 1080));

		var primaryInfo = raw.FirstOrDefault(m => (m.dwFlags & PrimaryMonitorFlag) != 0);
		if (primaryInfo.cbSize == 0)
			primaryInfo = raw[0];

		var originX = primaryInfo.rcWork.Left;
		var originY = primaryInfo.rcWork.Top;
		MonitorBounds ToBounds(MONITORINFO m) =>
			new(m.rcWork.Left - originX, m.rcWork.Top - originY, m.rcWork.Right - m.rcWork.Left, m.rcWork.Bottom - m.rcWork.Top);

		return (raw.Select(ToBounds).ToList(), ToBounds(primaryInfo));
	}

	private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WINDOWPLACEMENT
	{
		public int length;
		public int flags;
		public int showCmd;
		public POINT ptMinPosition;
		public POINT ptMaxPosition;
		public RECT rcNormalPosition;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MONITORINFO
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
