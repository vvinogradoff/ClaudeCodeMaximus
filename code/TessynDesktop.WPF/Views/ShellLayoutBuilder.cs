using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Builds the default layout of the docked shell on AvalonDock's model (FR.6.12): the left pane (Sessions, Recent, Tags as tabs, Sessions selected) at the given width, the Session view
/// as the central document, and the Focus pane on the right. Works on the model only, so it is tested without a window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutBuilder
{
	/// <summary>
	/// The default layout around <paramref name="document"/>. <paramref name="areaWidth"/> is the docking area's width (0 before it is laid out: Focus then takes a proportional
	/// share, <see cref="Constants.DockedShell.DefaultFocusPaneShare"/> of what the left pane leaves, and the exact share is applied once the area is measured).
	/// </summary>
	public LayoutRoot Build(LayoutDocumentPane document, IReadOnlyList<LayoutAnchorable> left, LayoutAnchorable? focus, double leftWidth, double areaWidth)
	{
		var panel = new LayoutPanel { Orientation = Orientation.Horizontal };
		if (left.Count > 0)
		{
			var leftPane = new LayoutAnchorablePane { DockWidth = new GridLength(Math.Max(Constants.ShellLayout.MinLeftPaneWidth, leftWidth)) };
			foreach (var tab in left)
				leftPane.Children.Add(tab);

			leftPane.SelectedContentIndex = 0;
			panel.Children.Add(leftPane);
		}

		panel.Children.Add(document);
		if (focus != null)
		{
			var focusPane = new LayoutAnchorablePane(focus);
			if (areaWidth > 0)
			{
				focusPane.DockWidth = new GridLength(FocusWidth(areaWidth));
			}
			else
			{
				focusPane.DockWidth = new GridLength(Constants.DockedShell.DefaultFocusPaneShare, GridUnitType.Star);
				document.DockWidth = new GridLength(1 - Constants.DockedShell.DefaultFocusPaneShare, GridUnitType.Star);
			}

			panel.Children.Add(focusPane);
		}

		return new LayoutRoot { RootPanel = panel };
	}

	/// <summary>Focus's default width: <see cref="Constants.DockedShell.DefaultFocusPaneShare"/> of the docking area's width (FR.6.12).</summary>
	public double FocusWidth(double areaWidth) => Math.Max(Constants.PreviewDock.MinPaneSize, Math.Round(areaWidth * Constants.DockedShell.DefaultFocusPaneShare));
}
