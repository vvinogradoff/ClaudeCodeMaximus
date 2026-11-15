using System.Diagnostics;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Presses a button of the running app through UI Automation (its InvokePattern), by automation name. The app's test window is
/// off-screen and never activated, so no mouse or keyboard input is involved.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiAutomationClicker
{
	/// <summary>Waits for a button with this name to exist and invokes it; false on timeout.</summary>
	public async Task<bool> ClickAsync(Process process, string buttonName, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TryInvoke(hwnd, buttonName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>Waits for a text box with this automation name and sets its text; false on timeout.</summary>
	public async Task<bool> SetTextAsync(Process process, string textBoxName, string text, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TrySetText(hwnd, textBoxName, text))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private static bool TrySetText(IntPtr hwnd, string textBoxName, string text)
	{
		try
		{
			var box = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
				new PropertyCondition(AutomationElement.NameProperty, textBoxName)));
			if (box == null || !box.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
				return false;

			((ValuePattern)pattern).SetValue(text);
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	/// <summary>True when a button with this name currently exists in the window.</summary>
	public bool Exists(Process process, string buttonName)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		return hwnd != IntPtr.Zero && FindButton(hwnd, buttonName) != null;
	}

	private static bool TryInvoke(IntPtr hwnd, string buttonName)
	{
		try
		{
			var button = FindButton(hwnd, buttonName);
			if (button == null || !button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
				return false;

			((InvokePattern)pattern).Invoke();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	private static AutomationElement? FindButton(IntPtr hwnd, string buttonName)
	{
		var root = AutomationElement.FromHandle(hwnd);
		return root.FindFirst(TreeScope.Descendants, new AndCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
			new PropertyCondition(AutomationElement.NameProperty, buttonName)));
	}
}
