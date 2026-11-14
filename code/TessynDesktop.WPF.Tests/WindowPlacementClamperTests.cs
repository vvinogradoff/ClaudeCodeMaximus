using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests;

/// <summary>Restoring a remembered window onto the monitors present now.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WindowPlacementClamperTests
{
	private readonly WindowPlacementClamper _clamper = new();
	private static readonly MonitorBounds Primary = new(0, 0, 1920, 1040);
	private static readonly MonitorBounds Right = new(1920, 0, 2560, 1400);
	private static readonly MonitorBounds Left = new(-1280, -200, 1280, 1024);

	private static WindowPlacementModel Window(int left, int top, int width, int height, bool maximized = false) =>
		new() { Left = left, Top = top, Width = width, Height = height, Maximized = maximized };

	[Fact]
	public void NothingSaved_ReturnsNull()
	{
		Assert.Null(_clamper.Clamp(null, [Primary], Primary));
		Assert.Null(_clamper.Clamp(Window(0, 0, 0, 0), [Primary], Primary));
	}

	[Fact]
	public void FullyVisibleWindow_IsUnchanged()
	{
		var result = _clamper.Clamp(Window(100, 80, 1200, 800), [Primary], Primary);

		Assert.Equal(100, result!.Left);
		Assert.Equal(80, result.Top);
		Assert.Equal(1200, result.Width);
		Assert.Equal(800, result.Height);
	}

	[Fact]
	public void MaximizedFlag_IsKept()
	{
		Assert.True(_clamper.Clamp(Window(100, 80, 1200, 800, maximized: true), [Primary], Primary)!.Maximized);
	}

	[Fact]
	public void WindowOnASecondMonitorThatStillExists_StaysThere()
	{
		var result = _clamper.Clamp(Window(2100, 100, 1400, 900), [Primary, Right], Primary);

		Assert.Equal(2100, result!.Left);
		Assert.Equal(100, result.Top);
	}

	[Fact]
	public void WindowOnAnUnpluggedMonitor_MovesToTheCenterOfThePrimary()
	{
		var result = _clamper.Clamp(Window(2100, 100, 1400, 900), [Primary], Primary);

		Assert.Equal((1920 - 1400) / 2, result!.Left);
		Assert.Equal((1040 - 900) / 2, result.Top);
		Assert.Equal(1400, result.Width);
		Assert.Equal(900, result.Height);
	}

	[Fact]
	public void WindowOnAMonitorLeftOfThePrimary_IsValidWithNegativeCoordinates()
	{
		var result = _clamper.Clamp(Window(-1200, -100, 1000, 700), [Left, Primary], Primary);

		Assert.Equal(-1200, result!.Left);
		Assert.Equal(-100, result.Top);
	}

	[Fact]
	public void LeftMonitorGone_NegativeCoordinatesLandOnThePrimary()
	{
		var result = _clamper.Clamp(Window(-1200, -100, 1000, 700), [Primary], Primary);

		Assert.InRange(result!.Left, 0, 1920 - result.Width);
		Assert.InRange(result.Top, 0, 1040 - result.Height);
	}

	[Fact]
	public void WindowHangingOffTheRightEdge_IsShiftedFullyIn()
	{
		var result = _clamper.Clamp(Window(1500, 300, 1000, 600), [Primary], Primary);

		Assert.Equal(1920 - 1000, result!.Left);
		Assert.Equal(300, result.Top);
	}

	[Fact]
	public void WindowHangingOffTheTop_IsShiftedDown()
	{
		var result = _clamper.Clamp(Window(200, -150, 1000, 600), [Primary], Primary);

		Assert.Equal(0, result!.Top);
	}

	[Fact]
	public void WindowLargerThanItsMonitor_IsShrunkToFit()
	{
		var result = _clamper.Clamp(Window(0, 0, 4000, 2000), [Primary], Primary);

		Assert.Equal(1920, result!.Width);
		Assert.Equal(1040, result.Height);
		Assert.Equal(0, result.Left);
		Assert.Equal(0, result.Top);
	}

	[Fact]
	public void OnlyASliverVisible_CountsAsOffScreen()
	{
		var result = _clamper.Clamp(Window(1900, 100, 1000, 600), [Primary], Primary);

		Assert.Equal((1920 - 1000) / 2, result!.Left);
	}

	[Fact]
	public void SpanningTwoMonitors_GoesToTheOneWithTheLargerOverlap()
	{
		var result = _clamper.Clamp(Window(1700, 100, 1000, 600), [Primary, Right], Primary);

		Assert.Equal(1920, result!.Left);
	}

	[Fact]
	public void UnknownMonitors_KeepTheSavedPlacement()
	{
		var result = _clamper.Clamp(Window(5000, 5000, 800, 600), [], Primary);

		Assert.Equal(5000, result!.Left);
	}

	[Fact]
	public void ResultNeverLeavesTheMonitor_ForManyPlacements()
	{
		for (var left = -3000; left <= 5000; left += 700)
		{
			for (var top = -2000; top <= 3000; top += 450)
			{
				var result = _clamper.Clamp(Window(left, top, 1300, 800), [Primary, Right], Primary)!;
				var inside = new[] { Primary, Right }.Any(m => result.Left >= m.Left && result.Top >= m.Top && result.Left + result.Width <= m.Right && result.Top + result.Height <= m.Bottom);
				Assert.True(inside, $"({left},{top}) -> ({result.Left},{result.Top},{result.Width}x{result.Height})");
			}
		}
	}
}
