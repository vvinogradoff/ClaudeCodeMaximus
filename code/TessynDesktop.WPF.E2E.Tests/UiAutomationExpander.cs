using System.Diagnostics;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Reads and drives an expandable element of the running app (by its AutomationId) through the UI Automation ExpandCollapse pattern.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiAutomationExpander
{
	/// <summary>The element's state; null when it is not there (yet) or has no ExpandCollapse pattern.</summary>
	public ExpandCollapseState? GetState(Process process, string automationId)
	{
		var pattern = FindPattern(process, automationId, out _);
		try
		{
			return pattern?.Current.ExpandCollapseState;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return null;
		}
	}

	/// <summary>The element's height in pixels; null when it is not there.</summary>
	public double? GetHeight(Process process, string automationId)
	{
		FindPattern(process, automationId, out var element);
		try
		{
			return element?.Current.BoundingRectangle.Height;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return null;
		}
	}

	/// <summary>Waits until the element reports this state; false on timeout.</summary>
	public async Task<bool> WaitForStateAsync(Process process, string automationId, ExpandCollapseState state, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (GetState(process, automationId) == state)
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>Expands or collapses the element; false when it is not there.</summary>
	public bool TrySet(Process process, string automationId, bool expand)
	{
		var pattern = FindPattern(process, automationId, out _);
		if (pattern == null)
			return false;

		try
		{
			if (expand)
				pattern.Expand();
			else
				pattern.Collapse();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
		{
			return false;
		}
	}

	private static ExpandCollapsePattern? FindPattern(Process process, string automationId, out AutomationElement? element)
	{
		element = null;
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return null;

		try
		{
			element = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
			return element != null && element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) ? (ExpandCollapsePattern)pattern : null;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return null;
		}
	}
}
