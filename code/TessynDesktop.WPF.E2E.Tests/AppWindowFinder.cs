using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Finds and closes the app's top-level window by process id. The app's test window is a tool window (no taskbar button), which
/// <see cref="Process.MainWindowHandle"/> does not report, so the windows are enumerated directly.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppWindowFinder
{
	private const uint WmClose = 0x0010;

	private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

	/// <summary>A visible top-level window of <paramref name="processId"/>, or zero.</summary>
	public IntPtr Find(int processId)
	{
		var found = IntPtr.Zero;
		EnumWindows((hwnd, _) =>
		{
			GetWindowThreadProcessId(hwnd, out var pid);
			if (pid == processId && HasVisibleStyle(hwnd) && found == IntPtr.Zero)
			{
				found = hwnd;
				return false;
			}

			return true;
		}, IntPtr.Zero);
		return found;
	}

	/// <summary>Every visible top-level window of <paramref name="processId"/> (an open popup such as the colour picker is a window of its own).</summary>
	public List<IntPtr> FindAll(int processId)
	{
		var found = new List<IntPtr>();
		EnumWindows((hwnd, _) =>
		{
			GetWindowThreadProcessId(hwnd, out var pid);
			if (pid == processId && HasVisibleStyle(hwnd))
				found.Add(hwnd);

			return true;
		}, IntPtr.Zero);
		return found;
	}

	/// <summary>Waits for the window to exist.</summary>
	public async Task<bool> WaitForWindowAsync(Process process, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (Find(process.Id) != IntPtr.Zero)
				return true;
			await Task.Delay(100);
		}

		return false;
	}

	/// <summary>Asks the window to close (WM_CLOSE, like the close button) without activating it.</summary>
	public bool Close(Process process)
	{
		var hwnd = Find(process.Id);
		return hwnd != IntPtr.Zero && PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

	/// <summary>
	/// The WS_VISIBLE style of a top-level window. <c>IsWindowVisible</c> would say the same on the interactive desktop, but in a
	/// non-interactive window station (the E2E run as the test user in session 0) it returns false for a window that was shown.
	/// </summary>
	private static bool HasVisibleStyle(IntPtr hwnd) => (GetWindowLong(hwnd, GwlStyle) & WsVisible) != 0;

	private const int GwlStyle = -16;
	private const int WsVisible = 0x10000000;

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
	private static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
