using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Themes;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The main window's docking area (FR.29.19): AvalonDock with the <see cref="SessionPane"/> as the single, non-closable, non-floatable document of the central document
/// pane (no tab strip). Docked preview panels are placed around it by <see cref="PreviewDockLayoutApplier"/>. Implements <see cref="IDockingSurface"/>. The theme is
/// AvalonDock's VS2013 light or dark for now (the Tessyn colours are a later stage, FR.29.24).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class DockingArea : UserControl, IDockingSurface
{
	private bool _stripHidden;

	public DockingArea() : this(new SessionPane())
	{
	}

	/// <summary>An area around another central content than the real session pane (a test cannot load the real one: it needs the running app's services).</summary>
	public DockingArea(FrameworkElement sessionView)
	{
		InitializeComponent();
		SessionView = sessionView;
		var document = new LayoutDocument
		{
			ContentId = Constants.PreviewDock.SessionDocumentId,
			Title = Constants.PreviewDock.SessionDocumentTitle,
			Content = SessionView,
			CanClose = false,
			CanFloat = false,
		};
		DocumentPane = new LayoutDocumentPane(document);
		var root = new LayoutPanel { Orientation = Orientation.Horizontal };
		root.Children.Add(DocumentPane);
		DockManager.Layout = new LayoutRoot { RootPanel = root };
		DockManager.Layout.Updated += (_, _) =>
		{
			_stripHidden = false;   // the library may have built the panes again
			LayoutChanged?.Invoke(this, EventArgs.Empty);
		};
		DockManager.LayoutUpdated += (_, _) =>
		{
			if (!_stripHidden)
				_stripHidden = HideDocumentTabStrip();

			LayoutChanged?.Invoke(this, EventArgs.Empty);
		};
		ApplyTheme(dark: false);
	}

	/// <summary>The session pane, the central document.</summary>
	public FrameworkElement SessionView { get; }

	/// <summary>The AvalonDock manager.</summary>
	public DockingManager Manager => DockManager;

	public LayoutRoot Root => DockManager.Layout;

	public LayoutDocumentPane DocumentPane { get; }

	public double AreaWidth => ActualWidth;

	public double AreaHeight => ActualHeight;

	public event EventHandler? LayoutChanged;

	/// <summary>Switches the docking theme with Tessyn's light or dark theme (FR.29.24, first stage: AvalonDock's own VS2013 pair).</summary>
	public void ApplyTheme(bool dark)
	{
		if (dark && DockManager.Theme is Vs2013DarkTheme || !dark && DockManager.Theme is Vs2013LightTheme)
			return;

		DockManager.Theme = dark ? new Vs2013DarkTheme() : new Vs2013LightTheme();
		_stripHidden = false;   // a new theme builds the panes' templates again
	}

	/// <summary>
	/// The session pane is the one document, so its pane shows no tab strip (FR.29.19). The library's template has no switch for it, and an implicit style does not reach into
	/// it, so the strip (the tab panel and the drop-down button around it) is collapsed once the template is built (the next layout pass after the pane is shown); the template still makes the
	/// selected content. True when the strip was found.
	/// </summary>
	public bool HideDocumentTabStrip()
	{
		var found = false;
		foreach (var panel in Descendants<DocumentPaneTabPanel>(DockManager))
		{
			found = true;
			if (VisualTreeHelper.GetParent(panel) is UIElement strip && strip.Visibility != Visibility.Collapsed)
				strip.Visibility = Visibility.Collapsed;
		}

		return found;
	}

	private IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}
}
