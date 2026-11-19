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
	private bool _stripSynced;
	private Visibility _stripState = Visibility.Visible;
	private bool _floatingHidden;

	public DockingArea() : this(new SessionPane())
	{
	}

	/// <summary>An area around another central content than the real session pane (a test cannot load the real one: it needs the running app's services).</summary>
	public DockingArea(FrameworkElement sessionView)
	{
		InitializeComponent();
		SessionView = sessionView;
		Chrome = new DockingChrome(DockManager, this);   // the compact chrome, the focus guard, the slide-out closer (FR.6.13, FR.6.20)
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
			_stripSynced = false;   // the library may have built the panes again
			LayoutChanged?.Invoke(this, EventArgs.Empty);
		};
		DockManager.LayoutUpdated += (_, _) =>
		{
			if (!_stripSynced || _stripState != WantedStrip())
				_stripSynced = UpdateDocumentTabStrip();

			LayoutChanged?.Invoke(this, EventArgs.Empty);
		};
		DockManager.LayoutFloatingWindowControlCreated += (_, e) =>
		{
			if (e.LayoutFloatingWindowControl is not { } created)
				return;

			Chrome.OnFloatingWindowCreated(created);   // its keys go to the shell commands (FR.6.20)

			// The library shows the window after it is created; while the cover is up it is hidden again at once.
			created.IsVisibleChanged += (_, _) =>
			{
				if (_floatingHidden && created.IsVisible)
					created.Visibility = Visibility.Hidden;
			};
			if (_floatingHidden)
				created.Visibility = Visibility.Hidden;
		};
	}

	/// <summary>Hides or shows the floating panels' windows (the screensaver cover does not reach them, FR.29.19).</summary>
	public void SetFloatingVisible(bool visible)
	{
		_floatingHidden = !visible;
		foreach (var window in DockManager.FloatingWindows.ToList())
			window.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
	}

	/// <summary>The session pane, the central document.</summary>
	public FrameworkElement SessionView { get; }

	/// <summary>The compact chrome, keyboard routing for floating panels, focus guard and slide-out closer (FR.6.13, FR.6.20).</summary>
	public DockingChrome Chrome { get; }

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
		if (DockManager.Theme is Vs2013DarkTheme && dark || DockManager.Theme is Vs2013LightTheme && !dark)
			return;

		DockManager.Theme = dark ? new Vs2013DarkTheme() : new Vs2013LightTheme();
		_stripSynced = false;   // a new theme builds the panes' templates again
	}

	/// <summary>
	/// The document pane shows no tab strip while it holds the session alone (FR.29.19); as soon as it holds another item (a preview panel dropped onto the centre of the session
	/// pane becomes a tab beside the session) the strip shows, so the tabs can be selected and the preview dragged out again. The library's template has no switch for it, and an
	/// implicit style does not reach into it, so the strip (the tab panel and the drop-down button around it) is collapsed or shown after the template is built; the template still
	/// makes the selected content. Run on every layout update and theme switch. True when the strip was found.
	/// </summary>
	public bool UpdateDocumentTabStrip()
	{
		var found = false;
		var wanted = WantedStrip();
		foreach (var panel in Descendants<DocumentPaneTabPanel>(DockManager))
		{
			found = true;
			if (VisualTreeHelper.GetParent(panel) is UIElement strip && strip.Visibility != wanted)
				strip.Visibility = wanted;
		}

		if (found)
			_stripState = wanted;

		return found;
	}

	/// <summary>The strip shows only while the document pane holds more than the session.</summary>
	private Visibility WantedStrip() => DocumentPane.ChildrenCount > 1 ? Visibility.Visible : Visibility.Collapsed;

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
