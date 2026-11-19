using AvalonDock.Layout;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.19, FR.29.21 (stage 1): the mapping between a remembered <see cref="PreviewDockLayoutModel"/> and AvalonDock's layout model, both ways, on the model alone.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewDockLayoutMappingTests
{
	private readonly PreviewDockLayoutApplier _applier = new();
	private readonly PreviewDockLayoutReader _reader = new();

	private static LayoutAnchorable Panel(string id) => new() { ContentId = id, Title = id, Content = id, CanFloat = true, CanAutoHide = true };

	[Fact]
	public void FirstTime_DocksToTheRight_At40PercentOfTheAreasWidth()
	{
		var surface = new TestDockingSurface(width: 1000);
		var panel = Panel("images");

		_applier.Apply(surface.Root, surface.DocumentPane, panel, null, surface.AreaWidth, surface.AreaHeight);

		var pane = Assert.IsType<LayoutAnchorablePane>(panel.Parent);
		Assert.Equal(400, pane.DockWidth.Value);
		var read = _reader.Read(panel, surface.DocumentPane);
		Assert.Equal(PreviewDockSide.Right, read.Side);
		Assert.Equal(400, read.Size);
		Assert.False(read.AutoHide);
		Assert.False(read.Floating);
		Assert.True(read.Shown);
		Assert.Empty(read.StackedWith);
	}

	[Fact]
	public void FirstTime_WithoutALaidOutArea_UsesTheFallbackSize()
	{
		var surface = new TestDockingSurface(width: 0);
		var panel = Panel("images");

		_applier.Apply(surface.Root, surface.DocumentPane, panel, null, 0, 0);

		Assert.Equal(Constants.PreviewDock.FallbackPaneSize, _reader.Read(panel, surface.DocumentPane).Size);
	}

	[Theory]
	[InlineData(PreviewDockSide.Right)]
	[InlineData(PreviewDockSide.Left)]
	[InlineData(PreviewDockSide.Top)]
	[InlineData(PreviewDockSide.Bottom)]
	public void ARememberedSideAndSize_AreRestored_AndReadBack(PreviewDockSide side)
	{
		var surface = new TestDockingSurface();
		var panel = Panel("images");

		_applier.Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = side, Size = 333 }, 1000, 600);

		var read = _reader.Read(panel, surface.DocumentPane);
		Assert.Equal(side, read.Side);
		Assert.Equal(333, read.Size);
	}

	[Fact]
	public void PanelsOnAllFourSides_KeepTheirSides_InAnyOrder()
	{
		var surface = new TestDockingSurface();
		var panels = new Dictionary<PreviewDockSide, LayoutAnchorable>();
		foreach (var side in new[] { PreviewDockSide.Bottom, PreviewDockSide.Left, PreviewDockSide.Top, PreviewDockSide.Right })
		{
			var panel = Panel(side.ToString());
			panels[side] = panel;
			_applier.Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = side, Size = 200 }, 1000, 600);
		}

		foreach (var (side, panel) in panels)
			Assert.Equal(side, _reader.Read(panel, surface.DocumentPane).Side);
	}

	[Fact]
	public void ARememberedNeighbourThatIsOpen_IsStackedWithAsATab_AtItsIndex()
	{
		var surface = new TestDockingSurface();
		var first = Panel("images");
		var second = Panel("csv");
		_applier.Apply(surface.Root, surface.DocumentPane, first, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 300 }, 1000, 600);

		_applier.Apply(surface.Root, surface.DocumentPane, second, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, StackedWith = ["images"], TabIndex = 1 }, 1000, 600);

		Assert.Same(first.Parent, second.Parent);
		var read = _reader.Read(second, surface.DocumentPane);
		Assert.Equal(["images"], read.StackedWith);
		Assert.Equal(1, read.TabIndex);
		Assert.Equal(PreviewDockSide.Left, read.Side);
		Assert.True(second.IsSelected);
	}

	[Fact]
	public void ARememberedNeighbourThatIsNotOpen_IsIgnored_ThePanelDocksToTheSameSide()
	{
		var surface = new TestDockingSurface();
		var panel = Panel("csv");

		_applier.Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Bottom, StackedWith = ["images"], TabIndex = 1, Size = 250 }, 1000, 600);

		var read = _reader.Read(panel, surface.DocumentPane);
		Assert.Equal(PreviewDockSide.Bottom, read.Side);
		Assert.Empty(read.StackedWith);
		Assert.Equal(250, read.Size);
	}

	[Fact]
	public void AutoHide_IsRestored_AtItsEdge_AndReadBack()
	{
		var surface = new TestDockingSurface();
		var panel = Panel("images");

		_applier.Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, AutoHide = true, Size = 350 }, 1000, 600);

		Assert.True(panel.IsAutoHidden);
		var read = _reader.Read(panel, surface.DocumentPane);
		Assert.True(read.AutoHide);
		Assert.Equal(PreviewDockSide.Left, read.Side);
		Assert.Equal(350, read.Size);
	}

	[Fact]
	public void TheSessionDocument_IsNeverMoved_ByDockingPanels()
	{
		var surface = new TestDockingSurface();
		foreach (var side in new[] { PreviewDockSide.Left, PreviewDockSide.Bottom })
			_applier.Apply(surface.Root, surface.DocumentPane, Panel(side.ToString()), new PreviewDockLayoutModel { Side = side, Size = 200 }, 1000, 600);

		Assert.Single(surface.DocumentPane.Children);
		Assert.Equal("session", surface.DocumentPane.Children.Single().ContentId);
		Assert.Same(surface.Root, surface.DocumentPane.Root);
	}

	[Fact]
	public void AFloatingPanel_ReadsAsFloating_WithItsRectangle_AndTheLastDockedSide()
	{
		StaTestRunner.Run(() =>
		{
			var manager = new AvalonDock.DockingManager();
			var surface = new TestDockingSurface(manager: manager);
			var panel = Panel("images");
			_applier.Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 300 }, 1000, 600);
			var docked = _reader.Read(panel, surface.DocumentPane);
			panel.FloatingLeft = 50;
			panel.FloatingTop = 60;
			panel.FloatingWidth = 500;
			panel.FloatingHeight = 400;

			panel.Float();

			Assert.True(panel.IsFloating);
			var floating = _reader.Read(panel, surface.DocumentPane, docked);
			Assert.True(floating.Floating);
			Assert.Equal(PreviewDockSide.Left, floating.Side);
			Assert.Equal(300, floating.Size);
			Assert.Equal(500, floating.FloatingPlacement!.Width);
		});
	}

	[Fact]
	public void APreviewTabbedIntoTheDocumentPane_KeepsItsLastDockedSide_AndNeverThrows()
	{
		var surface = new TestDockingSurface();
		var panel = Panel("images");
		_applier.Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 300 }, 1000, 600);
		var docked = _reader.Read(panel, surface.DocumentPane);

		((ILayoutContainer)panel.Parent!).RemoveChild(panel);
		surface.DocumentPane.InsertChildAt(1, panel);   // dropped onto the centre of the session pane

		var tabbed = _reader.Read(panel, surface.DocumentPane, docked);
		Assert.Equal(PreviewDockSide.Left, tabbed.Side);
		Assert.False(tabbed.Floating);
		Assert.Equal(PreviewDockSide.Right, _reader.Read(panel, surface.DocumentPane).Side);   // nothing known before: the default
		Assert.Equal(2, surface.DocumentPane.ChildrenCount);
	}
}
