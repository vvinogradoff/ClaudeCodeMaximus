using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Gives the docking chrome UI Automation names (FR.6.22, FR.29.26): without them every tab is called <c>AvalonDock.Layout.LayoutAnchorable</c>. The tab items of the panes carry the
/// panel's title declaratively (<c>AutomationProperties.Name</c> bound to the title in the tab item styles of <c>DockingChrome.xaml</c>: no tree walk, nothing to run). What a style cannot reach
/// is named here: an auto-hide edge tab (its panel's title) and a floating window frame (its panel's title). E2E tests find them by name, so <see cref="DockingArea"/> runs this only under
/// <c>--test-window</c>; the user's app never walks the tree for it.
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

	/// <summary>Names every edge tab and floating frame that exists now.</summary>
	public void Update()
	{
		foreach (var edge in _tree.Descendants<LayoutAnchorControl>(_manager))
			Name(HostOf(edge), EdgeTitle(edge.Model));

		foreach (var window in _manager.FloatingWindows)
			Name(window, EdgeTitle(window.Model));
	}

	private void Name(DependencyObject? element, string? title)
	{
		if (element != null && !string.IsNullOrEmpty(title) && AutomationProperties.GetName(element) != title)
			AutomationProperties.SetName(element, title);
	}

	/// <summary>The item container the edge tab sits in (the one UI Automation exposes), else the tab itself.</summary>
	private DependencyObject HostOf(LayoutAnchorControl edge)
	{
		for (DependencyObject? node = VisualTreeHelper.GetParent(edge); node != null; node = VisualTreeHelper.GetParent(node))
		{
			if (node is ContentPresenter presenter)
				return presenter;
		}

		return edge;
	}

	/// <summary>An edge tab stands for a panel, a floating frame for a panel or a pane's panels (their titles in layout order).</summary>
	private string? EdgeTitle(object? model)
	{
		if (model is LayoutContent content)
			return content.Title;

		if (model is ILayoutContainer container)
			return string.Join(Constants.DockingTestHook.TitleSeparator, container.Descendents().OfType<LayoutContent>().Select(c => c.Title));

		return null;
	}
}
