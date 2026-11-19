using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Finds the docking chrome by UI Automation name and WPF class name (FR.6.22: the tabs, edge tabs and floating frames carry the panel's title as their name).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class DockingTabFinder
{
	/// <summary>The WPF class of a pane's tab item (anchorable and document panes both use the library's tab control).</summary>
	public const string Tab = "TabItem";

	/// <summary>The WPF class of an auto-hide edge tab's item (the tab itself has no UI Automation peer; its item container is named).</summary>
	public const string EdgeTab = "ItemsControlItem";

	/// <summary>True when the window holds an element of the given WPF class with this name.</summary>
	public bool Exists(IntPtr hwnd, string className, string name) => Find(hwnd, className, name) != null;

	/// <summary>True when the element exists and takes room on screen (a collapsed tab strip keeps its items in the tree with an empty rectangle).</summary>
	public bool IsShown(IntPtr hwnd, string className, string name)
	{
		var element = Find(hwnd, className, name);
		try
		{
			return element != null && !element.Current.IsOffscreen && !element.Current.BoundingRectangle.IsEmpty && element.Current.BoundingRectangle.Width > 0 && element.Current.BoundingRectangle.Height > 0;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
		{
			return false;
		}
	}

	/// <summary>True when the window holds any element with this name.</summary>
	public bool ExistsNamed(IntPtr hwnd, string name) => PreviewWindowsHarness.FindElement(hwnd, name) != null;

	public AutomationElement? Find(IntPtr hwnd, string className, string name)
	{
		try
		{
			var both = new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ClassNameProperty, className));
			return AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, both);
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>The top-level windows of the app other than the main window whose UI Automation name is the title (a floating panel's frame).</summary>
	public List<IntPtr> FloatingFrames(int processId, IntPtr main, string title)
	{
		var found = new List<IntPtr>();
		foreach (var hwnd in new AppWindowFinder().FindAll(processId))
		{
			if (hwnd == main)
				continue;

			try
			{
				if (AutomationElement.FromHandle(hwnd).Current.Name == title)
					found.Add(hwnd);
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
			{
				// the window went away
			}
		}

		return found;
	}
}
