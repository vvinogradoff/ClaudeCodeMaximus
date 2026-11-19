using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.16: a window Windows arranged is remembered where it is; sizes keep their pixels across monitor scales; the restore applies the remembered rectangle again.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewPlacementSnapDpiTests
{
	private static readonly IntPtr Hwnd = new(1);
	private const string D1 = @"\\.\DISPLAY1";
	private const string D2 = @"\\.\DISPLAY2";

	private readonly FakeWindowPlacementApi _api = new();
	private readonly PreviewWindowPlacementCapture _capture;
	private readonly PreviewPlacementPlanner _planner = new();

	public PreviewPlacementSnapDpiTests()
	{
		_capture = new PreviewWindowPlacementCapture(_api);
	}

	private static MonitorInfo Mon(string name, int left, int width, int dpi, bool primary = false) => new(name, new MonitorBounds(left, 0, width, 2000), primary, dpi);

	// TC.PW.71
	[Fact]
	public void Capture_SnappedWindow_RemembersTheActualRectangleNotTheOneBeforeTheSnap()
	{
		_api.Behind = new WindowPlacementModel { Left = 300, Top = 200, Width = 700, Height = 500 };   // before the snap
		_api.Actual = new WindowPlacementModel { Left = 960, Top = 0, Width = 960, Height = 1000 };     // right half

		var captured = _capture.Capture(Hwnd)!;

		Assert.Equal(960, captured.Placement.Left);
		Assert.Equal(960, captured.Placement.Width);
		Assert.Equal(1000, captured.Placement.Height);
		Assert.False(captured.Placement.Maximized);
	}

	[Fact]
	public void Remembered_SnappedRectangle_ReopensExactlyThereOnTheSameMonitor()
	{
		var monitors = new[] { Mon(D1, 0, 1920, 96, true), Mon(D2, 1920, 2560, 96) };
		_api.Actual = new WindowPlacementModel { Left = 1920 + 1280, Top = 0, Width = 1280, Height = 1000 };
		var captured = _capture.Capture(Hwnd)!;

		var saved = _planner.Remember(captured.Placement, monitors[1], captured.Dpi);
		var planned = _planner.Plan(saved, monitors, D1);

		Assert.Equal(3200, planned.Left);
		Assert.Equal(1280, planned.Width);
		Assert.Equal(1000, planned.Height);
	}

	// TC.PW.74
	[Fact]
	public void Capture_Maximized_RemembersTheActualRectangleSeenBeforeMaximizing()
	{
		_api.Actual = new WindowPlacementModel { Left = 960, Top = 0, Width = 960, Height = 1000 };   // snapped, then observed
		_capture.Observe(Hwnd);
		_api.Restored = false;
		_api.Maximized = true;
		_api.Behind = new WindowPlacementModel { Left = 300, Top = 200, Width = 700, Height = 500 };   // stale: from before the snap
		_api.Actual = new WindowPlacementModel { Left = -8, Top = -8, Width = 1936, Height = 1056 };   // the maximized frame

		var captured = _capture.Capture(Hwnd)!;

		Assert.True(captured.Placement.Maximized);
		Assert.Equal(960, captured.Placement.Left);
		Assert.Equal(960, captured.Placement.Width);
	}

	[Fact]
	public void Capture_MaximizedAndNeverSeenRestored_FallsBackToTheRectangleBehindTheState()
	{
		_api.Restored = false;
		_api.Maximized = true;
		_api.Behind = new WindowPlacementModel { Left = 300, Top = 200, Width = 700, Height = 500 };

		var captured = _capture.Capture(Hwnd)!;

		Assert.True(captured.Placement.Maximized);
		Assert.Equal(300, captured.Placement.Left);
		Assert.Equal(700, captured.Placement.Width);
	}

	[Fact]
	public void Capture_Minimized_KeepsTheLastRestoredRectangleAndIsNotMaximized()
	{
		_capture.Observe(Hwnd);
		_api.Restored = false;
		_api.Maximized = false;
		_api.Actual = new WindowPlacementModel { Left = -32000, Top = -32000, Width = 160, Height = 28 };

		var captured = _capture.Capture(Hwnd)!;

		Assert.False(captured.Placement.Maximized);
		Assert.Equal(100, captured.Placement.Left);
		Assert.Equal(800, captured.Placement.Width);
	}

	[Fact]
	public void Capture_StoresTheDpiOfTheMonitor()
	{
		_api.Dpi = 144;

		var captured = _capture.Capture(Hwnd)!;

		Assert.Equal(144, captured.Dpi);
		Assert.Equal(144, _planner.Remember(captured.Placement, null, captured.Dpi).Dpi);
	}

	// TC.PW.72
	[Fact]
	public void Plan_SameMonitorOfTheSameScale_KeepsTheRememberedPixels()
	{
		var monitors = new[] { Mon(D1, 0, 1920, 96, true), Mon(D2, 1920, 2560, 144) };
		var saved = _planner.Remember(new WindowPlacementModel { Left = 2200, Top = 100, Width = 1500, Height = 900 }, monitors[1], 144);

		var planned = _planner.Plan(saved, monitors, D1);

		Assert.Equal(1500, planned.Width);
		Assert.Equal(900, planned.Height);
		Assert.Equal(2200, planned.Left);
	}

	[Fact]
	public void Plan_SameMonitorWhoseScaleChangedSinceSaving_ConvertsTheSize()
	{
		var monitors = new[] { Mon(D1, 0, 1920, 96, true), Mon(D2, 1920, 3840, 192) };
		var saved = _planner.Remember(new WindowPlacementModel { Left = 1920 + 100, Top = 50, Width = 1000, Height = 600 }, Mon(D2, 1920, 1920, 96), 96);

		var planned = _planner.Plan(saved, monitors, D1);

		Assert.Equal(2000, planned.Width);
		Assert.Equal(1200, planned.Height);
		Assert.Equal(1920 + 200, planned.Left);
	}

	// TC.PW.73
	[Fact]
	public void Plan_MonitorGone_ConvertsTheSizeByTheRatioOfTheScales()
	{
		var monitors = new[] { Mon(D1, 0, 3840, 192, true) };
		var saved = new PreviewPlacementModel { Left = 500, Top = 100, Width = 800, Height = 500, MonitorDeviceName = @"\\.\DISPLAY9", MonitorLeft = 5000, Dpi = 96 };

		var planned = _planner.Plan(saved, monitors, null);

		Assert.Equal(1600, planned.Width);
		Assert.Equal(1000, planned.Height);
	}

	[Fact]
	public void Plan_MonitorGone_ConversionIsClampedToTheTarget()
	{
		var monitors = new[] { Mon(D1, 0, 1000, 192, true) };
		var saved = new PreviewPlacementModel { Width = 800, Height = 500, MonitorDeviceName = @"\\.\DISPLAY9", MonitorLeft = 5000, Dpi = 96 };

		Assert.Equal(1000, _planner.Plan(saved, monitors, null).Width);
	}

	[Fact]
	public void Plan_MonitorGone_PlacementWithoutAScale_IsOnlyClamped()
	{
		var monitors = new[] { Mon(D1, 0, 3840, 192, true) };
		var saved = new PreviewPlacementModel { Width = 800, Height = 500, MonitorDeviceName = @"\\.\DISPLAY9", MonitorLeft = 5000 };

		Assert.Equal(800, _planner.Plan(saved, monitors, null).Width);
	}

	// TC.PW.72: the restore guard
	[Fact]
	public void RestoreGuard_DpiChangedDuringTheRestore_AppliesTheRememberedRectangleAgain()
	{
		var guard = new PreviewRestoreGuard(_api);
		guard.Restore(Hwnd, new WindowPlacementModel { Left = 10, Top = 20, Width = 1500, Height = 900 });

		Assert.True(guard.OnDpiChanged(Hwnd));

		Assert.Equal(2, _api.Applied.Count);   // the restore itself, then the re-apply
		Assert.Equal(1500, _api.Applied[1].Width);
	}

	[Fact]
	public void RestoreGuard_AfterTheRestore_LeavesTheUserAlone()
	{
		var guard = new PreviewRestoreGuard(_api);
		guard.Restore(Hwnd, new WindowPlacementModel { Width = 1500, Height = 900 });
		guard.End();

		Assert.False(guard.OnDpiChanged(Hwnd));
		Assert.Single(_api.Applied);
		Assert.False(guard.Active);
	}

	[Fact]
	public void RestoreGuard_NeverBegun_DoesNothing()
	{
		Assert.False(new PreviewRestoreGuard(_api).OnDpiChanged(Hwnd));
	}

	[Fact]
	public void RestoreGuard_StopsAfterTheBudget()
	{
		var guard = new PreviewRestoreGuard(_api);
		guard.Restore(Hwnd, new WindowPlacementModel { Width = 1500, Height = 900 });

		for (var i = 0; i < Constants.PreviewWindow.MaxRestoreReapplies + 3; i++)
			guard.OnDpiChanged(Hwnd);

		Assert.Equal(Constants.PreviewWindow.MaxRestoreReapplies + 1, _api.Applied.Count);
	}

	// F1: Windows raises the DPI change inside the apply, so the phase must already be active then.
	[Fact]
	public void RestoreGuard_IsActiveWhileTheRememberedRectangleIsBeingApplied()
	{
		var guard = new PreviewRestoreGuard(_api);
		var activeDuringApply = false;
		var reapplied = false;
		_api.OnApply = () =>
		{
			activeDuringApply = guard.Active;
			if (guard.Active && !reapplied)
			{
				reapplied = true;
				guard.OnDpiChanged(Hwnd);   // what the window does when WM_DPICHANGED arrives inside SetWindowPlacement
			}
		};

		guard.Restore(Hwnd, new WindowPlacementModel { Width = 1500, Height = 900 });

		Assert.True(activeDuringApply);
		Assert.Equal(2, _api.Applied.Count);
	}

	// F3: Win+Shift+Arrow while maximized
	[Fact]
	public void Capture_MaximizedAndMovedToAnotherMonitor_KeepsTheRectangleWithItsOwnMonitorAndScale()
	{
		var a = Mon(D1, 0, 1920, 96, true);
		var b = Mon(D2, 1920, 3840, 192);
		var current = a;
		var capture = new PreviewWindowPlacementCapture(_api, _ => current);
		_api.Actual = new WindowPlacementModel { Left = 200, Top = 100, Width = 900, Height = 700 };
		capture.Observe(Hwnd);
		_api.Restored = false;
		_api.Maximized = true;
		_api.Dpi = 192;
		current = b;

		var captured = capture.Capture(Hwnd)!;

		Assert.Equal(D1, captured.Monitor!.DeviceName);
		Assert.Equal(96, captured.Dpi);
		Assert.Equal(200, captured.Placement.Left);
		Assert.True(captured.Placement.Maximized);
	}
}
