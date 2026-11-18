using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests;

/// <summary>Where preview group windows open and what is remembered (FR.29.5, FR.29.6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewPlacementPlannerTests
{
	private const string D1 = @"\\.\DISPLAY1";
	private const string D2 = @"\\.\DISPLAY2";
	private const string D3 = @"\\.\DISPLAY3";

	private readonly PreviewPlacementPlanner _planner = new();

	private static MonitorInfo Mon(string name, int left, int top, int width, int height, bool primary = false) => new(name, new MonitorBounds(left, top, width, height), primary);

	private static readonly MonitorInfo Primary = Mon(D1, 0, 0, 1920, 1040, true);
	private static readonly MonitorInfo Second = Mon(D2, 1920, 0, 2560, 1400);
	private static readonly MonitorInfo Third = Mon(D3, -1280, 0, 1280, 1024);

	private static PreviewPlacementModel Saved(MonitorInfo? monitor, int left, int top, int width, int height, bool maximized = false) => new()
	{
		Left = left, Top = top, Width = width, Height = height, Maximized = maximized,
		MonitorDeviceName = monitor?.DeviceName, MonitorLeft = monitor?.WorkArea.Left ?? 0, MonitorTop = monitor?.WorkArea.Top ?? 0,
	};

	[Fact]
	public void First_TwoMonitors_MainOnPrimary_OpensCentredOnTheOtherOne()
	{
		var placement = _planner.Plan(null, [Primary, Second], D1);

		Assert.Equal(new WindowPlacementModel { Left = 1920 + 512, Top = 280, Width = 1536, Height = 840 }.Left, placement.Left);
		Assert.Equal(1536, placement.Width);
		Assert.Equal(840, placement.Height);
		Assert.Equal(280, placement.Top);
		Assert.False(placement.Maximized);
	}

	[Fact]
	public void First_TwoMonitors_MainOnTheLargeOne_OpensOnTheOtherOne()
	{
		var placement = _planner.Plan(null, [Primary, Second], D2);

		Assert.Equal(1152, placement.Width);
		Assert.Equal(384, placement.Left);
		Assert.Equal(208, placement.Top);
	}

	[Fact]
	public void First_ThreeMonitors_PicksTheLargestThatIsNotTheMainWindows()
	{
		Assert.Equal(1920 + 512, _planner.Plan(null, [Primary, Second, Third], D1).Left);
		Assert.Equal(1152, _planner.Plan(null, [Primary, Second, Third], D2).Width);
	}

	[Fact]
	public void First_TieGoesToTheFirstListedMonitor()
	{
		var a = Mon(D1, 0, 0, 1000, 800, true);
		var b = Mon(D2, 1000, 0, 1000, 800);
		var c = Mon(D3, 2000, 0, 1000, 800);

		Assert.Equal(1000 + 200, _planner.Plan(null, [a, b, c], D1).Left);
	}

	[Fact]
	public void First_OneMonitor_UsesTheMainWindowsMonitor()
	{
		var placement = _planner.Plan(null, [Primary], D1);

		Assert.Equal(1152, placement.Width);
		Assert.Equal(384, placement.Left);
	}

	[Fact]
	public void First_NeverBelowTheMinimum()
	{
		var tiny = Mon(D1, 0, 0, 500, 300, true);

		var placement = _planner.Plan(null, [tiny], null);

		Assert.Equal(Constants.Previews.MinWindowWidth, placement.Width);
		Assert.Equal(Constants.Previews.MinWindowHeight, placement.Height);
	}

	[Fact]
	public void Remembered_OnAPresentMonitor_IsKept()
	{
		var placement = _planner.Plan(Saved(Second, 2500, 100, 800, 600), [Primary, Second], D1);

		Assert.Equal((2500, 100, 800, 600), (placement.Left, placement.Top, placement.Width, placement.Height));
	}

	[Fact]
	public void Remembered_MonitorMovedInTheLayout_ShiftsByTheOriginDifference()
	{
		var movedSecond = Mon(D2, -2560, 100, 2560, 1400);

		var placement = _planner.Plan(Saved(Second, 2500, 100, 800, 600), [Primary, movedSecond], D1);

		Assert.Equal((2500 - 1920 - 2560, 200), (placement.Left, placement.Top));
	}

	[Fact]
	public void Remembered_MonitorGone_IsCentredOnTheTargetWithTheSizeClamped()
	{
		var placement = _planner.Plan(Saved(Third, -1000, 50, 3000, 600), [Primary, Second], D2);

		Assert.Equal(1920, placement.Width);
		Assert.Equal(600, placement.Height);
		Assert.Equal(0, placement.Left);
		Assert.Equal(220, placement.Top);
	}

	[Fact]
	public void Remembered_WindowLargerThanItsMonitor_Shrinks()
	{
		var placement = _planner.Plan(Saved(Primary, 100, 100, 5000, 4000), [Primary, Third], D2);

		Assert.Equal(1920, placement.Width);
		Assert.Equal(1040, placement.Height);
		Assert.Equal(0, placement.Left);
		Assert.Equal(0, placement.Top);
	}

	[Fact]
	public void Remembered_PartlyOffScreen_IsPulledIn()
	{
		var placement = _planner.Plan(Saved(Primary, 1700, 900, 800, 600), [Primary], D1);

		Assert.Equal((1120, 440), (placement.Left, placement.Top));
	}

	[Fact]
	public void Remembered_MaximizedCarriesOver_InBothPaths()
	{
		Assert.True(_planner.Plan(Saved(Primary, 100, 100, 800, 600, maximized: true), [Primary], D1).Maximized);
		Assert.True(_planner.Plan(Saved(Third, 0, 0, 800, 600, maximized: true), [Primary, Second], D1).Maximized);
	}

	[Fact]
	public void Remembered_SmallerThanThePreviewMinimum_GrowsToIt_ButNotToTheMainWindowsMinimum()
	{
		var placement = _planner.Plan(Saved(Primary, 100, 100, 100, 100), [Primary], D1);

		Assert.Equal(Constants.Previews.MinWindowWidth, placement.Width);
		Assert.Equal(Constants.Previews.MinWindowHeight, placement.Height);
		Assert.True(placement.Width < Constants.Limits.MinWindowWidth);
	}

	[Fact]
	public void Remembered_WithoutAMonitorName_IsClampedAgainstAllMonitors()
	{
		var placement = _planner.Plan(Saved(null, 2500, 100, 800, 600), [Primary, Second], D1);

		Assert.Equal((2500, 100), (placement.Left, placement.Top));
	}

	[Fact]
	public void Remember_StoresTheRectangleAndTheMonitor()
	{
		var remembered = _planner.Remember(new WindowPlacementModel { Left = 1, Top = 2, Width = 3, Height = 4, Maximized = true }, Second);

		Assert.Equal((1, 2, 3, 4, true), (remembered.Left, remembered.Top, remembered.Width, remembered.Height, remembered.Maximized));
		Assert.Equal((D2, 1920, 0), (remembered.MonitorDeviceName, remembered.MonitorLeft, remembered.MonitorTop));
		Assert.Null(_planner.Remember(new WindowPlacementModel(), null).MonitorDeviceName);
	}

	[Fact]
	public void ClamperOverload_UsesTheGivenMinimum_AndTheOldSignatureKeepsTheMainWindowsOne()
	{
		var clamper = new WindowPlacementClamper();
		var screen = new MonitorBounds(0, 0, 1920, 1080);
		var small = new WindowPlacementModel { Left = 0, Top = 0, Width = 100, Height = 100 };

		Assert.Equal(360, clamper.Clamp(small, [screen], screen, 360, 240)!.Width);
		Assert.Equal(Constants.Limits.MinWindowWidth, clamper.Clamp(small, [screen], screen)!.Width);
	}
}
