using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The preview router (FR.29.1, FR.29.3–FR.29.11) over fake windows, a fake monitor list and the real matcher and settings service.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRouterTests : IDisposable
{
	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly PreviewSettingsService _settings;
	private readonly FakeMonitorWatcher _monitors = new(2);
	private readonly FakePreviewWindowFactory _factory = new();
	private readonly FakeTessynClient _client = new();
	private readonly PreviewRouter _router;
	private int _stateEvents;

	public PreviewRouterTests()
	{
		_settings = new PreviewSettingsService(_store);
		_router = NewRouter();
		_router.StateChanged += (_, _) => _stateEvents++;
	}

	public void Dispose() => _router.Dispose();

	private PreviewRouter NewRouter(IClientStateStore? state = null) =>
		new(_settings, new PreviewGroupMatcher(), _monitors, _factory, new UrlPolicy(), new ImmediateUiDispatcher(), NullLogger<PreviewRouter>.Instance, state);

	private static PreviewItemModel File(string name, string? node = "n1", string? focusItem = null) =>
		new() { Kind = PreviewItemKind.File, FileName = name, RefId = "ref-" + name, SourceNodeId = node, SourceFocusItemId = focusItem };

	private static PreviewItemModel Url(string? url, string? node = "n1", string? focusItem = null) =>
		new() { Kind = PreviewItemKind.Url, Url = url, SourceNodeId = node, SourceFocusItemId = focusItem };

	private bool OpenFile(string name, string? node = "n1", string? focusItem = null) =>
		_router.TryOpenFile(File(name, node, focusItem), new PreviewTarget("ref-" + name, name), _client);

	private FakePreviewWindowHandle Window(string groupId, bool pinned = false) =>
		_factory.Open.Single(h => h.GroupId == groupId && h.IsPinned == pinned);

	private string[] SavedGroups() => _settings.OpenWindows.Select(w => w.GroupId + (w.Pinned ? "*" : string.Empty)).ToArray();

	// ---- routing ----

	[Fact]
	public void File_GoesToTheWindowOfItsGroup_AndSecondItemOfTheGroupReplacesInIt()
	{
		Assert.True(OpenFile("a.png"));
		Assert.True(OpenFile("b.JPG"));

		var window = Window("images");
		Assert.Equal(["a.png", "b.JPG"], window.Shown.Select(i => i.FileName));
		Assert.Single(_factory.Created);
		Assert.Equal("ref-b.JPG", window.LastTarget!.Handle);
		Assert.False(window.IsPinned);
		Assert.Null(window.OwnPlacement);
	}

	[Fact]
	public void TwoGroups_TwoWindows_AndAnAddressGoesToItsGroup()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		Assert.True(_router.TryOpenUrl(Url("https://linear.app/hardstop/issue/HSE-1")));
		Assert.True(_router.TryOpenUrl(Url("https://example.com/x")));

		Assert.Equal(["images", "text-code", "linear", "web"], _factory.Open.Select(h => h.GroupId));
		Assert.Equal("https://linear.app/hardstop/issue/HSE-1", Window("linear").Item!.Url);
	}

	[Fact]
	public void ShownItem_IsACopy()
	{
		var item = File("a.png");
		_router.TryOpenFile(item, new PreviewTarget("r", "a.png"), _client);

		item.FileName = "changed";

		Assert.Equal("a.png", Window("images").Item!.FileName);
	}

	[Fact]
	public void NoGroup_NotHttp_OrToggleOff_IsRefused()
	{
		Assert.False(OpenFile("a.xyz"));
		Assert.False(OpenFile("noextension"));
		Assert.False(_router.TryOpenUrl(Url("ftp://example.com/x")));
		Assert.False(_router.TryOpenUrl(Url("javascript:alert(1)")));
		Assert.False(_router.TryOpenUrl(Url(null)));
		Assert.Empty(_factory.Created);

		_router.SetEnabled(false);
		Assert.False(OpenFile("a.png"));
		Assert.False(_router.TryOpenUrl(Url("https://example.com")));
		Assert.False(_router.WouldRouteFile("a.png"));
	}

	[Fact]
	public void OneMonitor_IsNotActive_AndRefuses()
	{
		_monitors.Set(1);

		Assert.False(_router.CanToggle);
		Assert.True(_router.IsEnabled);
		Assert.False(_router.IsActive);
		Assert.False(OpenFile("a.png"));
		Assert.False(_router.WouldRouteFile("a.png"));
	}

	[Fact]
	public void WouldRouteFile_NeedsTheModeAndAGroup()
	{
		Assert.True(_router.WouldRouteFile("pic.png"));
		Assert.False(_router.WouldRouteFile("movie.mp4"));
	}

	[Fact]
	public void SetEnabled_StoresTheChoice_AndRaisesStateChangedOnce()
	{
		_router.SetEnabled(false);

		Assert.False(_settings.WindowsEnabled);
		Assert.False(_router.IsActive);
		Assert.Equal(1, _stateEvents);
		_router.SetEnabled(false);
		Assert.Equal(1, _stateEvents);
	}

	[Fact]
	public void MonitorChange_RaisesStateChanged_OnlyWhenTheStateChanged()
	{
		_monitors.Set(3);
		Assert.Equal(0, _stateEvents);

		_monitors.Set(1);
		Assert.Equal(1, _stateEvents);
	}

	// ---- pinning ----

	[Fact]
	public void Pin_LeavesTheGroupSlot_NextItemOpensANewWindow_PinnedKeepsItsItem()
	{
		OpenFile("a.png");
		var first = Window("images");
		first.Pin();

		OpenFile("b.png");

		Assert.Equal(2, _factory.Open.Count());
		Assert.Equal("a.png", first.Item!.FileName);
		Assert.Equal("b.png", _factory.Created[1].Item!.FileName);
		Assert.False(_factory.Created[1].IsPinned);
	}

	// ---- auto-close ----

	[Fact]
	public void Rule1_SelectingAnotherSession_ClosesAutoCloseGroupWindowsOfOtherSessions_Only()
	{
		_settings.SetAutoClose("images", true);
		OpenFile("a.png", "n1");
		OpenFile("a.md", "n1");   // text group: auto-close off
		_router.OnSessionShown("n1");
		Assert.Equal(2, _factory.Open.Count());

		_router.OnSessionShown("n2");

		Assert.Equal(["text-code"], _factory.Open.Select(h => h.GroupId));
		Assert.Equal("n2", _router.CurrentNodeId);
	}

	[Fact]
	public void Rule1_NeverClosesPinnedWindows_NorOnANullSelection()
	{
		_settings.SetAutoClose("images", true);
		OpenFile("a.png", "n1");
		OpenFile("a.md", "n1");
		_settings.SetAutoClose("text-code", true);
		Window("images").Pin();

		_router.OnSessionShown(null);
		Assert.Equal(2, _factory.Open.Count());

		_router.OnSessionShown("n2");

		Assert.Equal(["images"], _factory.Open.Select(h => h.GroupId));
	}

	[Fact]
	public void Rule2_ClosedFocusItem_ClosesItsWindows_OnlyInAutoCloseGroups_OnlyOfThatSession()
	{
		_settings.SetAutoClose("images", true);
		_settings.SetAutoClose("web", true);
		OpenFile("a.png", "n1", "item1");
		OpenFile("a.md", "n1", "item1");   // not auto-close
		_router.TryOpenUrl(Url("https://example.com", "n1", "item9"));

		_router.OnFocusItemsClosed("n2", ["item1"]);   // another session's items
		Assert.Equal(3, _factory.Open.Count());

		_router.OnFocusItemsClosed("n1", ["item1"]);

		Assert.Equal(["text-code", "web"], _factory.Open.Select(h => h.GroupId));
	}

	[Fact]
	public void Rule2_EmptyAnnouncement_AndPinnedWindows_StayOpen()
	{
		_settings.SetAutoClose("images", true);
		OpenFile("a.png", "n1", "item1");
		_router.OnFocusItemsClosed("n1", []);
		Assert.Single(_factory.Open);

		Window("images").Pin();
		_router.OnFocusItemsClosed("n1", ["item1"]);

		Assert.Single(_factory.Open);
	}

	// ---- mode and monitors ----

	[Fact]
	public void ToggleOff_ClosesGroupWindows_PinnedStay()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		Window("images").Pin();

		_router.SetEnabled(false);

		Assert.Equal(["images"], _factory.Open.Select(h => h.GroupId));
		Assert.True(_factory.Open.Single().IsPinned);
	}

	[Fact]
	public void MonitorsDropToOne_ClosesGroupWindows_PinnedStay()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		Window("text-code").Pin();

		_monitors.Set(1);

		Assert.Equal(["text-code"], _factory.Open.Select(h => h.GroupId));
	}

	// ---- groups ----

	[Fact]
	public void GroupRemoved_ClosesItsGroupWindow_KeepsItsPinnedOne()
	{
		OpenFile("a.png");
		Window("images").Pin();
		OpenFile("b.png");
		OpenFile("a.md");

		_settings.SaveGroups(_settings.Groups.Where(g => g.Id != "images").ToList());

		Assert.Equal(2, _factory.Open.Count());
		Assert.False(_factory.Created[0].IsClosed);
		Assert.True(_factory.Created[1].IsClosed);
		Assert.Equal(["images*", "text-code"], SavedGroups());
	}

	[Fact]
	public void GroupRenamed_TellsItsWindows()
	{
		OpenFile("a.png");
		Window("images").Pin();
		OpenFile("b.png");

		var groups = _settings.Groups.ToList();
		groups.Single(g => g.Id == "images").Name = "Pictures";
		_settings.SaveGroups(groups);

		Assert.All(_factory.Open, w => Assert.Equal("Pictures", w.GroupName));
	}

	// ---- the saved list ----

	[Fact]
	public void SavedList_FollowsOpenCloseAndPin_InOpeningOrder()
	{
		OpenFile("a.png");
		_router.TryOpenUrl(Url("https://example.com/x"));
		Assert.Equal(["images", "web"], SavedGroups());

		var images = Window("images");
		images.CurrentPlacement = new PreviewPlacementModel { Left = 5, Top = 6, Width = 700, Height = 500, MonitorDeviceName = "m" };
		images.Pin();

		var saved = _settings.OpenWindows;
		Assert.Equal(["images*", "web"], SavedGroups());
		Assert.Equal(700, saved[0].Placement!.Width);
		Assert.Null(saved[1].Placement);
		Assert.Equal("a.png", saved[0].Item.FileName);

		Window("web").RaiseClosed(byUser: true);
		Assert.Equal(["images*"], SavedGroups());
	}

	[Fact]
	public void SavedList_FollowsANavigationInsideAWindow()
	{
		_router.TryOpenUrl(Url("https://example.com/x"));

		Window("web").Navigate("https://example.com/y");

		Assert.Equal("https://example.com/y", _settings.OpenWindows.Single().Item.Url);
	}

	[Fact]
	public void APinnedWindowsMove_SavesTheListAtOnce_SoACrashKeepsIt()
	{
		OpenFile("a.png");
		var images = Window("images");
		images.Pin();
		Assert.Equal(["images*"], SavedGroups());

		images.MoveTo(new PreviewPlacementModel { Left = 40, Width = 700, Height = 500 });

		Assert.Equal(40, _settings.OpenWindows.Single().Placement!.Left);
		Assert.Equal(700, _settings.OpenWindows.Single().Placement!.Width);
	}

	[Fact]
	public void AGroupWindowsPlacementEvent_DoesNotWriteThePlacementIntoTheList()
	{
		OpenFile("a.png");
		var images = Window("images");

		images.MoveTo(new PreviewPlacementModel { Left = 40, Width = 700, Height = 500 });

		Assert.Null(_settings.OpenWindows.Single().Placement);
	}

	[Fact]
	public void AClosedWindowsPlacementEvent_IsNotHeardAnymore()
	{
		OpenFile("a.png");
		var images = Window("images");
		images.Pin();
		images.RaiseClosed(byUser: true);
		Assert.Empty(_settings.OpenWindows);

		images.MoveTo(new PreviewPlacementModel { Left = 1 });

		Assert.Empty(_settings.OpenWindows);
	}

	[Fact]
	public void SessionShown_SkipsAWindowWithoutAnItem()
	{
		OpenFile("a.png");
		_settings.SetAutoClose("images", true);
		var images = Window("images");
		images.Item = null;

		_router.OnSessionShown("n2");

		Assert.Equal(0, images.CloseCalls);
	}

	[Fact]
	public void CloseAllForExit_ClosesEverything_ButKeepsTheSavedList()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		var images = Window("images");
		images.CurrentPlacement = new PreviewPlacementModel { Width = 800, Height = 600 };
		images.Pin();

		_router.CloseAllForExit();

		Assert.Empty(_factory.Open);
		Assert.Equal(["images*", "text-code"], SavedGroups());
		Assert.Equal(800, _settings.OpenWindows[0].Placement!.Width);
	}

	// ---- restore ----

	private static OpenPreviewWindowModel Saved(string group, PreviewItemModel item, bool pinned = false, PreviewPlacementModel? placement = null) =>
		new() { GroupId = group, Pinned = pinned, Item = item, Placement = placement };

	private Task RestoreAsync(params OpenPreviewWindowModel[] saved) => RestoreAsync(_router, saved);

	private Task RestoreAsync(PreviewRouter router, params OpenPreviewWindowModel[] saved)
	{
		_settings.SaveOpenWindows(saved);
		return router.RestoreAsync(_client);
	}

	[Fact]
	public async Task Restore_ReopensGroupAndPinnedWindows_PinnedAtTheirOwnPlacement_WebAtItsAddress_FileByRefId()
	{
		_client.Files["ref-a.png"] = ("a.png", "image/png", [1, 2, 3]);
		var placement = new PreviewPlacementModel { Left = 10, Top = 20, Width = 640, Height = 480, MonitorDeviceName = "m" };

		await RestoreAsync(
			Saved("web", Url("https://example.com/page")),
			Saved("images", File("a.png"), pinned: true, placement),
			Saved("images", File("a.png")));

		Assert.Equal(3, _factory.Created.Count);
		var web = _factory.Created[0];
		Assert.False(web.IsPinned);
		Assert.Equal("https://example.com/page", web.Item!.Url);
		var pinned = _factory.Created[1];
		Assert.True(pinned.IsPinned);
		Assert.Equal(640, pinned.OwnPlacement!.Width);
		Assert.Equal("ref-a.png", pinned.LastTarget!.Handle);
		Assert.Null(_factory.Created[2].OwnPlacement);
		Assert.Equal(["web", "images*", "images"], SavedGroups());
		Assert.Contains(_client.Calls, c => c.StartsWith("file.read:ref-a.png:0:1", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Restore_FileFromAPath_AsksFilePreviewAgain()
	{
		_client.Files["h1"] = ("a.png", "image/png", [1]);
		_client.PreviewHandles["n1|e1|C:\\x\\a.png"] = "h1";
		var item = new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "a.png", NodeId = "n1", EntryId = "e1", Path = "C:\\x\\a.png", SourceNodeId = "n1" };

		await RestoreAsync(Saved("images", item));

		Assert.Contains(_client.Calls, c => c.StartsWith("file.preview:n1:e1:", StringComparison.Ordinal));
		Assert.Equal("h1", Window("images").LastTarget!.Handle);
	}

	[Fact]
	public async Task Restore_SkipsAFailingFile_AGoneGroup_AndDropsThemFromTheList()
	{
		_client.Files["ref-ok.png"] = ("ok.png", "image/png", [1]);
		var missing = File("gone.png");   // no such handle: the 1-byte read fails
		var noHandle = new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "x.png", NodeId = "n1", EntryId = "e", Path = "p" };   // file.preview refuses

		await RestoreAsync(
			Saved("images", missing),
			Saved("images", noHandle),
			Saved("deleted-group", Url("https://example.com")),
			Saved("images", File("ok.png")));

		Assert.Equal("ok.png", Window("images").Item!.FileName);
		Assert.Single(_factory.Created);
		Assert.Equal(["images"], SavedGroups());
	}

	[Fact]
	public async Task Restore_SkipsAddressesThatAreNotHttp()
	{
		await RestoreAsync(Saved("web", Url("file:///C:/secret.txt")));

		Assert.Empty(_factory.Created);
		Assert.Empty(_settings.OpenWindows);
	}

	[Fact]
	public async Task Restore_WhileInactive_SkipsGroupWindows_ButReopensPinnedOnes()
	{
		_monitors.Set(1);

		await RestoreAsync(Saved("web", Url("https://example.com/a")), Saved("web", Url("https://example.com/b"), pinned: true));

		var only = Assert.Single(_factory.Created);
		Assert.True(only.IsPinned);
		Assert.Equal(["web*"], SavedGroups());
	}

	[Fact]
	public async Task Restore_AutoCloseWindowOfAnotherSession_IsSkipped_OfTheShownSessionReopens()
	{
		_settings.SetAutoClose("web", true);
		_router.OnSessionShown("n2");

		await RestoreAsync(Saved("web", Url("https://example.com/a", "n1")), Saved("slack", Url("https://hardstop.slack.com/x", "n1")));

		Assert.Equal(["slack"], _factory.Created.Select(h => h.GroupId));   // slack does not close automatically: it comes back
		Assert.Equal(["slack"], SavedGroups());
	}

	[Fact]
	public async Task Restore_AutoCloseWindowOfTheShownSession_Reopens()
	{
		_settings.SetAutoClose("web", true);
		_router.OnSessionShown("n1");

		await RestoreAsync(Saved("web", Url("https://example.com/a", "n1")));

		Assert.Single(_factory.Created);
	}

	[Fact]
	public async Task Restore_AutoCloseUsesTheLastSelectedSession_WhenNoneIsShownYet()
	{
		_settings.SetAutoClose("web", true);
		var state = new MemoryClientState();
		state.State.LastSelectedNodeId = "n1";
		using var router = NewRouter(state);

		await RestoreAsync(router, Saved("web", Url("https://example.com/a", "n1")), Saved("web", Url("https://example.com/b", "n9")));

		Assert.Equal("https://example.com/a", Assert.Single(_factory.Created).Item!.Url);
	}

	[Fact]
	public async Task Restore_SkipsAGroupWindowWhoseGroupAlreadyHasOne()
	{
		OpenFile("new.png");

		await RestoreAsync(Saved("images", File("old.png")), Saved("web", Url("https://example.com")));

		Assert.Equal(["images", "web"], _factory.Created.Select(h => h.GroupId));
		Assert.Equal("new.png", Window("images").Item!.FileName);
		Assert.Equal(["images", "web"], SavedGroups());
	}

	[Fact]
	public async Task Restore_RunsOncePerAppRun()
	{
		await RestoreAsync(Saved("web", Url("https://example.com/a")));
		Window("web").RaiseClosed(byUser: true);
		_settings.SaveOpenWindows([Saved("web", Url("https://example.com/b"))]);

		await _router.RestoreAsync(_client);

		Assert.Single(_factory.Created);
	}

	[Fact]
	public async Task Restore_KeepsTheWindowsNotYetRestoredInTheSavedList_WhileAFileIsChecked()
	{
		var seen = new List<string[]>();
		_client.FileReadHandler = (_, _, _) =>
		{
			OpenFile("z.md");   // the user opens something while the restore checks the file
			seen.Add(SavedGroups());
			return new FileReadResult("a.png", "image/png", 2, DateTimeOffset.UnixEpoch, 0, 1, "base64", "AQ==", false);
		};

		await RestoreAsync(Saved("images", File("a.png")), Saved("web", Url("https://example.com")));

		Assert.Equal(["text-code", "images", "web"], seen.Single());   // the one being checked and the one waiting are both still saved
		Assert.Equal(["text-code", "images", "web"], SavedGroups());
	}
}
