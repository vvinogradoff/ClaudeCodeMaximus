using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The main window's docking area (FR.6.11, FR.29.19): AvalonDock holding the whole shell layout. The <see cref="SessionPane"/> is the single, non-closable, non-floatable
/// document of the central document pane (no tab strip); Sessions, Recent and Tags (left pane) and Focus (right pane) are panels around it (<see cref="BuildShell"/>);
/// docked preview panels are placed by <see cref="PreviewDockLayoutApplier"/>. Implements <see cref="IDockingSurface"/> for the previews and <see cref="IShellLayout"/> for the
/// view models and menu commands. The layout is remembered (FR.6.18) through <see cref="ShellLayoutMemory"/>. The docking theme is <see cref="TessynDockTheme"/>, set once; light and dark are the application's colour dictionary (FR.6.21).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class DockingArea : UserControl, IDockingSurface, IShellLayout
{
	private static readonly string[] ShellPanelIds =
	[
		Constants.DockedShell.SessionsPanelId, Constants.DockedShell.RecentPanelId, Constants.DockedShell.TagsPanelId, Constants.DockedShell.FocusPanelId,
	];

	private readonly ShellLayoutBuilder _builder = new();
	private readonly ShellLayoutSerializer _serializer = new();
	private readonly PreviewDockLayoutApplier _applier = new();
	private readonly PreviewDockLayoutReader _reader = new();
	private readonly Dictionary<string, ShellPanelDefinition> _definitions = new();
	private readonly Dictionary<string, LayoutAnchorable> _panels = new();
	private readonly Dictionary<string, PreviewDockLayoutModel> _places = new();
	private readonly Dictionary<string, PreviewDockLayoutModel> _lastDocked = new();
	private readonly DispatcherTimer _saveTimer;
	private readonly DockingVisualTree _tree = new();
	private readonly DockingStripSync _strip;
	private readonly DockingPanelRaiser _raiser;
	private LayoutDocumentPane _documentPane;
	private LayoutRoot? _subscribedRoot;
	private ShellLayoutMemory? _memory;
	private ShellFloatingPlacements? _floatingPlaces;
	private ILogger? _logger;
	private Func<(IReadOnlyList<MonitorBounds> Monitors, MonitorBounds Primary)>? _monitors;
	private string _lastState = string.Empty;
	private string _lastSignature = string.Empty;
	private bool _widthsPending;
	private bool _savePending;
	private int _buildDepth;
	private bool _floatingHidden;

	public DockingArea() : this(new SessionPane())
	{
	}

	/// <summary>An area around another central content than the real session pane (a test cannot load the real one: it needs the running app's services).</summary>
	public DockingArea(FrameworkElement sessionView)
	{
		InitializeComponent();
		SessionView = sessionView;
		DockManager.Theme = new TessynDockTheme();   // the one theme, for light and dark alike: the colours are the application resources' (FR.6.21)
		Chrome = new DockingChrome(DockManager, this);   // the compact chrome, the focus guard, the slide-out closer (FR.6.13, FR.6.20)
		DockManager.AnchorableHeaderTemplateSelector = new ShellPanelHeaderTemplateSelector();   // the Focus tab shows the badge (FR.6.16)
		_strip = new DockingStripSync(DockManager, () => _documentPane, _tree);
		_raiser = new DockingPanelRaiser(DockManager, _tree, () => Chrome.SlideOut.IsOpen);
		_documentPane = CreateDocumentPane();
		var root = new LayoutPanel { Orientation = Orientation.Horizontal };
		root.Children.Add(_documentPane);
		SetLayout(new LayoutRoot { RootPanel = root });
		UseDocumentPane(_documentPane);
		_saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Constants.DockedShell.LayoutSaveDelay };
		_saveTimer.Tick += (_, _) =>
		{
			_saveTimer.Stop();
			Evaluate();   // a splitter drag or a window resize has no event of its own: it is found here, at most once per delay
			if (!_savePending)
				return;

			_savePending = false;
			SaveNow();
		};
		DockManager.LayoutUpdated += (_, _) => OnLayoutPass();
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

	/// <summary>The session pane, the central document.</summary>
	public FrameworkElement SessionView { get; }

	/// <summary>The compact chrome, keyboard routing for floating panels, focus guard and slide-out closer (FR.6.13, FR.6.20).</summary>
	public DockingChrome Chrome { get; }

	/// <summary>The AvalonDock manager.</summary>
	public DockingManager Manager => DockManager;

	public LayoutRoot Root => DockManager.Layout;

	public LayoutDocumentPane DocumentPane => _documentPane;

	public double AreaWidth => ActualWidth;

	public double AreaHeight => ActualHeight;

	public event EventHandler? LayoutChanged;

	/// <summary>How many times the places of the panels were read (a layout pass without a docking change reads none); for tests.</summary>
	public int CaptureCount { get; private set; }

	/// <summary>The layout is being replaced: the library's events during it are not changes.</summary>
	private bool _building => _buildDepth > 0;

	/// <summary>Raised after any change of the shell layout (a panel opened, closed, moved, tabbed, floated, auto-hidden, resized).</summary>
	public event EventHandler? Changed;

	/// <summary>A shell panel's model (null for an unknown id or before <see cref="BuildShell"/>); for tests and the dock layer.</summary>
	public LayoutAnchorable? FindPanel(string panelId) => _panels.GetValueOrDefault(panelId);

	/// <summary>
	/// Builds the whole shell layout (FR.6.11, FR.6.12, FR.6.18): from the saved layout when <paramref name="memory"/> has a usable one, else the default layout. Run once, before the
	/// window is shown. <paramref name="monitors"/> tells where the screens are (a restored floating panel off every monitor is clamped, FR.29.16).
	/// </summary>
	public void BuildShell(IReadOnlyList<ShellPanelDefinition> panels, ShellLayoutMemory memory, ILogger logger, Func<(IReadOnlyList<MonitorBounds> Monitors, MonitorBounds Primary)>? monitors = null, IFloatingPanelPlacement? floating = null)
	{
		_floatingPlaces = floating == null ? null : new ShellFloatingPlacements(floating, FloatingWindowHandle);   // floating panels are remembered with monitor and scale (FR.6.18, FR.29.16)
		_memory = memory;
		_logger = logger;
		Chrome.UseLogger(logger);
		_raiser.Logger = logger;
		_monitors = monitors;
		foreach (var definition in panels)
			_definitions[definition.Id] = definition;

		_buildDepth++;
		try
		{
			BuildCore(memory, logger);
		}
		finally
		{
			_buildDepth--;
		}

		_widthsPending = true;
		_lastState = StateText();
		_lastSignature = _lastState + SizeText();
		logger.LogInformation(Constants.ShellLayout.LayoutLogFormat, _lastState);
	}

	private void BuildCore(ShellLayoutMemory memory, ILogger logger)
	{
		var start = memory.ReadStart();
		string? failure = null;
		if (start.Xml != null && TryRestore(start.Xml, out failure))
		{
			logger.LogInformation(Constants.ShellLayout.LayoutRestoredLog);
		}
		else
		{
			if (start.Xml != null)
			{
				start = memory.DefaultFor(failure ?? string.Empty);
				logger.LogWarning(Constants.ShellLayout.LayoutDefaultLog, start.Reason);
			}
			else
			{
				logger.LogInformation(Constants.ShellLayout.LayoutDefaultLog, start.Reason);
			}

			InstallDefault(start.LeftWidth, start.LeftAutoHidden, []);
		}
	}

	/// <summary>Hides or shows the floating panels' windows (the screensaver cover does not reach them, FR.29.19).</summary>
	public void Reveal(LayoutAnchorable panel)
	{
		panel.IsSelected = true;   // a tab behind another comes forward
		if (panel.IsAutoHidden)
			_raiser.SlideOut(panel);
		else if (panel.IsFloating)
			_raiser.Raise(panel);   // lifted, not activated: the composer keeps the keyboard
	}

	public IntPtr FloatingWindowHandle(LayoutAnchorable panel) => _raiser.WindowOf(panel);

	public void SetFloatingVisible(bool visible)
	{
		_floatingHidden = !visible;
		foreach (var window in DockManager.FloatingWindows.ToList())
			window.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
	}

	/// <summary>Saves a layout change that is still waiting for its delay (the window is closing).</summary>
	public void FlushLayout()
	{
		_saveTimer.Stop();
		Evaluate();
		if (!_savePending)
			return;

		_savePending = false;
		SaveNow();
	}

	public bool IsOpen(string panelId)
	{
		if (panelId == Constants.DockedShell.SessionPanelId)
			return true;

		return _panels.TryGetValue(panelId, out var panel) && IsInLayout(panel);
	}

	public void BringForward(string panelId)
	{
		if (!_panels.TryGetValue(panelId, out var panel))
			return;

		if (!IsInLayout(panel))
		{
			if (panel.IsHidden && panel.Root != null)
				panel.Root.Hidden.Remove(panel);   // the library's Show picks a pane of its own after a restart; the panel is placed from its remembered place instead

			PlaceClosed(panel);
		}

		panel.IsSelected = true;
		NotifySelected(panel);   // also when it already was the selected tab (the change event does not fire then)
		if (panel.IsAutoHidden)
			_raiser.SlideOut(panel);
		else if (panel.IsFloating)
			_raiser.Raise(panel);

		Evaluate();
	}

	/// <summary>A panel's tab became the selected one of its pane (FR.6.11): its content reacts (the Recent panel's collapse rule, the Focus panel's Closed expander).</summary>
	private void TrackSelection(LayoutAnchorable panel)
	{
		panel.IsSelectedChanged -= OnPanelIsSelectedChanged;
		panel.IsSelectedChanged += OnPanelIsSelectedChanged;
	}

	private void OnPanelIsSelectedChanged(object? sender, EventArgs e)
	{
		if (sender is LayoutAnchorable { IsSelected: true } panel)
			NotifySelected(panel);

		Evaluate();
	}

	private void NotifySelected(LayoutAnchorable panel)
	{
		if (panel.Content is IPanelSelectionAware aware)
			aware.OnPanelSelected();
	}

	public void Close(string panelId)
	{
		if (panelId == Constants.DockedShell.SessionPanelId || !_panels.TryGetValue(panelId, out var panel) || !IsInLayout(panel))
			return;

		CapturePlace(panel);   // the place it comes back to
		panel.Hide();
		Evaluate();
	}

	public bool CanToggleAutoHide(string panelId) => _panels.TryGetValue(panelId, out var panel) && IsInLayout(panel) && !panel.IsFloating && panel.Parent != null;

	public bool IsAutoHidden(string panelId) => _panels.TryGetValue(panelId, out var panel) && IsInLayout(panel) && panel.IsAutoHidden;

	public void ToggleAutoHide(string panelId)
	{
		if (!CanToggleAutoHide(panelId))
			return;

		_panels[panelId].ToggleAutoHide();
		Evaluate();
	}

	public void ResetLayout()
	{
		if (_definitions.Count == 0)
			return;

		var others = Root.Descendents().OfType<LayoutAnchorable>().Where(a => !ShellPanelIds.Contains(a.ContentId)).ToList();
		InstallDefault(Constants.DockedShell.DefaultLeftPaneWidth, false, others);
		_logger?.LogInformation(Constants.ShellLayout.LayoutResetLog);
		Evaluate();
		_saveTimer.Stop();
		_savePending = false;
		SaveNow();
	}

	/// <summary>Collapses or shows the document pane's tab strip now (<see cref="DockingStripSync"/>); true when the strip was found.</summary>
	public bool UpdateDocumentTabStrip() => _strip.Update();

	/// <summary>The one line the E2E tests read: every shell panel's state (side, tab index, open / autohide / floating, selected) or <c>closed</c>.</summary>
	public string StateText()
	{
		var parts = new List<string> { $"{Constants.DockedShell.SessionsPanelId}={PanelState(Constants.DockedShell.SessionsPanelId)}" };
		parts.Add($"{Constants.DockedShell.RecentPanelId}={PanelState(Constants.DockedShell.RecentPanelId)}");
		parts.Add($"{Constants.DockedShell.TagsPanelId}={PanelState(Constants.DockedShell.TagsPanelId)}");
		parts.Add($"{Constants.ShellLayout.SessionViewLogName}=center");
		parts.Add($"{Constants.DockedShell.FocusPanelId}={PanelState(Constants.DockedShell.FocusPanelId)}");
		return string.Join(' ', parts);
	}

	private string PanelState(string id)
	{
		if (!_panels.TryGetValue(id, out var panel) || !IsInLayout(panel))
			return "closed";

		var mode = panel.IsFloating ? "floating" : panel.IsAutoHidden ? "autohide" : "open";
		var side = panel.IsFloating ? "floating" : _reader.SideOf(panel, _documentPane)?.ToString().ToLowerInvariant() ?? "center";
		var tab = panel.Parent is ILayoutContainer container ? container.Children.ToList().IndexOf(panel) : 0;
		return $"{side},tab{Math.Max(tab, 0)},{mode}{(panel.IsSelected ? ",selected" : string.Empty)}";
	}

	/// <summary>The panes' sizes and the floating rectangles, so that a resize counts as a change to save though it is not one to log.</summary>
	private string SizeText()
	{
		var text = new System.Text.StringBuilder();
		foreach (var panel in _panels.Values.Where(IsInLayout))
		{
			if (panel.IsFloating)
				text.Append($"|{panel.ContentId}:{panel.FloatingLeft:0},{panel.FloatingTop:0},{panel.FloatingWidth:0},{panel.FloatingHeight:0}");
			else if (panel.IsAutoHidden)
				text.Append($"|{panel.ContentId}:{panel.AutoHideWidth:0},{panel.AutoHideHeight:0}");
			else if (panel.Parent is LayoutAnchorablePane pane)
				text.Append($"|{panel.ContentId}:{pane.DockWidth.Value:0.##}{pane.DockWidth.GridUnitType},{pane.DockHeight.Value:0.##}{pane.DockHeight.GridUnitType}");
		}

		return text.ToString();
	}

	/// <summary>The panel is part of the layout: docked, tabbed, auto-hidden or floating, and not hidden or closed.</summary>
	private bool IsInLayout(LayoutAnchorable panel) => panel.Root != null && !panel.IsHidden && panel.Parent is ILayoutContainer container && container.Children.Contains(panel);

	private LayoutDocumentPane CreateDocumentPane()
	{
		var document = new LayoutDocument
		{
			ContentId = Constants.PreviewDock.SessionDocumentId,
			Title = Constants.PreviewDock.SessionDocumentTitle,
			Content = SessionView,
			CanClose = false,
			CanFloat = false,
		};
		return new LayoutDocumentPane(document);
	}

	private LayoutAnchorable CreatePanel(ShellPanelDefinition definition) => new()
	{
		ContentId = definition.Id,
		Title = definition.Title,
		Content = definition.Content,
		CanClose = true,
		CanFloat = true,
		CanAutoHide = true,
	};

	private void SetLayout(LayoutRoot layout)
	{
		_buildDepth++;
		try
		{
			if (_subscribedRoot != null)
				_subscribedRoot.Updated -= OnRootUpdated;

			DockManager.Layout = layout;
			_subscribedRoot = layout;
			layout.Updated += OnRootUpdated;
		}
		finally
		{
			_buildDepth--;
		}
	}

	private void OnRootUpdated(object? sender, EventArgs e)
	{
		_strip.Invalidate();   // the library may have built the panes again
		Evaluate();
	}

	private void UseDocumentPane(LayoutDocumentPane pane)
	{
		_documentPane = pane;
		pane.ChildrenCollectionChanged += (_, _) => _strip.Invalidate();
		_strip.Invalidate();
	}

	/// <summary>
	/// Replaces the layout with the default one (FR.6.12) around a new document, with new panels over the same contents. <paramref name="others"/> (the open preview panels) are
	/// taken out of the old layout and put back at their default place (FR.6.19).
	/// </summary>
	private void InstallDefault(double leftWidth, bool leftAutoHidden, IReadOnlyList<LayoutAnchorable> others)
	{
		_buildDepth++;
		try
		{
			InstallDefaultCore(leftWidth, leftAutoHidden, others);
		}
		finally
		{
			_buildDepth--;
		}

		_widthsPending = true;
	}

	private void InstallDefaultCore(double leftWidth, bool leftAutoHidden, IReadOnlyList<LayoutAnchorable> others)
	{
		foreach (var other in others)
			(other.Parent as ILayoutContainer)?.RemoveChild(other);

		_places.Clear();
		_lastDocked.Clear();
		_panels.Clear();
		UseDocumentPane(CreateDocumentPane());
		var left = new List<LayoutAnchorable>();
		foreach (var definition in _definitions.Values.Where(d => d.IsLeft).OrderBy(d => LeftOrder(d.Id)))
		{
			var panel = CreatePanel(definition);
			TrackSelection(panel);
			_panels[definition.Id] = panel;
			left.Add(panel);
		}

		LayoutAnchorable? focus = null;
		if (_definitions.TryGetValue(Constants.DockedShell.FocusPanelId, out var focusDefinition))
		{
			focus = CreatePanel(focusDefinition);
			TrackSelection(focus);
			_panels[focus.ContentId] = focus;
		}

		SetLayout(_builder.Build(_documentPane, left, focus, leftWidth, ActualWidth));
		if (leftAutoHidden && _panels.TryGetValue(Constants.DockedShell.SessionsPanelId, out var sessions))
		{
			foreach (var tab in left)
				tab.AutoHideWidth = Math.Max(Constants.ShellLayout.MinLeftPaneWidth, leftWidth);   // the library reads the slide-out's width from a control that does not exist yet

			sessions.ToggleAutoHide();
		}

		foreach (var other in others)
			_applier.Apply(Root, _documentPane, other, null, ActualWidth, ActualHeight);
	}

	private int LeftOrder(string id) => id switch
	{
		Constants.DockedShell.SessionsPanelId => 0,
		Constants.DockedShell.RecentPanelId => 1,
		_ => 2,
	};

	private bool TryRestore(string xml, out string? failure)
	{
		failure = null;
		try
		{
			_serializer.Deserialize(DockManager, xml, id => id == Constants.PreviewDock.SessionDocumentId ? SessionView : _definitions.GetValueOrDefault(id)?.Content);
		}
		catch (Exception ex)
		{
			failure = string.Format(Constants.ShellLayout.ReasonUnreadableFormat, ex.Message);
			return false;
		}

		var session = Root.Descendents().OfType<LayoutDocument>().FirstOrDefault(d => d.ContentId == Constants.PreviewDock.SessionDocumentId);
		if (session?.Parent is not LayoutDocumentPane pane)
		{
			failure = Constants.ShellLayout.ReasonNoSessionView;
			return false;
		}

		session.CanClose = false;
		session.CanFloat = false;
		UseDocumentPane(pane);
		if (_subscribedRoot != null)
			_subscribedRoot.Updated -= OnRootUpdated;

		_subscribedRoot = Root;
		Root.Updated += OnRootUpdated;
		_places.Clear();
		_lastDocked.Clear();
		_panels.Clear();
		var found = Root.Descendents().OfType<LayoutAnchorable>().Where(a => ShellPanelIds.Contains(a.ContentId)).ToList();
		var hidden = Root.Hidden.Where(h => ShellPanelIds.Contains(h.ContentId)).ToList();
		foreach (var definition in _definitions.Values)
		{
			// A panel saved as closed sits in the hidden list: that one is the panel in use (one element per id, so the next save writes one). Not saved at all: a new one, shown at its default place later.
			var panel = found.FirstOrDefault(a => a.ContentId == definition.Id) ?? hidden.FirstOrDefault(h => h.ContentId == definition.Id) ?? CreatePanel(definition);
			foreach (var duplicate in hidden.Where(h => h.ContentId == definition.Id && !ReferenceEquals(h, panel)))
				Root.Hidden.Remove(duplicate);

			panel.Title = definition.Title;
			panel.CanClose = true;
			panel.CanFloat = true;
			panel.CanAutoHide = true;
			TrackSelection(panel);
			_panels[definition.Id] = panel;
		}

		ClampFloating(RestoreFloatingPlaces(xml));
		return true;
	}

	/// <summary>
	/// A floating panel saved with its monitor and scale is put back by the placement rules (FR.29.16): sizes by the ratio of the scales, clamped to the monitors present; one saved without
	/// them (an older layout) is left to <see cref="ClampFloating"/>. Returns the ids placed here.
	/// </summary>
	private HashSet<string> RestoreFloatingPlaces(string xml)
	{
		var placed = new HashSet<string>();
		if (_floatingPlaces == null)
			return placed;

		var saved = _serializer.ReadFloating(xml);
		foreach (var panel in _panels.Values.Where(p => p.IsFloating && saved.ContainsKey(p.ContentId)))
		{
			var place = saved[panel.ContentId];
			var rectangle = _floatingPlaces.Plan(place);
			panel.FloatingLeft = rectangle.Left;
			panel.FloatingTop = rectangle.Top;
			panel.FloatingWidth = Math.Max(rectangle.Width, Constants.PreviewDock.MinPaneSize);
			panel.FloatingHeight = Math.Max(rectangle.Height, Constants.PreviewDock.MinPaneSize);
			_floatingPlaces.Seed(panel.ContentId, place);
			placed.Add(panel.ContentId);
			_logger?.LogInformation(Constants.ShellLayout.LayoutFloatingPlacedLog, panel.ContentId, place.MonitorDeviceName, place.Dpi);
		}

		return placed;
	}

	/// <summary>A restored floating panel off every monitor is moved onto one (FR.6.18, FR.29.16).</summary>
	private void ClampFloating(IReadOnlySet<string> placed)
	{
		if (_monitors == null)
			return;

		var (monitors, primary) = _monitors();
		foreach (var panel in _panels.Values.Where(p => p.IsFloating && !placed.Contains(p.ContentId)))
		{
			var saved = new WindowPlacementModel { Left = (int)panel.FloatingLeft, Top = (int)panel.FloatingTop, Width = (int)panel.FloatingWidth, Height = (int)panel.FloatingHeight };
			var clamped = new WindowPlacementClamper().Clamp(saved, monitors, primary);
			if (clamped == null || clamped.Left == saved.Left && clamped.Top == saved.Top && clamped.Width == saved.Width && clamped.Height == saved.Height)
				continue;

			panel.FloatingLeft = clamped.Left;
			panel.FloatingTop = clamped.Top;
			panel.FloatingWidth = clamped.Width;
			panel.FloatingHeight = clamped.Height;
			_logger?.LogInformation(Constants.ShellLayout.LayoutFloatingClampedLog, panel.ContentId);
		}
	}

	/// <summary>Opens a panel that is not in the layout (closed by the tab's Close, or not in the saved layout) at its last place, else its default one (FR.6.12, FR.6.14).</summary>
	private void PlaceClosed(LayoutAnchorable panel)
	{
		var model = _places.TryGetValue(panel.ContentId, out var last) ? last.Clone() : DefaultPlace(panel.ContentId);
		model.Shown = true;
		_applier.Apply(Root, _documentPane, panel, model, ActualWidth, ActualHeight);
	}

	/// <summary>The default place of a shell panel: the left panels as tabs of one pane at the left, Focus at the right.</summary>
	private PreviewDockLayoutModel DefaultPlace(string id)
	{
		if (id == Constants.DockedShell.FocusPanelId)
			return new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = ActualWidth > 0 ? _builder.FocusWidth(ActualWidth) : 0 };

		return new PreviewDockLayoutModel
		{
			Side = PreviewDockSide.Left,
			Size = Constants.DockedShell.DefaultLeftPaneWidth,
			StackedWith = [.. ShellPanelIds.Where(p => p != id && p != Constants.DockedShell.FocusPanelId)],
			TabIndex = LeftOrder(id),
		};
	}

	private void CapturePlace(LayoutAnchorable panel)
	{
		var read = _reader.Read(panel, _documentPane, _lastDocked.GetValueOrDefault(panel.ContentId));
		_places[panel.ContentId] = read;
		if (!read.Floating)
			_lastDocked[panel.ContentId] = read;
	}

	/// <summary>One layout pass of the manager (it runs often): nothing is read here; a probe for a size change is armed, and the tab strip is looked at only when it may be wrong.</summary>
	private void OnLayoutPass()
	{
		if (_strip.IsDirty)
			_strip.Update();

		LayoutChanged?.Invoke(this, EventArgs.Empty);
		if (_building || _memory == null)
			return;

		if (_widthsPending && ActualWidth > 0)
			FitWidths();

		if (!_saveTimer.IsEnabled)
			_saveTimer.Start();
	}

	/// <summary>
	/// After the first measure: Focus gets its exact 35 % when it was given a proportional share, and fixed pane widths that would leave the Session view narrower than
	/// <see cref="Constants.ShellLayout.MinSessionWidth"/> (a layout saved on a bigger screen) are shrunk proportionally. Not a change of the user's: it is not saved.
	/// </summary>
	private void FitWidths()
	{
		_widthsPending = false;
		if (_panels.TryGetValue(Constants.DockedShell.FocusPanelId, out var focus) && focus.Parent is LayoutAnchorablePane { DockWidth.IsStar: true } starPane && !focus.IsAutoHidden && !focus.IsFloating)
		{
			starPane.DockWidth = new GridLength(_builder.FocusWidth(ActualWidth));
			_documentPane.DockWidth = new GridLength(1, GridUnitType.Star);
		}

		var panes = new[] { Constants.DockedShell.SessionsPanelId, Constants.DockedShell.FocusPanelId }
			.Select(id => _panels.GetValueOrDefault(id))
			.Where(p => p is { IsAutoHidden: false, IsFloating: false } && IsInLayout(p))
			.Select(p => p!.Parent as LayoutAnchorablePane)
			.OfType<LayoutAnchorablePane>().Where(p => p.DockWidth.IsAbsolute).Distinct().ToList();
		var fixedWidth = panes.Sum(p => p.DockWidth.Value);
		var available = ActualWidth - Constants.ShellLayout.MinSessionWidth - panes.Count * DockManager.GridSplitterWidth;   // the theme's splitters (one beside each fixed pane) are not the Session view's
		if (fixedWidth > 0 && fixedWidth > available)
		{
			var factor = Math.Max(available, 0) / fixedWidth;
			foreach (var pane in panes)
				pane.DockWidth = new GridLength(Math.Max(Constants.PreviewDock.MinPaneSize, Math.Round(pane.DockWidth.Value * factor)));
		}

		_lastState = StateText();
		_lastSignature = _lastState + SizeText();   // the baseline: what the library and this fit made of it is not a change to save
	}

	/// <summary>Reads the layout when it really changed (a docking change, a selection, or a size found by the probe): logs the state, tells the listeners, and arms the save.</summary>
	private void Evaluate()
	{
		if (_building || _memory == null)
			return;

		var state = StateText();
		var signature = state + SizeText();
		if (signature == _lastSignature)
			return;

		_lastSignature = signature;
		CaptureCount++;
		foreach (var panel in _panels.Values.Where(IsInLayout))
			CapturePlace(panel);

		_floatingPlaces?.Observe(_panels.Values.Where(p => IsInLayout(p) && p.IsFloating).ToList());   // a place Windows forced after a display change is not taken over

		if (state != _lastState)
		{
			_lastState = state;
			_logger?.LogInformation(Constants.ShellLayout.LayoutLogFormat, state);
		}

		_savePending = true;
		_saveTimer.Stop();
		_saveTimer.Start();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void SaveNow()
	{
		if (_memory is not { Persistent: true })
			return;

		try
		{
			_memory.Save(_serializer.Serialize(DockManager, new HashSet<string>([.. ShellPanelIds, Constants.PreviewDock.SessionDocumentId]), _floatingPlaces?.Entries));
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, Constants.ShellLayout.SaveFailedLog);
		}
	}
}
