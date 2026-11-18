using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.2 (placements go with their group) and FR.29.6 (a renamed monitor is found by where it sits).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRoutingSupportTests
{
	private static PreviewPlacementModel Placement(string? device = null, int monitorLeft = 0) =>
		new() { Left = 100, Top = 100, Width = 800, Height = 600, MonitorDeviceName = device, MonitorLeft = monitorLeft, MonitorTop = 0 };

	[Fact]
	public void SaveGroups_DropsThePlacementsOfGroupsThatNoLongerExist()
	{
		var service = new PreviewSettingsService(SettingsTestSupport.NewStore());
		service.SavePlacement("images", Placement());
		service.SavePlacement("web", Placement());

		service.SaveGroups(service.Groups.Where(g => g.Id != "images").ToList());

		Assert.Null(service.GetPlacement("images"));
		Assert.NotNull(service.GetPlacement("web"));
	}

	[Fact]
	public void ResetGroups_DropsThePlacementsOfUserGroups()
	{
		var service = new PreviewSettingsService(SettingsTestSupport.NewStore());
		service.SaveGroups([.. service.Groups, new PreviewGroupModel { Id = "mine", Name = "Mine", Patterns = "log" }]);
		service.SavePlacement("mine", Placement());
		service.SavePlacement("images", Placement());

		service.ResetGroups();

		Assert.Null(service.GetPlacement("mine"));
		Assert.NotNull(service.GetPlacement("images"));
	}

	[Fact]
	public void SaveGroups_DoesNotTouchTheSavedWindowsOrTheToggle()
	{
		var service = new PreviewSettingsService(SettingsTestSupport.NewStore());
		service.SetWindowsEnabled(false);
		service.SaveOpenWindows([new OpenPreviewWindowModel { GroupId = "web" }]);

		service.SaveGroups(service.Groups.Where(g => g.Id != "images").ToList());

		Assert.False(service.WindowsEnabled);
		Assert.Single(service.OpenWindows);
	}

	[Fact]
	public void Plan_MonitorRenamedByWindows_IsFoundByItsWorkAreaOrigin()
	{
		var renamed = new MonitorInfo(@"\\.\DISPLAY7", new MonitorBounds(1920, 0, 2560, 1400), false);
		var primary = new MonitorInfo(@"\\.\DISPLAY1", new MonitorBounds(0, 0, 1920, 1040), true);

		var placement = new PreviewPlacementPlanner().Plan(Placement(@"\\.\DISPLAY2", monitorLeft: 1920), [primary, renamed], @"\\.\DISPLAY1");

		Assert.Equal(100, placement.Left);   // kept where it was: the renamed monitor is its home
		Assert.Equal(800, placement.Width);
	}

	[Fact]
	public void Plan_NeitherNameNorOriginKnown_CentresOnTheTarget()
	{
		var primary = new MonitorInfo(@"\\.\DISPLAY1", new MonitorBounds(0, 0, 1920, 1040), true);
		var other = new MonitorInfo(@"\\.\DISPLAY3", new MonitorBounds(1920, 0, 2560, 1400), false);

		var placement = new PreviewPlacementPlanner().Plan(Placement(@"\\.\DISPLAY2", monitorLeft: 5000), [primary, other], @"\\.\DISPLAY1");

		Assert.Equal(1920 + (2560 - 800) / 2, placement.Left);
		Assert.Equal((1400 - 600) / 2, placement.Top);
	}
}
