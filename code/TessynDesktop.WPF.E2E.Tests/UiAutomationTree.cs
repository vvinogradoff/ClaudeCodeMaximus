using System.Diagnostics;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Finds and selects rows of the app's session tree through UI Automation (the rows are named by their label); no mouse or keyboard is involved.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiAutomationTree
{
	/// <summary>Waits for a row with this name and selects it; false on timeout.</summary>
	public async Task<bool> SelectAsync(Process process, string rowName, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TrySelect(hwnd, rowName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>Waits until a row with this name exists (or, with <paramref name="shouldExist"/> false, is gone).</summary>
	public async Task<bool> WaitForRowAsync(Process process, string rowName, bool shouldExist, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && (Find(hwnd, rowName) != null) == shouldExist)
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private static bool TrySelect(IntPtr hwnd, string rowName)
	{
		try
		{
			var row = Find(hwnd, rowName);
			if (row == null || !row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
				return false;

			((SelectionItemPattern)pattern).Select();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	private static AutomationElement? Find(IntPtr hwnd, string rowName)
	{
		try
		{
			return AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
				new PropertyCondition(AutomationElement.NameProperty, rowName)));
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return null;
		}
	}
}
