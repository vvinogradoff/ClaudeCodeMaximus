using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>Floating docked panels across monitors and scales (FR.29.16, FR.29.21, FR.6.18): the conversion and clamp rules, placements saved without a scale, and a move Windows forces never being remembered.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FloatingPanelPlacementTests
{
	private const string Left = @"\\.\DISPLAY1";
	private const string Right = @"\\.\DISPLAY2";

	private sealed class WatcherFake : IMonitorWatcher
	{
		public IReadOnlyList<MonitorInfo> Monitors_ { get; set; } = [];

		public IReadOnlyList<MonitorInfo> Monitors => Monitors_;

		public bool IsSettling { get; set; }

		public string? OnWindow { get; set; }

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public string? MonitorOf(IntPtr hwnd) => OnWindow;
	}

	private sealed class Api : IWindowPlacementApi
	{
		public WindowPlacementModel Rectangle { get; set; } = new() { Left = 100, Top = 100, Width = 600, Height = 400 };

		public int Dpi { get; set; } = 96;

		public WindowPlacementModel? Capture(IntPtr hwnd) => Rectangle;

		public bool IsRestored(IntPtr hwnd) => true;

		public WindowPlacementModel? GetActualRectangle(IntPtr hwnd) => Rectangle.Clone();

		public int GetDpi(IntPtr hwnd) => Dpi;

		public void Apply(IntPtr hwnd, WindowPlacementModel model)
		{
		}
	}

	private static MonitorInfo Display1(int dpi = 96) => new(Left, new MonitorBounds(0, 0, 1920, 1040), true, dpi);

	private static MonitorInfo Display2(int dpi = 96) => new(Right, new MonitorBounds(1920, 0, 2560, 1400), false, dpi);

	private static (FloatingPanelPlacement Placement, WatcherFake Watcher, Api Api) New(params MonitorInfo[] monitors)
	{
		var watcher = new WatcherFake { Monitors_ = monitors, OnWindow = monitors.FirstOrDefault()?.DeviceName };
		var api = new Api();
		return (new FloatingPanelPlacement(api, watcher, new PreviewPlacementPlanner()), watcher, api);
	}

	// ---- what is remembered ----

	[Fact]
	public void Capture_KeepsTheActualRectangleInPixels_WithTheMonitorAndItsScale()
	{
		var (placement, _, api) = New(Display1(144), Display2(96));
		api.Dpi = 144;
		api.Rectangle = new WindowPlacementModel { Left = 300, Top = 200, Width = 900, Height = 700 };

		var saved = placement.Capture(new IntPtr(1))!;

		Assert.Equal((300, 200, 900, 700), (saved.Left, saved.Top, saved.Width, saved.Height));
		Assert.Equal(Left, saved.MonitorDeviceName);
		Assert.Equal(144, saved.Dpi);
		Assert.Equal(0, saved.MonitorLeft);
	}

	[Fact]
	public void Capture_NoWindow_IsNull() => Assert.Null(New(Display1()).Placement.Capture(IntPtr.Zero));

	// ---- restore: scales and monitors ----

	[Fact]
	public void Plan_SameMonitorSameScale_GivesTheSavedRectangle()
	{
		var (placement, _, _) = New(Display1(), Display2());
		var saved = new PreviewPlacementModel { Left = 2100, Top = 100, Width = 800, Height = 600, MonitorDeviceName = Right, MonitorLeft = 1920, MonitorTop = 0, Dpi = 96 };

		var plan = placement.Plan(saved);

		Assert.Equal((2100, 100, 800, 600), (plan.Left, plan.Top, plan.Width, plan.Height));
	}

	[Fact]
	public void Plan_TheMonitorNowHasAnotherScale_SizesFollowTheRatio_InTheLibrarysUnits()
	{
		var (placement, _, _) = New(Display1(), Display2(192));   // saved at 100 %, the monitor is 200 % now
		var saved = new PreviewPlacementModel { Left = 2020, Top = 100, Width = 800, Height = 600, MonitorDeviceName = Right, MonitorLeft = 1920, MonitorTop = 0, Dpi = 96 };

		var plan = placement.Plan(saved);

		// pixels: 800x600 at 96 dpi = 1600x1200 at 192 dpi, then to device-independent units at 192 dpi: 800x600 again
		Assert.Equal((800, 600), (plan.Width, plan.Height));
		Assert.Equal(1920 + 100 * 2, 2120);   // the offset in the work area doubled in pixels
		Assert.Equal(Math.Round(2120 * 96.0 / 192), plan.Left);
	}

	[Fact]
	public void Plan_PixelsAtHighScale_AreConvertedToDeviceIndependentUnits()
	{
		var (placement, _, _) = New(Display1(144), Display2());
		var saved = new PreviewPlacementModel { Left = 300, Top = 150, Width = 900, Height = 600, MonitorDeviceName = Left, MonitorLeft = 0, MonitorTop = 0, Dpi = 144 };

		var plan = placement.Plan(saved);

		Assert.Equal((200, 100, 600, 400), (plan.Left, plan.Top, plan.Width, plan.Height));
		var back = placement.ToLibraryUnits(saved);
		Assert.Equal((200, 100, 600, 400), (back.Left, back.Top, back.Width, back.Height));
	}

	[Fact]
	public void Plan_TheMonitorIsGone_ClampsToAMonitorPresent()
	{
		var (placement, _, _) = New(Display1());
		var saved = new PreviewPlacementModel { Left = 5000, Top = 100, Width = 800, Height = 600, MonitorDeviceName = Right, MonitorLeft = 4800, MonitorTop = 0, Dpi = 96 };

		var plan = placement.Plan(saved);

		Assert.InRange(plan.Left, 0, 1920 - plan.Width);
		Assert.InRange(plan.Top, 0, 1040 - plan.Height);
	}

	[Fact]
	public void Plan_TheMonitorWasRenamedButSitsWhereItDid_IsTheSameMonitor()
	{
		var renamed = new MonitorInfo(@"\\.\DISPLAY9", new MonitorBounds(1920, 0, 2560, 1400), false, 96);
		var (placement, _, _) = New(Display1(), renamed);
		var saved = new PreviewPlacementModel { Left = 2100, Top = 100, Width = 800, Height = 600, MonitorDeviceName = Right, MonitorLeft = 1920, MonitorTop = 0, Dpi = 96 };

		var plan = placement.Plan(saved);

		Assert.Equal((2100, 100), (plan.Left, plan.Top));
	}

	[Fact]
	public void Plan_TheMonitorMovedInTheLayout_TheWindowMovesWithIt()
	{
		var moved = new MonitorInfo(Right, new MonitorBounds(0, -1400, 2560, 1400), false, 96);   // now above the first one
		var (placement, _, _) = New(Display1(), moved);
		var saved = new PreviewPlacementModel { Left = 2100, Top = 100, Width = 800, Height = 600, MonitorDeviceName = Right, MonitorLeft = 1920, MonitorTop = 0, Dpi = 96 };

		var plan = placement.Plan(saved);

		Assert.Equal((180, -1300), (plan.Left, plan.Top));
	}

	[Fact]
	public void Plan_ASavedPlaceWithoutAScale_IsOnlyClamped_NotConverted()
	{
		var (placement, _, _) = New(Display1(192), Display2(192));
		var old = new PreviewPlacementModel { Left = 100, Top = 80, Width = 700, Height = 500 };   // an older layout: no monitor, no scale

		var plan = placement.Plan(old);

		Assert.Equal((100, 80, 700, 500), (plan.Left, plan.Top, plan.Width, plan.Height));
		var far = new PreviewPlacementModel { Left = -40000, Top = -40000, Width = 700, Height = 500 };
		var clamped = placement.Plan(far);
		Assert.InRange(clamped.Left, 0, 1920);
		Assert.InRange(clamped.Top, 0, 1040);
		Assert.Equal((700, 500), (clamped.Width, clamped.Height));
	}

	[Fact]
	public void ToLibraryUnits_WithoutAScale_IsTheSavedRectangle()
	{
		var (placement, _, _) = New(Display1());

		var units = placement.ToLibraryUnits(new PreviewPlacementModel { Left = 7, Top = 8, Width = 9, Height = 10 });

		Assert.Equal((7, 8, 9, 10), (units.Left, units.Top, units.Width, units.Height));
	}

	[Fact]
	public void Plan_NothingKnownAboutTheMonitors_StillGivesARectangle()
	{
		var (placement, _, _) = New();

		var plan = placement.Plan(new PreviewPlacementModel { Left = 10, Top = 10, Width = 500, Height = 400, Dpi = 96, MonitorDeviceName = Left });

		Assert.True(plan.Width > 0 && plan.Height > 0);
	}

	// ---- a move Windows forces is never remembered ----

	private static PreviewPlacementModel At(int left, int top = 100, string monitor = Left) =>
		new() { Left = left, Top = top, Width = 600, Height = 400, MonitorDeviceName = monitor, Dpi = 96 };

	[Fact]
	public void Guard_AMoveSeenWhileSettling_IsNotRemembered_AndNeitherIsTheSamePlaceReadAgainAfterwards()
	{
		var guard = new FloatingPlacementGuard();
		Assert.Equal(2100, guard.Filter(At(2100, monitor: Right), settling: false)!.Left);   // the user put it there

		var duringChange = guard.Filter(At(200), settling: true);   // Windows pulled it onto the remaining monitor
		var afterwards = guard.Filter(At(200), settling: false);   // read again once the display settled

		Assert.Equal(2100, duringChange!.Left);
		Assert.Equal(Right, duringChange.MonitorDeviceName);
		Assert.Equal(2100, afterwards!.Left);
	}

	[Fact]
	public void Guard_TheUserMovesItAfterTheForcedMove_ThatIsRemembered()
	{
		var guard = new FloatingPlacementGuard();
		guard.Filter(At(2100, monitor: Right), settling: false);
		guard.Filter(At(200), settling: true);

		var moved = guard.Filter(At(260), settling: false);

		Assert.Equal(260, moved!.Left);
		Assert.Equal(260, guard.Filter(At(260), settling: false)!.Left);
	}

	[Fact]
	public void Guard_NothingSeenBeforeAndSettling_TheReadIsTheOnlyPlaceThereIs()
	{
		var guard = new FloatingPlacementGuard();

		Assert.Equal(200, guard.Filter(At(200), settling: true)!.Left);
	}

	[Fact]
	public void Guard_AnUnreadableWindow_KeepsTheLastGoodPlace()
	{
		var guard = new FloatingPlacementGuard();
		guard.Filter(At(500), settling: false);

		Assert.Equal(500, guard.Filter(null, settling: false)!.Left);
		Assert.Null(new FloatingPlacementGuard().Filter(null, settling: false));
	}

	// ---- the planner's panel mode ----

	[Fact]
	public void Planner_ForAPanel_AnUnknownMonitor_GoesNearThePrimary_NotToTheLargestOther()
	{
		var planner = new PreviewPlacementPlanner();
		var monitors = new[] { Display1(), Display2() };   // the second one is the larger
		var saved = new PreviewPlacementModel { Left = 100, Top = 100, Width = 700, Height = 500, MonitorDeviceName = @"\\.\GONE", MonitorLeft = 9000, MonitorTop = 0, Dpi = 96 };

		var forWindow = planner.Plan(saved, monitors, mainWindowMonitor: null);
		var forPanel = planner.Plan(saved, monitors, mainWindowMonitor: null, nearMainWindow: true);

		Assert.True(forWindow.Left >= 1920);   // a window goes to the largest monitor that does not hold the main window
		Assert.True(forPanel.Left < 1920);
	}
}
