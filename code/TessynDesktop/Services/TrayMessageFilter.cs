using System;
using System.Runtime.InteropServices;

namespace TessynDesktop.Services;

/// <summary>
/// Lets the shell's tray messages reach a host that runs elevated (FR.32.14). Windows' message filter (UIPI) drops most messages that a normal process posts
/// to an elevated one, and Explorer runs normally: without this, an elevated host's tray icon could miss its clicks and would not come back after Explorer
/// restarts (the TaskbarCreated broadcast). Only these two messages are let through, and only when the host runs elevated.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TrayMessageFilter
{
	/// <summary>The callback message of a WinForms <c>NotifyIcon</c>: <c>WM_TRAYMOUSEMESSAGE = WM_USER + 1024</c> in System.Windows.Forms.</summary>
	private const uint NotifyIconCallbackMessage = 0x0400 + 1024;

	private const string TaskbarCreatedMessage = "TaskbarCreated";

	private const uint MessageFilterAdd = 1;   // MSGFLT_ADD

	/// <summary>Allows the two tray messages when this process runs elevated; true when that worked or nothing had to be done.</summary>
	public bool AllowWhenElevated()
	{
		if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess)
			return true;

		var taskbarCreated = RegisterWindowMessageW(TaskbarCreatedMessage);
		var restartAllowed = taskbarCreated != 0 && ChangeWindowMessageFilter(taskbarCreated, MessageFilterAdd);
		var clicksAllowed = ChangeWindowMessageFilter(NotifyIconCallbackMessage, MessageFilterAdd);
		return restartAllowed && clicksAllowed;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint RegisterWindowMessageW(string message);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ChangeWindowMessageFilter(uint message, uint flag);
}
