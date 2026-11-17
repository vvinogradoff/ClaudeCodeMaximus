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

	/// <summary>Opens the title bar's hamburger menu (the "Main menu" button) and invokes its entry with this name; false on timeout. The entries are buttons of a popup, filled afresh on each open.</summary>
	public async Task<bool> ClickMainMenuItemAsync(Process process, string itemName, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (!Exists(process, itemName) && !await ClickAsync(process, "Main menu", TimeSpan.FromSeconds(5)))
				return false;

			if (await ClickAsync(process, itemName, TimeSpan.FromSeconds(3)))
				return true;
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

	/// <summary>
	/// Puts the caret of the text box at the end of its text (UI Automation's TextPattern, no keyboard): setting a text through the ValuePattern leaves the caret
	/// at the start, and a trigger such as <c>#Fo</c> is read at the caret. False on timeout.
	/// </summary>
	public async Task<bool> PlaceCaretAtEndAsync(Process process, string textBoxName, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TryPlaceCaretAtEnd(hwnd, textBoxName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private static bool TryPlaceCaretAtEnd(IntPtr hwnd, string textBoxName)
	{
		try
		{
			var box = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
				new PropertyCondition(AutomationElement.NameProperty, textBoxName)));
			if (box == null || !box.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
				return false;

			var range = ((TextPattern)pattern).DocumentRange;
			range.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start, range, System.Windows.Automation.Text.TextPatternRangeEndpoint.End);
			range.Select();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
		{
			return false;
		}
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

	/// <summary>Waits for a toggle button with this name and flips it through its TogglePattern; false on timeout.</summary>
	public async Task<bool> ToggleAsync(Process process, string buttonName, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TryToggle(hwnd, buttonName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private static bool TryToggle(IntPtr hwnd, string buttonName)
	{
		try
		{
			var button = FindButton(hwnd, buttonName);
			if (button == null || !button.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
				return false;

			((TogglePattern)pattern).Toggle();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	/// <summary>The current text of the text box with this automation name; null when it is not there (yet).</summary>
	public string? GetText(Process process, string textBoxName)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return null;

		try
		{
			var box = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
				new PropertyCondition(AutomationElement.NameProperty, textBoxName)));
			return box != null && box.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? ((ValuePattern)pattern).Current.Value : null;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return null;
		}
	}

	/// <summary>True when a text element whose name contains <paramref name="fragment"/> is currently shown (a notice).</summary>
	public bool TextExists(Process process, string fragment)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return false;

		try
		{
			return AutomationElement.FromHandle(hwnd)
				.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
				.Cast<AutomationElement>()
				.Any(e => e.Current.Name.Contains(fragment, StringComparison.Ordinal));
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	/// <summary>Waits for a tab item with this name and selects it through its SelectionItemPattern; false on timeout.</summary>
	public async Task<bool> SelectTabAsync(Process process, string tabName, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TrySelectTab(hwnd, tabName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private static bool TrySelectTab(IntPtr hwnd, string tabName)
	{
		try
		{
			var tab = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
				new PropertyCondition(AutomationElement.NameProperty, tabName)));
			if (tab == null || !tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
				return false;

			((SelectionItemPattern)pattern).Select();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	/// <summary>Waits for a radio button with this name (the Session / Focus switch) and selects it through its SelectionItemPattern; false on timeout.</summary>
	public async Task<bool> SelectRadioAsync(Process process, string name, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			if (hwnd != IntPtr.Zero && TrySelectRadio(hwnd, name))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private static bool TrySelectRadio(IntPtr hwnd, string name)
	{
		try
		{
			var radio = FindRadio(hwnd, name);
			if (radio == null || !radio.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
				return false;

			((SelectionItemPattern)pattern).Select();
			return true;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	private static AutomationElement? FindRadio(IntPtr hwnd, string name) =>
		AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
			new PropertyCondition(AutomationElement.NameProperty, name)));

	private static AutomationElement? FindCheckBox(IntPtr hwnd, string name) =>
		AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
			new PropertyCondition(AutomationElement.NameProperty, name)));

	/// <summary>Waits for a check box with this name and flips it through its TogglePattern; false on timeout.</summary>
	public async Task<bool> ToggleCheckBoxAsync(Process process, string name, TimeSpan timeout)
	{
		var finder = new AppWindowFinder();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var hwnd = finder.Find(process.Id);
			try
			{
				var box = hwnd == IntPtr.Zero ? null : FindCheckBox(hwnd, name);
				if (box != null && box.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
				{
					((TogglePattern)pattern).Toggle();
					return true;
				}
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
			{
				// the window is changing: look again
			}

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>True when the check box with this name exists and is checked.</summary>
	public bool IsCheckBoxChecked(Process process, string name)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		try
		{
			var box = hwnd == IntPtr.Zero ? null : FindCheckBox(hwnd, name);
			return box != null && box.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) && ((TogglePattern)pattern).Current.ToggleState == ToggleState.On;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	/// <summary>True when the radio button with this name exists and is selected.</summary>
	public bool IsRadioSelected(Process process, string name)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		try
		{
			var radio = hwnd == IntPtr.Zero ? null : FindRadio(hwnd, name);
			return radio != null && radio.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return false;
		}
	}

	/// <summary>Waits until a button with this name exists; false on timeout.</summary>
	public async Task<bool> WaitForButtonAsync(Process process, string buttonName, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (Exists(process, buttonName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>Waits until no button with this name exists any more; false on timeout.</summary>
	public async Task<bool> WaitForButtonGoneAsync(Process process, string buttonName, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (!Exists(process, buttonName))
				return true;

			await Task.Delay(250);
		}

		return false;
	}
}
