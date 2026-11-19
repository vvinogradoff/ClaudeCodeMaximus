using System.Windows;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The docking area's part of the programmatic test hook (FR.6.22, FR.29.26): <see cref="IDockingTestOperations"/> over the shell panels and the docked preview panels (by content id),
/// and the switch that makes floating panel windows test-safe. Only <see cref="UseTestMode"/> and the hook itself reach it, and only under <c>--test-window</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class DockingArea : IDockingTestOperations
{
	private bool _testMode;
	private readonly DockingTestFloatingPolicy _floatingPolicy = new();

	/// <summary>
	/// Test windows: every floating panel window the library creates from now on (and those that exist) is off-screen, not in the taskbar, never activated and under the
	/// test-window focus guard (FR.6.22, FR.29.26).
	/// </summary>
	public void UseTestMode()
	{
		_testMode = true;
		foreach (var window in DockManager.FloatingWindows.ToList())
			_floatingPolicy.Apply(window, _logger);
	}

	public IReadOnlyList<DockingPanelState> State()
	{
		DockManager.UpdateLayout();
		var panes = Root.Descendents().Where(e => e is LayoutAnchorablePane or LayoutDocumentPane or LayoutAnchorGroup).ToList();
		var states = new List<DockingPanelState>();
		var session = new DockingPanelState
		{
			Id = Constants.DockedShell.SessionPanelId,
			Kind = Constants.DockingTestHook.KindSession,
			Shown = true,
			Side = Constants.DockingTestHook.SideCenter,
			Pane = panes.IndexOf(_documentPane),
			Selected = true,
			Width = PaneSize(_documentPane).Width,
			Height = PaneSize(_documentPane).Height,
		};
		states.Add(session);
		var shell = ShellPanelIds.Select(id => _panels.GetValueOrDefault(id)).OfType<LayoutAnchorable>().ToList();
		var others = Root.Descendents().OfType<LayoutAnchorable>().Concat(Root.Hidden).Where(a => !ShellPanelIds.Contains(a.ContentId)).GroupBy(a => a.ContentId).Select(g => g.First());
		foreach (var panel in shell)
			states.Add(StateOf(panel, Constants.DockingTestHook.KindShell, panes));

		foreach (var panel in others)
			states.Add(StateOf(panel, Constants.DockingTestHook.KindPreview, panes));

		return states;
	}

	private DockingPanelState StateOf(LayoutAnchorable panel, string kind, List<ILayoutElement> panes)
	{
		var badge = panel.ContentId == Constants.DockedShell.FocusPanelId && panel.Content is FrameworkElement { DataContext: FocusViewModel { IsBadgeVisible: true } focus }
			? focus.OpenCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
			: null;
		if (!IsInLayout(panel))
			return new DockingPanelState { Id = panel.ContentId, Kind = kind, Shown = false, Side = Constants.DockingTestHook.SideClosed, Badge = badge };

		var container = panel.Parent as ILayoutContainer;
		var tab = container == null ? 0 : container.Children.ToList().IndexOf(panel);
		var pane = container == null ? -1 : panes.IndexOf(container as ILayoutElement ?? panel);
		var common = new DockingPanelState
		{
			Id = panel.ContentId,
			Kind = kind,
			Shown = true,
			Pane = pane,
			TabIndex = Math.Max(tab, 0),
			Selected = panel.IsSelected,
			Badge = badge,
		};
		if (panel.IsFloating)
		{
			return common with
			{
				Side = Constants.DockingTestHook.SideFloating,
				Floating = true,
				FloatRect = new DockingFloatRect(panel.FloatingLeft, panel.FloatingTop, panel.FloatingWidth, panel.FloatingHeight),
				Width = panel.FloatingWidth,
				Height = panel.FloatingHeight,
			};
		}

		var side = ReferenceEquals(container, _documentPane) ? Constants.DockingTestHook.SideCenter : SideName(_reader.SideOf(panel, _documentPane));
		if (panel.IsAutoHidden)
			return common with { Side = side, AutoHide = true, Edge = side, Width = panel.AutoHideWidth, Height = panel.AutoHideHeight };

		var size = PaneSize(container as ILayoutElement);
		return common with { Side = side, Width = size.Width, Height = size.Height };
	}

	private string SideName(PreviewDockSide? side) => side?.ToString().ToLowerInvariant() ?? Constants.DockingTestHook.SideCenter;

	/// <summary>The size of the pane's control (0 when it has none now).</summary>
	private (double Width, double Height) PaneSize(ILayoutElement? pane)
	{
		if (pane == null)
			return (0, 0);

		var control = _tree.Descendants<FrameworkElement>(DockManager).FirstOrDefault(c => c is LayoutAnchorablePaneControl { Model: var m } && ReferenceEquals(m, pane) || c is LayoutDocumentPaneControl { Model: var d } && ReferenceEquals(d, pane));
		return control == null ? (0, 0) : (Math.Round(control.ActualWidth, 1), Math.Round(control.ActualHeight, 1));
	}

	public DockingTestResult Dock(string panel, string side)
	{
		if (!Enum.TryParse<PreviewDockSide>(side, true, out var parsed) || !Enum.IsDefined(parsed))
			return Refuse(Constants.DockingTestHook.UnknownSideError, side);

		if (!TryMovable(panel, Constants.DockingTestHook.VerbDocked, out var target, out var refusal))
			return refusal;

		var read = _reader.Read(target, _documentPane, _lastDocked.GetValueOrDefault(target.ContentId));
		var horizontal = parsed is PreviewDockSide.Left or PreviewDockSide.Right;
		var wasHorizontal = read.Side is PreviewDockSide.Left or PreviewDockSide.Right;
		var model = new PreviewDockLayoutModel { Side = parsed, Size = horizontal == wasHorizontal ? read.Size : 0 };
		Move(target, model);
		return DockingTestResult.Success;
	}

	public DockingTestResult Tab(string panel, string withPanel)
	{
		if (!TryMovable(panel, Constants.DockingTestHook.VerbStacked, out var target, out var refusal))
			return refusal;

		if (withPanel == Constants.DockedShell.SessionPanelId)
			return TabIntoSession(target);

		var with = Find(withPanel);
		if (with == null)
			return Refuse(Constants.DockingTestHook.UnknownPanelError, withPanel);

		if (ReferenceEquals(with, target))
			return DockingTestResult.Success;

		if (!IsInLayout(with) || with.IsFloating || with.IsAutoHidden || with.Parent is not LayoutAnchorablePane pane)
			return Refuse(Constants.DockingTestHook.TabTargetError, panel, withPanel);

		Detach(target);
		pane.Children.Add(target);
		target.IsSelected = true;
		Evaluate();
		return DockingTestResult.Success;
	}

	private DockingTestResult TabIntoSession(LayoutAnchorable target)
	{
		Detach(target);
		try
		{
			_documentPane.Children.Add(target);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, Constants.DockingTestHook.RequestFailedLog);
			return Refuse(Constants.DockingTestHook.TabIntoSessionError, target.ContentId);
		}

		target.IsSelected = true;
		Evaluate();
		return DockingTestResult.Success;
	}

	public DockingTestResult Float(string panel, DockingFloatRect? rect)
	{
		if (!TryMovable(panel, Constants.DockingTestHook.VerbFloated, out var target, out var refusal))
			return refusal;

		if (rect != null)
		{
			target.FloatingLeft = rect.Left;
			target.FloatingTop = rect.Top;
			target.FloatingWidth = Math.Max(rect.Width, Constants.PreviewDock.MinPaneSize);
			target.FloatingHeight = Math.Max(rect.Height, Constants.PreviewDock.MinPaneSize);
		}

		if (target.IsFloating)
			return DockingTestResult.Success;

		if (target.IsAutoHidden)
			target.ToggleAutoHide();   // the library floats a docked panel

		CapturePlace(target);   // the place it returns to when docked again
		target.Float();
		Evaluate();
		return DockingTestResult.Success;
	}

	public DockingTestResult AutoHide(string panel, bool on)
	{
		var target = Find(panel);
		if (target == null)
			return Refuse(Constants.DockingTestHook.UnknownPanelError, panel);

		if (!IsInLayout(target))
			return Refuse(Constants.DockingTestHook.NotInLayoutError, panel);

		if (target.IsFloating || target.Parent == null)
			return Refuse(Constants.DockingTestHook.NotDockedError, panel);

		if (target.IsAutoHidden == on)
			return DockingTestResult.Success;

		if (target.IsAutoHidden == false && ReferenceEquals(target.Parent, _documentPane))
			return Refuse(Constants.DockingTestHook.NotDockedError, panel);

		target.ToggleAutoHide();
		DockManager.UpdateLayout();
		Evaluate();
		return DockingTestResult.Success;
	}

	public DockingTestResult SlideOut(string panel)
	{
		var target = Find(panel);
		if (target == null)
			return Refuse(Constants.DockingTestHook.UnknownPanelError, panel);

		if (!IsInLayout(target) || !target.IsAutoHidden)
			return Refuse(Constants.DockingTestHook.NotAutoHiddenError, panel);

		target.IsSelected = true;   // the edge tab of the selected panel is the one the library slides out
		DockManager.UpdateLayout();   // the edge tabs exist only after the layout pass that followed the auto-hide
		var ok = _raiser.SlideOut(target);
		return ok ? DockingTestResult.Success : Refuse(Constants.DockingTestHook.SlideOutFailedError, panel);
	}

	DockingTestResult IDockingTestOperations.Close(string panel)
	{
		if (panel == Constants.DockedShell.SessionPanelId)
			return Refuse(Constants.DockingTestHook.SessionRefusedError, Constants.DockingTestHook.VerbClosed);

		var target = Find(panel);
		if (target == null)
			return Refuse(Constants.DockingTestHook.UnknownPanelError, panel);

		if (!IsInLayout(target))
			return Refuse(Constants.DockingTestHook.NotInLayoutError, panel);

		if (_panels.ContainsKey(panel))
			Close(panel);
		else
			target.Close();   // a preview panel: the tab's Close, which its handle follows

		Evaluate();
		return DockingTestResult.Success;
	}

	public DockingTestResult Show(string panel)
	{
		if (panel == Constants.DockedShell.SessionPanelId)
			return DockingTestResult.Success;

		var target = Find(panel);
		if (target == null)
			return Refuse(Constants.DockingTestHook.UnknownPanelError, panel);

		if (_panels.ContainsKey(panel) || IsInLayout(target))
		{
			ForwardCore(target);
			return DockingTestResult.Success;
		}

		target.Show();   // a preview panel that was hidden
		Evaluate();
		return DockingTestResult.Success;
	}

	DockingTestResult IDockingTestOperations.BringForward(string panel)
	{
		if (panel == Constants.DockedShell.SessionPanelId)
			return DockingTestResult.Success;

		var target = Find(panel);
		if (target == null)
			return Refuse(Constants.DockingTestHook.UnknownPanelError, panel);

		if (!IsInLayout(target) && !_panels.ContainsKey(panel))
			return Refuse(Constants.DockingTestHook.NotInLayoutError, panel);

		ForwardCore(target);
		return DockingTestResult.Success;
	}

	DockingTestResult IDockingTestOperations.Reset()
	{
		ResetLayout();
		return DockingTestResult.Success;
	}

	/// <summary>Shell panels go through <see cref="BringForward"/>; any other panel is selected, slid out or raised the same way.</summary>
	private void ForwardCore(LayoutAnchorable target)
	{
		if (_panels.ContainsKey(target.ContentId))
		{
			BringForward(target.ContentId);
			return;
		}

		target.IsSelected = true;
		if (target.IsAutoHidden)
			_raiser.SlideOut(target);
		else if (target.IsFloating)
			_raiser.Raise(target);

		Evaluate();
	}

	/// <summary>A shell panel or a preview panel by its content id; null when unknown.</summary>
	private LayoutAnchorable? Find(string id) =>
		_panels.GetValueOrDefault(id) ?? Root.Descendents().OfType<LayoutAnchorable>().Concat(Root.Hidden).FirstOrDefault(a => a.ContentId == id);

	private DockingTestResult Refuse(string format, params object[] args) => new(false, string.Format(format, args));

	/// <summary>The panel for a move (dock, tab, float); false with the refusal when it is the Session view, unknown or not open.</summary>
	private bool TryMovable(string panel, string verb, out LayoutAnchorable target, out DockingTestResult refusal)
	{
		target = null!;
		refusal = DockingTestResult.Success;
		if (panel == Constants.DockedShell.SessionPanelId)
		{
			refusal = Refuse(Constants.DockingTestHook.SessionRefusedError, verb);
			return false;
		}

		var found = Find(panel);
		if (found == null)
		{
			refusal = Refuse(Constants.DockingTestHook.UnknownPanelError, panel);
			return false;
		}

		if (!IsInLayout(found))
		{
			refusal = Refuse(Constants.DockingTestHook.NotInLayoutError, panel);
			return false;
		}

		target = found;
		return true;
	}

	/// <summary>Takes the panel out of its pane, remembering where it was, and clears the panes and panels that were left empty. An auto-hidden or floating panel is docked first.</summary>
	private void Detach(LayoutAnchorable panel)
	{
		CapturePlace(panel);
		(panel.Parent as ILayoutContainer)?.RemoveChild(panel);
		Root.CollectGarbage();
	}

	private void Move(LayoutAnchorable panel, PreviewDockLayoutModel model)
	{
		Detach(panel);
		_applier.Apply(Root, _documentPane, panel, model, ActualWidth, ActualHeight);
		Evaluate();
	}
}
