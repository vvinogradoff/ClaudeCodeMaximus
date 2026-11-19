using AvalonDock.Layout;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Reads where a docked panel sits from AvalonDock's layout model (FR.29.21): the side of the session pane (from the layout tree, or the edge it is auto-hidden at),
/// the panels it shares a pane with, its tab index, the size of its pane, auto-hide, the floating rectangle and whether it is shown. Works on the model only (no controls),
/// so it is tested without a window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewDockLayoutReader
{
	/// <summary>
	/// The layout of <paramref name="panel"/> now; <paramref name="document"/> is the session pane's document pane, the centre every side is relative to.
	/// While the panel floats, the library does not say where it came from, so <paramref name="lastDocked"/> (what was read while it was docked) gives the side, neighbours,
	/// tab index and size it returns to. <paramref name="floatingNow"/> is the floating window's place as the window itself reports it (physical pixels, monitor, scale; FR.29.16); without
	/// it the library's rectangle is kept as it is, without a scale.
	/// </summary>
	public PreviewDockLayoutModel Read(LayoutAnchorable panel, LayoutDocumentPane document, PreviewDockLayoutModel? lastDocked = null, PreviewPlacementModel? floatingNow = null)
	{
		var model = new PreviewDockLayoutModel { Shown = !panel.IsHidden && panel.Root != null };
		if (panel.IsFloating)
		{
			model.Floating = true;
			model.Side = lastDocked?.Side ?? PreviewDockSide.Right;
			model.TabIndex = lastDocked?.TabIndex ?? 0;
			model.Size = lastDocked?.Size ?? 0;
			model.FloatingPlacement = floatingNow?.Clone() ?? new PreviewPlacementModel
			{
				Left = (int)Math.Round(panel.FloatingLeft),
				Top = (int)Math.Round(panel.FloatingTop),
				Width = (int)Math.Round(panel.FloatingWidth),
				Height = (int)Math.Round(panel.FloatingHeight),
			};
			model.StackedWith = lastDocked == null ? [] : [.. lastDocked.StackedWith];
			return model;
		}

		// A panel dropped onto the centre is a tab of the document pane: it has no side of its own, so it keeps the last docked one.
		model.Side = (ReferenceEquals(panel.Parent, document) ? lastDocked?.Side : SideOf(panel, document)) ?? lastDocked?.Side ?? PreviewDockSide.Right;
		model.AutoHide = panel.IsAutoHidden;
		var container = panel.Parent as ILayoutContainer;
		model.StackedWith = StackedWith(panel, container);
		model.TabIndex = container == null ? 0 : IndexOf(container, panel);
		model.Size = panel.IsAutoHidden ? AutoHideSize(panel, model.Side) : PaneSize(panel.Parent as LayoutAnchorablePane, model.Side);
		return model;
	}

	/// <summary>The side of the session pane an element is on; null when it is not part of the layout tree around the document pane.</summary>
	public PreviewDockSide? SideOf(ILayoutElement? element, LayoutDocumentPane document)
	{
		if (element == null)
			return null;

		for (var node = element; node != null; node = node.Parent)
		{
			if (node is LayoutAnchorSide side)
				return side.Side switch
				{
					AnchorSide.Left => PreviewDockSide.Left,
					AnchorSide.Top => PreviewDockSide.Top,
					AnchorSide.Bottom => PreviewDockSide.Bottom,
					_ => PreviewDockSide.Right,
				};
		}

		var documentChain = Chain(document);
		for (var node = element; node != null; node = node.Parent)
		{
			if (node.Parent is not LayoutPanel panel || !documentChain.Contains(panel))
				continue;

			var documentBranch = documentChain[documentChain.IndexOf(panel) + 1];
			var documentIndex = panel.IndexOfChild(documentBranch);
			var elementIndex = panel.IndexOfChild(node);
			if (elementIndex < 0 || documentIndex < 0 || elementIndex == documentIndex)
				return null;

			var before = elementIndex < documentIndex;
			if (panel.Orientation == System.Windows.Controls.Orientation.Horizontal)
				return before ? PreviewDockSide.Left : PreviewDockSide.Right;

			return before ? PreviewDockSide.Top : PreviewDockSide.Bottom;
		}

		return null;
	}

	/// <summary>The ids of the other panels in the container (the pane's tab strip).</summary>
	private List<string> StackedWith(LayoutAnchorable panel, ILayoutContainer? container)
	{
		if (container == null)
			return [];

		return container.Children.OfType<LayoutAnchorable>().Where(a => !ReferenceEquals(a, panel) && !string.IsNullOrEmpty(a.ContentId)).Select(a => a.ContentId).ToList();
	}

	private int IndexOf(ILayoutContainer container, LayoutAnchorable panel)
	{
		var index = 0;
		foreach (var child in container.Children)
		{
			if (ReferenceEquals(child, panel))
				return index;

			index++;
		}

		return 0;
	}

	private double PaneSize(LayoutAnchorablePane? pane, PreviewDockSide side)
	{
		if (pane == null)
			return 0;

		var horizontal = side is PreviewDockSide.Left or PreviewDockSide.Right;
		var length = horizontal ? pane.DockWidth : pane.DockHeight;
		if (length.IsAbsolute)
			return length.Value;

		return 0;   // a proportional length carries no size; the first-time size applies when it opens again
	}

	private double AutoHideSize(LayoutAnchorable panel, PreviewDockSide side) => side is PreviewDockSide.Left or PreviewDockSide.Right ? panel.AutoHideWidth : panel.AutoHideHeight;

	/// <summary>The element's ancestors from the root down to it.</summary>
	private List<ILayoutElement> Chain(ILayoutElement element)
	{
		var chain = new List<ILayoutElement>();
		for (var node = element; node != null; node = node.Parent)
			chain.Insert(0, node);

		return chain;
	}
}
