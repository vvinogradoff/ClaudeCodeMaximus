using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Gives the docking chrome UI Automation names (FR.6.22, FR.29.26): without them every tab is called <c>AvalonDock.Layout.LayoutAnchorable</c>. The tab items of the panes (the
/// <see cref="TabItem"/> containers of the anchorable and document panes) carry the panel's title as <see cref="AutomationProperties.NameProperty"/> (Focus: "Focus", without the
/// badge count), an auto-hide edge tab its panel's title, and a floating window frame its panel's title. E2E tests find them by name. Run after the layout changed; the library keeps the controls until the next layout change.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingAutomationNames
{
	private readonly DockingManager _manager;
	private readonly DockingVisualTree _tree;

	public DockingAutomationNames(DockingManager manager, DockingVisualTree tree)
	{
		_manager = manager;
		_tree = tree;
	}

	/// <summary>Names every tab, edge tab and floating frame that exists now; false when a tab was found that has no panel title yet (the library has not filled it).</summary>
	public bool Update()
	{
		var complete = true;
		foreach (var tab in _tree.Descendants<TabItem>(_manager))
		{
			var title = TitleOf(tab.Content) ?? TitleOf(tab.Header) ?? TitleOf(tab.DataContext);
			complete &= title != null || !string.IsNullOrEmpty(AutomationProperties.GetName(tab));
			Name(tab, title);
		}

		foreach (var edge in _tree.Descendants<LayoutAnchorControl>(_manager))
			Name(HostOf(edge), EdgeTitle(edge.Model));

		foreach (var window in _manager.FloatingWindows)
			Name(window, EdgeTitle(window.Model));

		return complete;
	}

	private static void Name(DependencyObject? element, string? title)
	{
		if (element != null && !string.IsNullOrEmpty(title) && AutomationProperties.GetName(element) != title)
			AutomationProperties.SetName(element, title);
	}

	/// <summary>The item container the edge tab sits in (the one UI Automation exposes), else the tab itself.</summary>
	private static DependencyObject HostOf(LayoutAnchorControl edge)
	{
		for (DependencyObject? node = VisualTreeHelper.GetParent(edge); node != null; node = VisualTreeHelper.GetParent(node))
		{
			if (node is ContentPresenter presenter)
				return presenter;
		}

		return edge;
	}

	private static string? TitleOf(object? model) => (model as LayoutContent)?.Title;

	/// <summary>An edge tab stands for a panel, a floating frame for a panel or a pane's panels (their titles in layout order).</summary>
	private static string? EdgeTitle(object? model)
	{
		if (model is LayoutContent content)
			return content.Title;

		if (model is ILayoutContainer container)
			return string.Join(Constants.DockingTestHook.TitleSeparator, container.Descendents().OfType<LayoutContent>().Select(c => c.Title));

		return null;
	}
}
