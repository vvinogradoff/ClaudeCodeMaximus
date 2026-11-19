using System.Windows;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The document pane shows no tab strip while it holds the session alone (FR.29.19); as soon as it holds another item (a preview panel dropped onto the centre of the session
/// pane becomes a tab beside the session) the strip shows, so the tabs can be selected and the preview dragged out again. The library's template has no switch for it, and an
/// implicit style does not reach into it, so the strip (the tab panel and the drop-down button around it) is collapsed or shown after the template is built. It is only looked at
/// while <see cref="IsDirty"/>: after the document pane's children, the layout tree or the theme changed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingStripSync
{
	private readonly DockingManager _manager;
	private readonly Func<LayoutDocumentPane> _documentPane;
	private readonly DockingVisualTree _tree;

	public DockingStripSync(DockingManager manager, Func<LayoutDocumentPane> documentPane, DockingVisualTree tree)
	{
		_manager = manager;
		_documentPane = documentPane;
		_tree = tree;
	}

	/// <summary>The strip may not show what it should; <see cref="Update"/> has to run.</summary>
	public bool IsDirty { get; private set; } = true;

	public void Invalidate() => IsDirty = true;

	/// <summary>Collapses or shows the strip. True when the strip was found (it exists once the template is built); otherwise the next layout pass tries again.</summary>
	public bool Update()
	{
		var found = false;
		var wanted = _documentPane().ChildrenCount > 1 ? Visibility.Visible : Visibility.Collapsed;
		foreach (var panel in _tree.Descendants<DocumentPaneTabPanel>(_manager))
		{
			found = true;
			if (VisualTreeHelper.GetParent(panel) is UIElement strip && strip.Visibility != wanted)
				strip.Visibility = wanted;
		}

		IsDirty = !found;
		return found;
	}
}
