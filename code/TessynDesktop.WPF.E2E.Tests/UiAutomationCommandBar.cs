using System.Diagnostics;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Reads the state of the app's command bar and session list through UI Automation: the selected item of a selector (Model, Effort, Profile),
/// the state of a toggle button, the tree rows in order and the selected row. Read only; nothing is clicked.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiAutomationCommandBar
{
	/// <summary>The name of the selected item of the combo box with this automation name (an option's label); null when there is none (yet).</summary>
	public string? GetSelectedItem(Process process, string comboName) =>
		Read(process, root =>
		{
			var combo = root.FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
				new PropertyCondition(AutomationElement.NameProperty, comboName)));
			if (combo == null || !combo.TryGetCurrentPattern(SelectionPattern.Pattern, out var pattern))
				return null;

			var selection = ((SelectionPattern)pattern).Current.GetSelection();
			return selection.Length == 0 ? null : selection[0].Current.Name;
		});

	/// <summary>The state of the toggle button with this automation name; null when it is not there (yet).</summary>
	public ToggleState? GetToggleState(Process process, string buttonName) =>
		Read<ToggleState?>(process, root =>
		{
			var button = root.FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
				new PropertyCondition(AutomationElement.NameProperty, buttonName)));
			return button != null && button.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) ? ((TogglePattern)pattern).Current.ToggleState : null;
		});

	/// <summary>The names of the list rows in the window, in their visual order (tree rows are named by their label).</summary>
	public IReadOnlyList<string> GetRowNames(Process process) =>
		Read(process, root => root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
			.Cast<AutomationElement>().Select(e => e.Current.Name).ToList()) ?? [];

	/// <summary>The names of the selected list rows.</summary>
	public IReadOnlyList<string> GetSelectedRowNames(Process process) =>
		Read(process, root => root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
			.Cast<AutomationElement>()
			.Where(e => e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected)
			.Select(e => e.Current.Name).ToList()) ?? [];

	private static T? Read<T>(Process process, Func<AutomationElement, T?> read)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return default;

		try
		{
			return read(AutomationElement.FromHandle(hwnd));
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return default;
		}
	}
}
