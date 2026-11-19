using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Puts a docked panel into AvalonDock's layout model the way it was remembered (FR.29.21): stacked as a tab with a remembered neighbour that is open, else docked to
/// its side of the session pane (the outermost edge of the docking area) at its size; then auto-hidden or floating as remembered. Nothing remembered: docked right at
/// <see cref="Constants.PreviewDock.FirstTimeWidthFraction"/> of the area. A remembered neighbour that is not open is ignored (the panel docks to the same side). Works
/// on the model only (no controls), so it is tested without a window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewDockLayoutApplier
{
	/// <summary>Places <paramref name="panel"/> (not yet in the layout) and selects it. <paramref name="areaWidth"/> and <paramref name="areaHeight"/> size the first-time layout (0 when not laid out yet).</summary>
	public void Apply(LayoutRoot root, LayoutDocumentPane document, LayoutAnchorable panel, PreviewDockLayoutModel? remembered, double areaWidth, double areaHeight)
	{
		var layout = remembered ?? new PreviewDockLayoutModel();
		if (!layout.AutoHide && !layout.Floating && TryStack(root, panel, layout))
		{
			panel.IsSelected = true;
			return;
		}

		var size = SizeOf(layout, areaWidth, areaHeight);
		var pane = new LayoutAnchorablePane(panel);
		if (layout.Side is PreviewDockSide.Left or PreviewDockSide.Right)
			pane.DockWidth = new GridLength(size);
		else
			pane.DockHeight = new GridLength(size);

		DockAtEdge(root, document, pane, layout.Side);
		panel.IsSelected = true;
		if (layout.AutoHide)
		{
			if (layout.Side is PreviewDockSide.Left or PreviewDockSide.Right)
				panel.AutoHideWidth = size;
			else
				panel.AutoHideHeight = size;

			panel.ToggleAutoHide();
			return;
		}

		if (layout.Floating && layout.FloatingPlacement is { } place)
		{
			panel.FloatingLeft = place.Left;
			panel.FloatingTop = place.Top;
			panel.FloatingWidth = Math.Max(place.Width, Constants.PreviewDock.MinPaneSize);
			panel.FloatingHeight = Math.Max(place.Height, Constants.PreviewDock.MinPaneSize);
			panel.Float();
		}
	}

	/// <summary>
	/// The panel opened while the docking area had no size yet, so it got the stand-in size; now the area is measured: the first-time size (40 %) replaces it. False when the panel is
	/// not docked in a pane of its own any more (the user moved it meanwhile).
	/// </summary>
	public bool ApplyFirstTimeSize(LayoutAnchorable panel, PreviewDockSide side, double areaWidth, double areaHeight)
	{
		if (panel.Parent is not LayoutAnchorablePane pane || panel.IsAutoHidden || panel.IsFloating)
			return false;

		var size = SizeOf(new PreviewDockLayoutModel { Side = side }, areaWidth, areaHeight);
		if (side is PreviewDockSide.Left or PreviewDockSide.Right)
			pane.DockWidth = new GridLength(size);
		else
			pane.DockHeight = new GridLength(size);

		return true;
	}

	private bool TryStack(LayoutRoot root, LayoutAnchorable panel, PreviewDockLayoutModel layout)
	{
		if (layout.StackedWith.Count == 0)
			return false;

		foreach (var other in root.Descendents().OfType<LayoutAnchorable>())
		{
			if (ReferenceEquals(other, panel) || !layout.StackedWith.Contains(other.ContentId) || other.IsFloating || other.IsAutoHidden || other.Parent is not LayoutAnchorablePane pane)
				continue;

			pane.InsertChildAt(Math.Clamp(layout.TabIndex, 0, pane.ChildrenCount), panel);
			return true;
		}

		return false;
	}

	private double SizeOf(PreviewDockLayoutModel layout, double areaWidth, double areaHeight)
	{
		if (layout.Size >= Constants.PreviewDock.MinPaneSize)
			return layout.Size;

		var horizontal = layout.Side is PreviewDockSide.Left or PreviewDockSide.Right;
		var area = horizontal ? areaWidth : areaHeight;
		if (area <= 0)
			return Constants.PreviewDock.FallbackPaneSize;

		var fraction = horizontal ? Constants.PreviewDock.FirstTimeWidthFraction : Constants.PreviewDock.FirstTimeHeightFraction;
		return Math.Max(Constants.PreviewDock.MinPaneSize, Math.Round(area * fraction));
	}

	/// <summary>Adds <paramref name="pane"/> at the outer edge of the whole docking area on <paramref name="side"/> of the session pane.</summary>
	private void DockAtEdge(LayoutRoot root, LayoutDocumentPane document, LayoutAnchorablePane pane, PreviewDockSide side)
	{
		var orientation = side is PreviewDockSide.Left or PreviewDockSide.Right ? Orientation.Horizontal : Orientation.Vertical;
		var outer = OutermostPanel(document, orientation) ?? Wrap(root, orientation);
		if (side is PreviewDockSide.Left or PreviewDockSide.Top)
			outer.InsertChildAt(0, pane);
		else
			outer.Children.Add(pane);
	}

	private LayoutPanel? OutermostPanel(LayoutDocumentPane document, Orientation orientation)
	{
		LayoutPanel? found = null;
		for (var node = document.Parent; node != null; node = node.Parent)
		{
			if (node is LayoutPanel panel && panel.Orientation == orientation)
				found = panel;
		}

		return found;
	}

	/// <summary>No panel of that orientation surrounds the session pane yet: the root panel becomes the only child of a new one.</summary>
	private LayoutPanel Wrap(LayoutRoot root, Orientation orientation)
	{
		var old = root.RootPanel;
		var wrapper = new LayoutPanel { Orientation = orientation };
		root.RootPanel = wrapper;
		wrapper.Children.Add(old);
		return wrapper;
	}
}
