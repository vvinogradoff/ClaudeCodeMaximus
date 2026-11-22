using System.Diagnostics;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Reads and picks the lines of a combo box of the app through UI Automation (the command bar's Model, Effort and Profile selectors, the Settings
/// drop-downs): the drop-down is expanded so its items exist, read or selected, then collapsed again.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiAutomationComboBox
{
	/// <summary>One line of a drop-down: its name (an option's label) and whether it can be picked (headers and hints cannot).</summary>
	public sealed record Line(string Name, bool IsEnabled);

	/// <summary>The lines of the combo box with this automation name, in order; empty when it was not found before the timeout.</summary>
	public async Task<IReadOnlyList<Line>> ReadLinesAsync(Process process, string comboName, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var lines = WithExpanded(process, comboName, combo => combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
				.Cast<AutomationElement>().Select(e => new Line(e.Current.Name, e.Current.IsEnabled)).ToList());
			if (lines is { Count: > 0 })
				return lines;

			await Task.Delay(200);
		}

		return [];
	}

	/// <summary>Picks the line with this name; false when the combo box or the line was not found before the timeout.</summary>
	public async Task<bool> SelectAsync(Process process, string comboName, string itemName, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var picked = WithExpanded(process, comboName, combo =>
			{
				// The exact name first; a drop-down with a display member path names its items by the whole item, which contains the label.
				var items = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().ToList();
				var item = items.FirstOrDefault(e => e.Current.Name == itemName) ?? items.FirstOrDefault(e => e.Current.Name.Contains(itemName, StringComparison.Ordinal));
				if (item == null || !item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
					return false;

				((SelectionItemPattern)pattern).Select();
				return true;
			});
			if (picked)
				return true;

			await Task.Delay(200);
		}

		return false;
	}

	private static T? WithExpanded<T>(Process process, string comboName, Func<AutomationElement, T> read)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return default;

		try
		{
			var combo = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
				new PropertyCondition(AutomationElement.NameProperty, comboName)));
			if (combo == null)
				return default;

			var expand = combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) ? (ExpandCollapsePattern)pattern : null;
			expand?.Expand();
			try
			{
				return read(combo);
			}
			finally
			{
				if (expand?.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
					expand.Collapse();
			}
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
		{
			return default;
		}
	}
}
