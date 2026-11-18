using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The preview groups and the preview windows' state over the client settings store, the monitor seam and its start option.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewSettingsServiceTests
{
	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly PreviewSettingsService _service;
	private int _groupEvents;
	private int _enabledEvents;

	public PreviewSettingsServiceTests()
	{
		_service = new PreviewSettingsService(_store);
		_service.GroupsChanged += (_, _) => _groupEvents++;
		_service.WindowsEnabledChanged += (_, _) => _enabledEvents++;
	}

	[Fact]
	public void NeverEdited_YieldsTheEightDefaultsInOrder_AsCopies()
	{
		var groups = _service.Groups;

		Assert.Equal(["images", "text-code", "csv", "slack", "windows-app", "backend-frontend", "linear", "web"], groups.Select(g => g.Id));
		Assert.All(groups, g => Assert.False(g.AutoClose));
		groups[0].Name = "changed";
		Assert.Equal("Images", _service.Groups[0].Name);
		Assert.Null(_store.Get<PreviewSettings>().Groups);
	}

	[Fact]
	public void SaveGroups_StoresCopies_AndRaisesTheEvent()
	{
		var mine = new PreviewGroupModel { Id = "abc", Name = "Mine", Patterns = "md" };

		_service.SaveGroups([mine]);
		mine.Name = "later";

		Assert.Equal("Mine", Assert.Single(_service.Groups).Name);
		Assert.Equal(1, _groupEvents);
	}

	[Fact]
	public void ResetGroups_ReturnsToTheDefaults()
	{
		_service.SaveGroups([new PreviewGroupModel { Id = "abc", Name = "Mine" }]);

		_service.ResetGroups();

		Assert.Equal(8, _service.Groups.Count);
		Assert.Null(_store.Get<PreviewSettings>().Groups);
		Assert.Equal(2, _groupEvents);
	}

	[Fact]
	public void SetAutoClose_FlipsOneDefaultGroup_AndKeepsTheRest()
	{
		_service.SetAutoClose("slack", true);

		Assert.Equal(8, _service.Groups.Count);
		Assert.True(_service.Groups.Single(g => g.Id == "slack").AutoClose);
		Assert.Equal(1, _service.Groups.Count(g => g.AutoClose));
		Assert.Equal(1, _groupEvents);
	}

	[Fact]
	public void SetAutoClose_UnknownGroup_DoesNothing()
	{
		_service.SetAutoClose("nope", true);

		Assert.Equal(0, _groupEvents);
		Assert.Null(_store.Get<PreviewSettings>().Groups);
	}

	[Fact]
	public void WindowsEnabled_DefaultsOn_AndRaisesOnlyWhenTheChoiceChanges()
	{
		Assert.True(_service.WindowsEnabled);

		_service.SetWindowsEnabled(false);
		_service.SetWindowsEnabled(false);
		_service.SavePlacement("images", new PreviewPlacementModel { Width = 1 });
		_service.SetWindowsEnabled(true);

		Assert.Equal(2, _enabledEvents);
		Assert.True(_service.WindowsEnabled);
	}

	[Fact]
	public void WritesFromAnywhere_RaiseTheEvents()
	{
		_store.Update<PreviewSettings>(s => s.Groups = []);
		_store.Update<PreviewWindowsSettings>(s => s.Enabled = false);

		Assert.Equal(1, _groupEvents);
		Assert.Equal(1, _enabledEvents);
	}

	[Fact]
	public void Placements_AreSavedPerGroup_AndForgotten()
	{
		Assert.Null(_service.GetPlacement("images"));

		_service.SavePlacement("images", new PreviewPlacementModel { Left = 5, Width = 10, Height = 20, MonitorDeviceName = "d" });

		Assert.Equal(5, _service.GetPlacement("images")!.Left);
		Assert.Null(_service.GetPlacement("csv"));

		_service.ForgetPlacements();

		Assert.Null(_service.GetPlacement("images"));
		Assert.True(_service.WindowsEnabled);
	}

	[Fact]
	public void SavePlacement_IgnoresAGroupThatIsNotInTheList()
	{
		_service.SavePlacement("ghost", new PreviewPlacementModel { Left = 7 });

		Assert.Null(_service.GetPlacement("ghost"));
		Assert.Empty(_store.Get<PreviewWindowsSettings>().Placements);
	}

	[Fact]
	public void ARemovedGroupsLatePlacement_IsNotSavedAgain()
	{
		_service.SavePlacement("csv", new PreviewPlacementModel { Left = 1 });
		_service.SaveGroups(_service.Groups.Where(g => g.Id != "csv").ToList());
		Assert.Null(_service.GetPlacement("csv"));

		_service.SavePlacement("csv", new PreviewPlacementModel { Left = 2 });   // the closing window's pending timer

		Assert.Null(_service.GetPlacement("csv"));
		Assert.DoesNotContain("csv", _store.Get<PreviewWindowsSettings>().Placements.Keys);
	}

	[Fact]
	public void OpenWindows_DefaultToEmpty()
	{
		Assert.Empty(_service.OpenWindows);
	}

	[Fact]
	public void OpenWindows_RoundTrip()
	{
		var window = new OpenPreviewWindowModel
		{
			GroupId = "images",
			Pinned = true,
			Item = new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "a.png", RefId = "r1", SourceNodeId = "n1", SourceFocusItemId = "f1" },
			Placement = new PreviewPlacementModel { Left = 7, Width = 10, Height = 20, MonitorDeviceName = "d" },
		};

		_service.SaveOpenWindows([window]);

		var saved = Assert.Single(_service.OpenWindows);
		Assert.Equal(("images", true), (saved.GroupId, saved.Pinned));
		Assert.Equal((PreviewItemKind.File, "a.png", "r1", "n1", "f1"), (saved.Item.Kind, saved.Item.FileName, saved.Item.RefId, saved.Item.SourceNodeId, saved.Item.SourceFocusItemId));
		Assert.Equal(7, saved.Placement!.Left);
	}

	[Fact]
	public void SaveOpenWindows_KeepsPlacementsAndTheToggle_AndRaisesNoEnabledEvent()
	{
		_service.SavePlacement("images", new PreviewPlacementModel { Left = 5 });
		_service.SetWindowsEnabled(false);
		var before = _enabledEvents;

		_service.SaveOpenWindows([new OpenPreviewWindowModel { GroupId = "csv", Item = new PreviewItemModel { Kind = PreviewItemKind.Url, Url = "https://x.test" } }]);

		Assert.Equal(5, _service.GetPlacement("images")!.Left);
		Assert.False(_service.WindowsEnabled);
		Assert.Equal(before, _enabledEvents);
		Assert.Equal("https://x.test", _service.OpenWindows[0].Item.Url);
	}

	[Fact]
	public void PlacementModel_ConvertsToAWindowPlacement()
	{
		var placement = new PreviewPlacementModel { Left = 1, Top = 2, Width = 3, Height = 4, Maximized = true }.ToWindowPlacement();

		Assert.Equal((1, 2, 3, 4, true), (placement.Left, placement.Top, placement.Width, placement.Height, placement.Maximized));
	}

	[Fact]
	public void ScriptedMonitorWatcher_ListsNMonitorsSideBySide()
	{
		var watcher = new ScriptedMonitorWatcher(3);

		Assert.Equal([@"\\.\TESTDISPLAY1", @"\\.\TESTDISPLAY2", @"\\.\TESTDISPLAY3"], watcher.Monitors.Select(m => m.DeviceName));
		Assert.Equal([0, 1920, 3840], watcher.Monitors.Select(m => m.WorkArea.Left));
		Assert.Equal([true, false, false], watcher.Monitors.Select(m => m.IsPrimary));
		Assert.All(watcher.Monitors, m => Assert.Equal((1920, 1080), (m.WorkArea.Width, m.WorkArea.Height)));
		Assert.False(watcher.IsSettling);
		Assert.Equal(@"\\.\TESTDISPLAY1", watcher.MonitorOf(IntPtr.Zero));
	}

	[Theory]
	[InlineData("2", 2)]
	[InlineData("1", 1)]
	[InlineData("4", 4)]
	[InlineData("0", null)]
	[InlineData("5", null)]
	[InlineData("x", null)]
	[InlineData("-1", null)]
	public void StartupOptions_TestMonitors_WithTestWindow(string value, int? expected)
	{
		var options = StartupOptions.Parse(["--test-window", "--test-monitors", value]);

		Assert.Equal(expected, options.TestMonitorCount);
	}

	[Fact]
	public void StartupOptions_TestMonitors_IgnoredWithoutTestWindow_AndAbsentByDefault()
	{
		Assert.Null(StartupOptions.Parse(["--test-monitors", "2"]).TestMonitorCount);
		Assert.Null(StartupOptions.Parse(["--test-window"]).TestMonitorCount);
	}
}
