using System.Runtime.InteropServices;
using System.Windows.Interop;
using Serilog;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>
/// Keeps a <c>--test-window</c> instance from holding the user's foreground window or keyboard focus.
/// E2E runs start the real app many times while the user works. A window shown with <c>ShowActivated = false</c> was still
/// activated later (WebView2 initialisation and WPF focus calls activate the top-level window), so every test start stole the
/// user's typing for the life of the test.
/// <list type="bullet">
/// <item>The window gets <c>WS_EX_NOACTIVATE</c> and answers <c>WM_MOUSEACTIVATE</c> with <c>MA_NOACTIVATE</c>.</item>
/// <item>A thread-local CBT hook on the UI thread vetoes <c>HCBT_ACTIVATE</c>, so the window is not activated in the first place (no
/// foreground change, no focus flash); the hand-back below remains as the second line for activations that slip through.</item>
/// <item>A system foreground hook remembers the user's last real (on-screen, foreign) foreground window. The moment one of this
/// process's windows becomes the foreground window, the foreground is handed straight back to it. Off-screen windows (other test
/// instances) are never chosen, so test windows cannot pass the focus between themselves.</item>
/// <item>Every other top-level window of this process that shows up (popups, dialogs, tooltips: WPF clamps a <c>Popup</c> onto a
/// visible monitor even when its owner is off-screen) is made non-activating, hidden from the taskbar and fully transparent
/// (layered, alpha 0) and moved off-screen, so nothing is ever drawn on the user's screen. UI Automation still sees it.</item>
/// </list>
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TestWindowFocusGuard
{
	private const int GwlExStyle = -20;
	private const long WsExNoActivate = 0x08000000;
	private const int WmMouseActivate = 0x0021;
	private const int MaNoActivate = 3;
	private const uint EventSystemForeground = 0x0003;
	private const uint EventObjectShow = 0x8002;
	private const long WsExToolWindow = 0x00000080;
	private const long WsExLayered = 0x00080000;
	private const uint GaRoot = 2;
	private const uint LwaAlpha = 2;
	private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
	private const int HiddenPosition = -32000;
	private const int WhCbt = 5;
	private const int HcbtActivate = 5;
	private const uint WinEventOutOfContext = 0x0000;
	private const int OffScreenLimit = -20000;
	private const int RecheckAttempts = 10;
	private const int RecheckDelayMs = 50;

	private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
	private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

	private static readonly uint OwnProcessId = (uint)Environment.ProcessId;
	private static WinEventProc? _proc;   // kept alive for the native hook
	private static WinEventProc? _showProc;
	private static IntPtr _hook;
	private static IntPtr _showHook;
	private static HookProc? _cbtProc;   // kept alive for the native hook
	private static int _vetoedActivations;
	private static Thread? _hookThread;
	private static IntPtr _userForeground;
	private static int _handBackCount;

	/// <summary>Number of times a window of this process became the foreground and was handed back.</summary>
	public static int HandBackCount => _handBackCount;

	/// <summary>Number of activations of this process's windows that the CBT hook vetoed.</summary>
	public static int VetoedActivationCount => _vetoedActivations;

	/// <summary>Remembers the user's foreground window before the test window exists; call it as early as possible at startup.</summary>
	public static void CaptureForegroundAtStart()
	{
		var foreground = GetForegroundWindow();
		if (IsUserWindow(foreground))
			Interlocked.Exchange(ref _userForeground, foreground);
	}

	/// <summary>Applies both layers; call it from <c>SourceInitialized</c> on the UI thread (the hook needs its message loop).</summary>
	public static void Apply(IntPtr hwnd)
	{
		var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
		SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExNoActivate));
		HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

		if (_cbtProc == null)
		{
			_cbtProc = OnCbt;
			if (SetWindowsHookEx(WhCbt, _cbtProc, IntPtr.Zero, GetCurrentThreadId()) == IntPtr.Zero)
				Log.Warning("Test window focus guard: the activation veto hook could not be installed");
		}

		if (_hookThread != null)
			return;

		// The hook runs on its own thread with its own message loop: on the UI thread its events would wait behind the startup work
		// (about 100 ms measured), and the test window would keep the user's foreground for that long.
		_hookThread = new Thread(RunHookLoop) { IsBackground = true, Name = "TestWindowFocusGuard" };
		_hookThread.Start();
	}

	private static void RunHookLoop()
	{
		_proc = OnForegroundChanged;
		_hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, WinEventOutOfContext);
		if (_hook == IntPtr.Zero)
		{
			Log.Warning("Test window focus guard: the foreground hook could not be installed");
			return;
		}

		_showProc = OnWindowShown;
		_showHook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _showProc, OwnProcessId, 0, WinEventOutOfContext);
		if (_showHook == IntPtr.Zero)
			Log.Warning("Test window focus guard: the window-show hook could not be installed");

		while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
		{
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
		}
	}

	/// <summary>A window of this process was shown: any top-level window except the (already off-screen) main window becomes invisible and non-activating.</summary>
	private static void OnWindowShown(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
	{
		if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0 || GetAncestor(hwnd, GaRoot) != hwnd)
			return;

		if (!GetWindowRect(hwnd, out var rect) || (rect.Left <= OffScreenLimit && rect.Top <= OffScreenLimit))
			return; // the main test window (or an already hidden one)

		var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
		SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow | WsExLayered));
		SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
		SetWindowPos(hwnd, IntPtr.Zero, HiddenPosition, HiddenPosition, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
		Log.Debug("Test window focus guard: made the top-level window 0x{Handle:X} invisible", hwnd.ToInt64());
	}

	private static IntPtr OnCbt(int code, IntPtr wParam, IntPtr lParam)
	{
		if (code == HcbtActivate)
		{
			Interlocked.Increment(ref _vetoedActivations);
			return new IntPtr(1); // a non-zero result prevents the activation
		}

		return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
	}

	private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg != WmMouseActivate)
			return IntPtr.Zero;

		handled = true;
		return new IntPtr(MaNoActivate);
	}

	private static void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
	{
		if (hwnd == IntPtr.Zero)
			return;

		if (!IsOwnWindow(hwnd))
		{
			if (IsUserWindow(hwnd))
				Interlocked.Exchange(ref _userForeground, hwnd);
			return;
		}

		// One of this test process's windows took the foreground: give it back at once, and check again for a short while,
		// because WebView2 can pull the focus back into its child window right after.
		HandBack();
		var count = Interlocked.Increment(ref _handBackCount);
		Log.Information("Test window focus guard: the test window became the foreground window; handed it back ({Count})", count);
		for (var attempt = 0; attempt < RecheckAttempts && IsOwnWindow(GetForegroundWindow()); attempt++)
		{
			Thread.Sleep(RecheckDelayMs);
			HandBack();
		}
	}

	/// <summary>
	/// Sets the user's window as the foreground window. While a WebView2 child (another process) holds the focus, the foreground
	/// thread is that process's, so the call is attached to its input for the moment of the switch.
	/// </summary>
	private static void HandBack()
	{
		var target = Interlocked.CompareExchange(ref _userForeground, IntPtr.Zero, IntPtr.Zero);
		if (target == IntPtr.Zero || !IsWindow(target))
			return;

		var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
		var ownThread = GetCurrentThreadId();
		var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
		try
		{
			SetForegroundWindow(target);
		}
		finally
		{
			if (attached)
				AttachThreadInput(ownThread, foregroundThread, false);
		}
	}

	private static bool IsOwnWindow(IntPtr hwnd)
	{
		GetWindowThreadProcessId(hwnd, out var pid);
		return pid == OwnProcessId;
	}

	/// <summary>A visible, on-screen window of another process: a candidate for the user's foreground window.</summary>
	private static bool IsUserWindow(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || !IsWindowVisible(hwnd))
			return false;

		return GetWindowRect(hwnd, out var rect) && !(rect.Left <= OffScreenLimit && rect.Top <= OffScreenLimit);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Msg
	{
		public IntPtr Hwnd;
		public uint Message;
		public IntPtr WParam;
		public IntPtr LParam;
		public uint Time;
		public int PointX, PointY;
	}

	[DllImport("user32.dll")]
	private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TranslateMessage(ref Msg lpMsg);

	[DllImport("user32.dll")]
	private static extern IntPtr DispatchMessage(ref Msg lpMsg);

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left, Top, Right, Bottom;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int hookId, HookProc proc, IntPtr module, uint threadId);

	[DllImport("user32.dll")]
	private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
