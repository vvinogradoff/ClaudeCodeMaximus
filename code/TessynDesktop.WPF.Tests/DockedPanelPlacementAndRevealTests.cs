using AvalonDock;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>A docked panel's handle: a new item brings the panel forward (FR.29.17, decision 3), a router close keeps the layout (FR.29.23), a floating panel is remembered with its monitor and scale, and a forced move is not (FR.29.16, FR.29.21).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockedPanelPlacementAndRevealTests
{
	private static readonly PreviewGroupModel Group = new() { Id = "text-code", Name = "Text and code", Patterns = "md txt" };

	private sealed class ScriptedPlacement : IFloatingPanelPlacement
	{
		public bool IsSettling { get; set; }

		public PreviewPlacementModel? Now { get; set; }

		public PreviewPlacementModel? Capture(IntPtr hwnd) => Now?.Clone();

		public WindowPlacementModel Plan(PreviewPlacementModel saved) => new() { Left = saved.Left / 2, Top = saved.Top / 2, Width = saved.Width / 2, Height = saved.Height / 2 };

		public WindowPlacementModel ToLibraryUnits(PreviewPlacementModel pixels) => pixels.ToWindowPlacement();
	}

	private static (DockedPreviewPanelHandle Handle, FakePreviewSettingsService Settings, FakePreviewContentFactory Content, LayoutAnchorable Panel) NewHandle(
		IDockingSurface surface, PreviewDockLayoutModel? remembered = null, ManualTimeProvider? time = null, IFloatingPanelPlacement? floating = null, string id = "text-code")
	{
		var group = id == Group.Id ? Group : new PreviewGroupModel { Id = id, Name = id, Patterns = "png" };
		var settings = new FakePreviewSettingsService(group);
		var content = new FakePreviewContentFactory();
		var vm = new PreviewWindowViewModel(group.Id, group.Name, false, settings, content, new ImmediateUiDispatcher());
		var panelContent = new PreviewPanelContent();
		panelContent.Attach(vm, NullLogger.Instance, true);
		var panel = new LayoutAnchorable { ContentId = group.Id, Content = panelContent };
		return (new DockedPreviewPanelHandle(vm, panelContent, panel, surface, settings, remembered, new ImmediateUiDispatcher(), NullLogger.Instance, time, floating: floating), settings, content, panel);
	}

	private static PreviewItemModel Md(string name = "note.md") => new() { Kind = PreviewItemKind.File, FileName = name };

	private static PreviewPlacementModel At(int left, string monitor = @"\\.\DISPLAY1", int dpi = 144) =>
		new() { Left = left, Top = 100, Width = 800, Height = 600, MonitorDeviceName = monitor, Dpi = dpi };

	// ---- decision 3: a new item brings the panel forward ----

	[Fact]
	public void ANewItem_ForAnAutoHiddenPanel_AsksTheDockingAreaToSlideItOut()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(manager: new DockingManager());
			var (handle, _, content, panel) = NewHandle(surface, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300, AutoHide = true });
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			Assert.True(panel.IsAutoHidden);
			Assert.Empty(surface.Revealed);   // the first item opens it at its remembered place

			handle.Show(Md("b.md"), new PreviewTarget("h", "b.md"), new FakeTessynClient());

			Assert.Same(panel, Assert.Single(surface.Revealed));
			handle.Close();
		});
	}

	[Fact]
	public void ANewItem_ForAPanelTabbedBehindAnother_SelectsItsTab()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(manager: new DockingManager());
			var (first, _, firstContent, firstPanel) = NewHandle(surface, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300 });
			var (second, _, secondContent, secondPanel) = NewHandle(surface, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300, StackedWith = ["text-code"], TabIndex = 1 }, id: "images");
			using var _1 = firstContent;
			using var _2 = secondContent;
			first.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			second.Show(Md("a.png"), new PreviewTarget("h", "a.png"), new FakeTessynClient());
			Assert.Same(firstPanel.Parent, secondPanel.Parent);
			firstPanel.IsSelected = true;   // the user looked at the first tab
			Assert.False(secondPanel.IsSelected);

			second.Show(Md("b.png"), new PreviewTarget("h", "b.png"), new FakeTessynClient());

			Assert.True(secondPanel.IsSelected);
			first.Close();
			second.Close();
		});
	}

	[Fact]
	public void ANewItem_ForAnOpenPanel_BringsItForward_AndLeavesItShown()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(manager: new DockingManager());
			var (handle, _, content, panel) = NewHandle(surface);
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());

			handle.Show(Md("b.md"), new PreviewTarget("h", "b.md"), new FakeTessynClient());

			Assert.False(panel.IsHidden);
			Assert.Single(surface.Revealed);
			handle.Close();
		});
	}

	// ---- FR.29.23: the layout is kept when the router closes a panel ----

	[Fact]
	public void ClosedByTheRouter_ALayoutChangeStillWaiting_IsSavedFirst_AndTheGroupStaysShown()
	{
		StaTestRunner.Run(() =>
		{
			var time = new ManualTimeProvider();
			var surface = new TestDockingSurface(manager: new DockingManager());
			var (handle, settings, content, panel) = NewHandle(surface, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300 }, time);
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			((LayoutAnchorablePane)panel.Parent!).DockWidth = new System.Windows.GridLength(420);
			surface.RaiseLayoutChanged();   // the user dragged the splitter; the 500 ms have not passed
			Assert.Empty(settings.DockLayouts);

			handle.Close();   // the router closes it (the mode turned on)

			Assert.Equal(420, settings.DockLayouts["text-code"].Size);
			Assert.True(settings.DockLayouts["text-code"].Shown);
		});
	}

	[Fact]
	public void ClosedByTheRouter_AFloatingPanelKeepsItsFloatingRectangle()
	{
		StaTestRunner.Run(() =>
		{
			var time = new ManualTimeProvider();
			var surface = new TestDockingSurface(manager: new DockingManager()) { FloatingHandle = new IntPtr(7) };
			var placement = new ScriptedPlacement { Now = At(300, dpi: 96) };
			var (handle, settings, content, panel) = NewHandle(surface, time: time, floating: placement);
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			panel.Float();
			surface.RaiseLayoutChanged();

			handle.Close();

			var saved = settings.DockLayouts["text-code"];
			Assert.True(saved.Floating);
			Assert.Equal(300, saved.FloatingPlacement!.Left);
		});
	}

	// ---- FR.29.16, FR.29.21: floating rectangles ----

	[Fact]
	public void AFloatingPanel_IsRememberedWithItsMonitorAndScale()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(manager: new DockingManager()) { FloatingHandle = new IntPtr(7) };
			var placement = new ScriptedPlacement { Now = At(300) };
			var (handle, _, content, panel) = NewHandle(surface, floating: placement);
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			panel.Float();

			var layout = handle.CurrentLayout!;

			Assert.True(layout.Floating);
			Assert.Equal(@"\\.\DISPLAY1", layout.FloatingPlacement!.MonitorDeviceName);
			Assert.Equal(144, layout.FloatingPlacement.Dpi);
			Assert.Equal((300, 100, 800, 600), (layout.FloatingPlacement.Left, layout.FloatingPlacement.Top, layout.FloatingPlacement.Width, layout.FloatingPlacement.Height));
			handle.Close();
		});
	}

	[Fact]
	public void AFloatingPanel_WithoutAWindowHandleOrAPlacementService_KeepsTheLibrarysRectangle_NoScale()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(manager: new DockingManager());   // FloatingHandle zero: no window yet
			var (handle, _, content, panel) = NewHandle(surface, floating: new ScriptedPlacement { Now = At(300) });
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			panel.FloatingLeft = 40;
			panel.FloatingTop = 50;
			panel.FloatingWidth = 500;
			panel.FloatingHeight = 400;
			panel.Float();

			var layout = handle.CurrentLayout!;

			Assert.Equal(40, layout.FloatingPlacement!.Left);
			Assert.Equal(0, layout.FloatingPlacement.Dpi);
			handle.Close();
		});
	}

	[Fact]
	public void ARememberedFloatingPlace_IsRestoredThroughThePlacementRules()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(manager: new DockingManager());
			var layout = new PreviewDockLayoutModel { Floating = true, FloatingPlacement = At(600) };   // 800x600 at x 600: the scripted rules halve it
			var (handle, _, content, panel) = NewHandle(surface, layout, floating: new ScriptedPlacement());
			using var _ = content;

			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());

			Assert.True(panel.IsFloating);
			Assert.Equal((300, 50, 400, 300), (panel.FloatingLeft, panel.FloatingTop, panel.FloatingWidth, panel.FloatingHeight));
			handle.Close();
		});
	}

	[Fact]
	public void AMoveWindowsForces_IsNotRemembered_NotEvenWhenReadAgainAfterTheDisplaySettled()
	{
		StaTestRunner.Run(() =>
		{
			var time = new ManualTimeProvider();
			var surface = new TestDockingSurface(manager: new DockingManager()) { FloatingHandle = new IntPtr(7) };
			var placement = new ScriptedPlacement { Now = At(2100, @"\\.\DISPLAY2") };
			var (handle, settings, content, panel) = NewHandle(surface, time: time, floating: placement);
			using var _ = content;
			handle.Show(Md("a.md"), new PreviewTarget("h", "a.md"), new FakeTessynClient());
			panel.Float();
			surface.RaiseLayoutChanged();
			time.Advance(TimeSpan.FromSeconds(1));
			Assert.Equal(2100, settings.DockLayouts["text-code"].FloatingPlacement!.Left);   // the user's place was saved

			placement.IsSettling = true;   // the second monitor was unplugged: Windows pulls the window onto the first
			placement.Now = At(200, @"\\.\DISPLAY1");
			surface.RaiseLayoutChanged();
			time.Advance(TimeSpan.FromSeconds(1));
			placement.IsSettling = false;
			surface.RaiseLayoutChanged();   // a later layout pass reads the window where Windows put it
			time.Advance(TimeSpan.FromSeconds(1));

			Assert.Equal(2100, settings.DockLayouts["text-code"].FloatingPlacement!.Left);
			Assert.Equal(@"\\.\DISPLAY2", settings.DockLayouts["text-code"].FloatingPlacement!.MonitorDeviceName);
			Assert.Equal(2100, handle.CurrentLayout!.FloatingPlacement!.Left);

			placement.Now = At(260, @"\\.\DISPLAY1");   // now the user moves it
			surface.RaiseLayoutChanged();
			time.Advance(TimeSpan.FromSeconds(1));

			Assert.Equal(260, settings.DockLayouts["text-code"].FloatingPlacement!.Left);
			handle.Close();
		});
	}
}
