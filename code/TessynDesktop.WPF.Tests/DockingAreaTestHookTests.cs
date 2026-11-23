using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.22, FR.29.26: the docking area's test hook operations (<see cref="IDockingTestOperations"/>) on an STA thread: dock, tab (also into the session area), float, auto-hide, slide-out,
/// close, show, bring forward and reset on the shell panels and on a preview panel, the state report, the refusals, the floating window placement of the test mode and the UI Automation names.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingAreaTestHookTests
{
	private const string Sessions = Constants.DockedShell.SessionsPanelId;
	private const string Recent = Constants.DockedShell.RecentPanelId;
	private const string Tags = Constants.DockedShell.TagsPanelId;
	private const string Focus = Constants.DockedShell.FocusPanelId;
	private const string Session = Constants.DockedShell.SessionPanelId;
	private static readonly MonitorBounds Screen = new(0, 0, 1920, 1080);

	private static (DockingArea Area, Window Window) Start(bool testMode = false)
	{
		var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
		if (testMode)
			area.UseTestMode();

		var window = new Window { Content = area, Width = 1400, Height = 800, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
		var state = new MemoryClientState();
		area.BuildShell(Panels(), new ShellLayoutMemory(new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance), state, true), NullLogger.Instance, () => ([Screen], Screen));
		window.Show();
		Settle(window);
		return (area, window);
	}

	private static List<ShellPanelDefinition> Panels() =>
	[
		Definition(Sessions, "Sessions", true),
		Definition(Recent, "Recent", true),
		Definition(Tags, "Tags", true),
		Definition(Focus, "Focus", false),
	];

	private static ShellPanelDefinition Definition(string id, string title, bool left) => new() { Id = id, Title = title, Content = new TextBlock { Text = id }, IsLeft = left };

	private static void Settle(Window window)
	{
		window.UpdateLayout();
		window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
		window.UpdateLayout();
	}

	private static DockingPanelState Of(DockingArea area, string id) => ((IDockingTestOperations)area).State().Single(p => p.Id == id);

	[Fact]
	public void TheDefaultState_ReportsEveryPanelWithItsPlaceAndSize()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();

			var state = ((IDockingTestOperations)area).State();

			Assert.Equal([Focus, Recent, Session, Sessions, Tags], state.Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal));
			var sessions = state.Single(p => p.Id == Sessions);
			Assert.Equal(("left", 0, true, true), (sessions.Side, sessions.TabIndex, sessions.Selected, sessions.Shown));
			Assert.Equal(320, sessions.Width, 1);
			Assert.Equal(sessions.Pane, state.Single(p => p.Id == Tags).Pane);
			Assert.Equal(2, state.Single(p => p.Id == Tags).TabIndex);
			Assert.Equal("right", state.Single(p => p.Id == Focus).Side);
			Assert.NotEqual(sessions.Pane, state.Single(p => p.Id == Focus).Pane);
			Assert.Equal("center", state.Single(p => p.Id == Session).Side);
			Assert.Equal("shell", sessions.Kind);
			Assert.Equal("session", state.Single(p => p.Id == Session).Kind);
			Assert.All(state, p => Assert.True(p.Width > 0 && p.Height > 0, p.Id));
			window.Close();
		});
	}

	[Fact]
	public void Dock_MovesAPanelNextToTheSessionArea_AndTab_StacksItOnAnother()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;

			Assert.True(ops.Dock(Recent, "bottom").Ok);
			Settle(window);
			Assert.Equal("bottom", Of(area, Recent).Side);
			Assert.NotEqual(Of(area, Sessions).Pane, Of(area, Recent).Pane);
			Assert.Equal(Of(area, Sessions).Pane, Of(area, Tags).Pane);

			Assert.True(ops.Tab(Tags, Recent).Ok);
			Settle(window);
			Assert.Equal(Of(area, Recent).Pane, Of(area, Tags).Pane);
			Assert.Equal(1, Of(area, Tags).TabIndex);
			Assert.Equal("bottom", Of(area, Tags).Side);
			Assert.True(Of(area, Tags).Selected);

			Assert.True(ops.Dock(Focus, "left").Ok);
			Settle(window);
			Assert.Equal("left", Of(area, Focus).Side);
			Assert.True(ops.Dock(Focus, "right").Ok);
			Settle(window);
			Assert.Equal("right", Of(area, Focus).Side);
			window.Close();
		});
	}

	[Fact]
	public void Tab_IntoTheSessionArea_MakesATabRow_AndDockingItOutHidesTheRowAgain()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;

			Assert.True(ops.Tab(Focus, Session).Ok);
			Settle(window);
			Assert.Equal(Of(area, Session).Pane, Of(area, Focus).Pane);
			Assert.Equal("center", Of(area, Focus).Side);
			Assert.True(area.UpdateDocumentTabStrip());
			Assert.Equal(2, area.DocumentPane.Children.Count);

			Assert.True(ops.Dock(Focus, "right").Ok);
			Settle(window);
			Assert.Equal("right", Of(area, Focus).Side);
			Assert.Single(area.DocumentPane.Children);
			window.Close();
		});
	}

	[Fact]
	public void BringForward_OfTheSessionView_SelectsItOverAFocusTabbedIntoTheSessionArea()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;
			Assert.True(ops.Tab(Focus, Session).Ok);
			Assert.True(ops.BringForward(Focus).Ok);
			Settle(window);
			Assert.True(Of(area, Focus).Selected);
			Assert.False(Of(area, Session).Selected);

			Assert.True(ops.BringForward(Session).Ok);   // what the Session button does (FR.6.23.7), TC.DS.49/54 drive it through the hook
			Settle(window);
			Assert.True(Of(area, Session).Selected);
			Assert.False(Of(area, Focus).Selected);
			window.Close();
		});
	}

	[Fact]
	public void TheSessionView_IsRefusedForEveryMove_AndTheStateIsUnchanged()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;
			var before = ops.State();

			Assert.False(ops.Close(Session).Ok);
			Assert.False(ops.Float(Session, null).Ok);
			Assert.False(ops.Dock(Session, "left").Ok);
			Assert.False(ops.Tab(Session, Recent).Ok);
			Assert.False(ops.AutoHide(Session, true).Ok);
			Assert.True(area.IsOpen(Session));
			Assert.Equal(before.Count, ops.State().Count);
			window.Close();
		});
	}

	[Fact]
	public void UnknownPanelsSidesAndClosedPanels_AreRefusedWithAReason()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;

			Assert.Contains("nothing", ops.Dock("nothing", "left").Error);
			Assert.Contains("diagonal", ops.Dock(Recent, "diagonal").Error);
			Assert.Contains("nothing", ops.Tab(Recent, "nothing").Error);
			Assert.Contains("nothing", ops.Float("nothing", null).Error);
			Assert.Contains("nothing", ops.AutoHide("nothing", true).Error);
			Assert.Contains("nothing", ops.SlideOut("nothing").Error);
			Assert.Contains("nothing", ops.Close("nothing").Error);
			Assert.Contains("nothing", ops.Show("nothing").Error);
			Assert.Contains("nothing", ops.BringForward("nothing").Error);
			Assert.False(ops.SlideOut(Focus).Ok);   // docked, not auto-hidden

			Assert.True(ops.Close(Tags).Ok);
			Settle(window);
			Assert.False(ops.Dock(Tags, "left").Ok);
			Assert.False(ops.Close(Tags).Ok);
			Assert.False(ops.Tab(Recent, Tags).Ok);   // the target is closed
			window.Close();
		});
	}

	[Fact]
	public void CloseAndShow_ComeBackAtTheLastPlace_AndBringForwardSelectsTheTab()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;

			Assert.True(ops.Close(Tags).Ok);
			Settle(window);
			var closed = Of(area, Tags);
			Assert.False(closed.Shown);
			Assert.Equal("closed", closed.Side);

			Assert.True(ops.Show(Tags).Ok);
			Settle(window);
			Assert.True(Of(area, Tags).Shown);
			Assert.Equal(Of(area, Sessions).Pane, Of(area, Tags).Pane);
			Assert.Equal(2, Of(area, Tags).TabIndex);

			Assert.True(ops.BringForward(Recent).Ok);
			Settle(window);
			Assert.True(Of(area, Recent).Selected);
			Assert.False(Of(area, Tags).Selected);
			window.Close();
		});
	}

	[Fact]
	public void AutoHide_HidesThePane_SlideOutOpensIt_AndOffRestoresIt()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;

			Assert.True(ops.AutoHide(Sessions, true).Ok);
			Settle(window);
			var hidden = Of(area, Recent);
			Assert.True(hidden.AutoHide);
			Assert.Equal("left", hidden.Edge);
			Assert.True(Of(area, Sessions).AutoHide);
			Assert.Equal(1, hidden.TabIndex);

			Assert.True(ops.SlideOut(Recent).Ok);
			Settle(window);
			Assert.True(area.Chrome.SlideOut.IsOpen);

			Assert.True(ops.AutoHide(Sessions, false).Ok);
			Settle(window);
			Assert.False(Of(area, Sessions).AutoHide);
			Assert.Equal(Of(area, Sessions).Pane, Of(area, Recent).Pane);
			Assert.True(ops.AutoHide(Sessions, false).Ok);   // already off: nothing to do
			window.Close();
		});
	}

	[Fact]
	public void Float_GivesTheRectangle_ReportsFloating_AndDockReturnsThePanel()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start(testMode: true);
			var ops = (IDockingTestOperations)area;

			Assert.True(ops.Float(Focus, new DockingFloatRect(120, 80, 520, 380)).Ok);
			Settle(window);
			var floating = Of(area, Focus);
			Assert.True(floating.Floating);
			Assert.Equal("floating", floating.Side);
			Assert.Equal(520, floating.Width, 0);
			Assert.Equal(380, floating.Height, 0);

			Assert.True(ops.Dock(Focus, "right").Ok);
			Settle(window);
			Assert.False(Of(area, Focus).Floating);
			Assert.Equal("right", Of(area, Focus).Side);
			Assert.Empty(area.Manager.FloatingWindows);
			window.Close();
		});
	}

	[Fact]
	public void InTestMode_AFloatingWindow_IsOffScreenNotInTheTaskbarAndNeverActivated()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start(testMode: true);

			Assert.True(((IDockingTestOperations)area).Float(Focus, new DockingFloatRect(100, 100, 480, 360)).Ok);
			Settle(window);

			var frame = Assert.Single(area.Manager.FloatingWindows);
			Assert.False(frame.ShowActivated);
			Assert.False(frame.ShowInTaskbar);
			Assert.True(frame.Left < -20000 && frame.Top < -20000, $"{frame.Left},{frame.Top}");   // -32000 pixels, in device-independent units at the screen's scale
			window.Close();
		});
	}

	[Fact]
	public void TheFloatingPolicy_ConfiguresAWindowBeforeItIsShown()
	{
		StaTestRunner.Run(() =>
		{
			var frame = new Window { Left = 100, Top = 100, ShowActivated = true, ShowInTaskbar = true };

			new DockingTestFloatingPolicy().Apply(frame, null);

			Assert.False(frame.ShowActivated);
			Assert.False(frame.ShowInTaskbar);
			Assert.Equal(WindowStartupLocation.Manual, frame.WindowStartupLocation);
			Assert.Equal(Constants.Limits.OffScreenCoordinate, frame.Left);
			Assert.Equal(Constants.Limits.OffScreenCoordinate, frame.Top);
		});
	}

	[Fact]
	public void TheTabsAndTheFloatingFrame_CarryThePanelTitleAsTheirUiAutomationName()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start(testMode: true);
			var names = new DockingAutomationNames(area.Manager, new DockingVisualTree());

			var tabNames = Descendants<TabItem>(area).Select(AutomationProperties.GetName).Where(n => n.Length > 0).ToList();
			Assert.Contains("Sessions", tabNames);
			Assert.Contains("Recent", tabNames);
			Assert.Contains("Tags", tabNames);
			Assert.Contains("Focus", tabNames);

			Assert.True(((IDockingTestOperations)area).Float(Focus, null).Ok);
			Settle(window);
			names.Update();
			Assert.Equal("Focus", AutomationProperties.GetName(Assert.Single(area.Manager.FloatingWindows)));
			window.Close();
		});
	}

	[Fact]
	public void Reset_AfterFloatingAutoHidingAndClosing_RestoresTheDefaultLayout()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start(testMode: true);
			var ops = (IDockingTestOperations)area;
			var first = ops.State().Select(p => (p.Id, p.Side, p.TabIndex, p.Shown)).ToList();

			ops.Float(Focus, null);
			ops.AutoHide(Sessions, true);
			ops.Close(Tags);
			Settle(window);

			Assert.True(ops.Reset().Ok);
			Settle(window);
			Assert.Equal(first, ops.State().Select(p => (p.Id, p.Side, p.TabIndex, p.Shown)).ToList());
			window.Close();
		});
	}

	[Fact]
	public void APreviewPanel_IsReportedAndMovedByItsContentId_LikeAShellPanel()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window) = Start();
			var ops = (IDockingTestOperations)area;
			var preview = new LayoutAnchorable { ContentId = "text-code", Title = "Text and code", Content = new TextBlock { Text = "p" }, CanClose = true, CanFloat = true, CanAutoHide = true };
			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, preview, null, area.AreaWidth, area.AreaHeight);
			Settle(window);

			var state = Of(area, "text-code");
			Assert.Equal("preview", state.Kind);
			Assert.Equal(Of(area, Focus).Pane, state.Pane);   // the first time: a tab in the pane that holds Focus

			Assert.True(ops.Dock("text-code", "bottom").Ok);
			Settle(window);
			Assert.Equal("bottom", Of(area, "text-code").Side);
			Assert.True(ops.Tab("text-code", Focus).Ok);
			Settle(window);
			Assert.Equal(Of(area, Focus).Pane, Of(area, "text-code").Pane);
			Assert.True(ops.Float("text-code", null).Ok);
			Settle(window);
			Assert.True(Of(area, "text-code").Floating);
			Assert.True(ops.Dock("text-code", "right").Ok);
			Assert.True(ops.AutoHide("text-code", true).Ok);
			Settle(window);
			Assert.True(Of(area, "text-code").AutoHide);
			Assert.True(ops.AutoHide("text-code", false).Ok);
			window.Close();
		});
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => new DockingVisualTree().Descendants<T>(root);
}
