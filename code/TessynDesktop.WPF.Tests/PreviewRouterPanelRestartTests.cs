using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The docked panels across a restart and a mode change (FR.29.22, FR.29.23) over fake panels: what is saved when, what reopens, what is skipped.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRouterPanelRestartTests : IDisposable
{
	private const string DefaultId = Constants.Previews.DefaultPanelId;

	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly PreviewSettingsService _settings;
	private readonly FakeMonitorWatcher _monitors = new(1);
	private readonly FakePreviewWindowFactory _windows = new();
	private readonly FakePreviewDockFactory _dock = new();
	private readonly FakeTessynClient _client = new();
	private readonly CapturingLogger<PreviewRouter> _log = new();
	private PreviewRouter _router;

	public PreviewRouterPanelRestartTests()
	{
		_settings = new PreviewSettingsService(_store);
		_router = NewRouter();
	}

	public void Dispose() => _router.Dispose();

	private PreviewRouter NewRouter() =>
		new(_settings, new PreviewGroupMatcher(), _monitors, _windows, new UrlPolicy(), new ImmediateUiDispatcher(), _log, null, _dock);

	private static PreviewItemModel File(string name, string? node = "n1") =>
		new() { Kind = PreviewItemKind.File, FileName = name, RefId = "ref-" + name, SourceNodeId = node };

	private static PreviewItemModel Url(string url, string? node = "n1") => new() { Kind = PreviewItemKind.Url, Url = url, SourceNodeId = node };

	private void OpenFile(string name, string? node = "n1") => _router.TryOpenFile(File(name, node), new PreviewTarget("ref-" + name, name), _client);

	private FakePreviewWindowHandle Panel(string id, bool pinned = false) => _dock.Open.Single(h => h.GroupId == id && h.IsPinned == pinned);

	private string[] SavedPanels() => _settings.OpenPanels.Select(p => p.GroupId + (p.Pinned ? "*" : string.Empty)).ToArray();

	private static OpenPreviewPanelModel SavedPanel(string group, PreviewItemModel item, bool pinned = false, PreviewDockLayoutModel? layout = null, string? panelId = null) =>
		new() { GroupId = group, Pinned = pinned, Item = item, Layout = layout, PanelId = panelId };

	/// <summary>A new run of the app: a fresh router over the same saved settings and a fresh set of panels.</summary>
	private async Task RestartAsync(params OpenPreviewPanelModel[] saved)
	{
		_router.Dispose();
		_dock.Created.Clear();
		_settings.SaveOpenPanels(saved);
		_router = NewRouter();
		await _router.RestoreAsync(_client);
	}

	// ---- item 1: what is saved ----

	[Fact]
	public void OpenPanels_AreSavedWithTheirItems_GroupPinnedAndDefault()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		Panel("text-code").Pin();
		OpenFile("b.md");
		OpenFile("data.xyz");
		_router.TryOpenUrl(Url("https://example.com/x"));

		Assert.Equal(["images", "text-code*", "text-code", DefaultId, "web"], SavedPanels());
		var pinned = _settings.OpenPanels.Single(p => p.Pinned);
		Assert.Equal("a.md", pinned.Item.FileName);
		Assert.Equal("ref-a.md", pinned.Item.RefId);
		Assert.Equal("https://example.com/x", _settings.OpenPanels.Single(p => p.GroupId == "web").Item.Url);
		Assert.Equal("data.xyz", _settings.OpenPanels.Single(p => p.GroupId == DefaultId).Item.FileName);
	}

	[Fact]
	public void APanelTheUserClosed_LeavesTheSavedList()
	{
		OpenFile("a.png");
		OpenFile("a.md");

		Panel("images").RaiseClosed(byUser: true);

		Assert.Equal(["text-code"], SavedPanels());
	}

	[Fact]
	public void ThePinnedPanel_IsSavedWithItsOwnLayout_AndFollowsItsChanges_TheGroupPanelHasNone()
	{
		OpenFile("a.md");
		var panel = Panel("text-code");
		panel.CurrentLayout = new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 300 };
		panel.Pin();

		Assert.Equal(300, _settings.OpenPanels.Single().Layout!.Size);
		Assert.Equal(PreviewDockSide.Left, _settings.OpenPanels.Single().Layout!.Side);

		panel.ChangeLayout(new PreviewDockLayoutModel { Side = PreviewDockSide.Bottom, Size = 222, Floating = false });
		Assert.Equal(PreviewDockSide.Bottom, _settings.OpenPanels.Single().Layout!.Side);

		OpenFile("b.md");   // the group's new panel: the group's layout is its own, nothing is saved with it
		Assert.Null(_settings.OpenPanels.Single(p => !p.Pinned).Layout);
	}

	[Fact]
	public void ANewItemInAPanel_IsSaved()
	{
		OpenFile("a.png");
		OpenFile("b.png");

		Assert.Equal("b.png", _settings.OpenPanels.Single().Item.FileName);
	}

	[Fact]
	public void ThePanelListAndTheWindowListAreSeparate()
	{
		OpenFile("a.png");

		Assert.Empty(_settings.OpenWindows);
		Assert.Single(_settings.OpenPanels);
	}

	// ---- item 1: what reopens ----

	[Fact]
	public async Task Restart_ReopensGroupPanelsWithTheirItems_FileByRefId_WebAtItsAddress_DefaultPanelToo()
	{
		_client.Files["ref-a.png"] = ("a.png", "image/png", [1]);
		_client.Files["ref-d.xyz"] = ("d.xyz", "text/plain", [1]);

		await RestartAsync(
			SavedPanel("images", File("a.png")),
			SavedPanel("web", Url("https://example.com/page")),
			SavedPanel(DefaultId, File("d.xyz")));

		Assert.Equal(["images", "web", DefaultId], _dock.Created.Select(h => h.GroupId));
		Assert.Equal("ref-a.png", _dock.Created[0].LastTarget!.Handle);
		Assert.Equal("https://example.com/page", _dock.Created[1].Item!.Url);
		Assert.Equal("d.xyz", _dock.Created[2].Item!.FileName);
		Assert.Equal(["images", "web", DefaultId], SavedPanels());
		Assert.Contains(_client.Calls, c => c.StartsWith("file.read:ref-a.png:0:1", StringComparison.Ordinal));   // the file is checked again
	}

	[Fact]
	public async Task Restart_PinnedPanel_ReopensAtItsOwnLayout_AndStaysPinned()
	{
		var layout = new PreviewDockLayoutModel { Side = PreviewDockSide.Top, Size = 250 };

		await RestartAsync(SavedPanel("web", Url("https://example.com/a"), pinned: true, layout));

		var panel = _dock.Created.Single();
		Assert.True(panel.IsPinned);
		Assert.Equal(250, panel.OwnLayout!.Size);
		Assert.Equal(PreviewDockSide.Top, panel.OwnLayout.Side);
		Assert.Equal(["web*"], SavedPanels());
	}

	[Fact]
	public void APinnedPanel_IsSavedWithItsId_AGroupPanelAndTheDefaultPanelHaveNone()
	{
		OpenFile("a.md");
		var panel = Panel("text-code");
		panel.Pin();
		OpenFile("b.md");

		var saved = _settings.OpenPanels.Single(p => p.Pinned);
		Assert.False(string.IsNullOrEmpty(saved.PanelId));
		Assert.Equal(panel.PanelId, saved.PanelId);
		Assert.All(_settings.OpenPanels.Where(p => !p.Pinned), p => Assert.Null(p.PanelId));
	}

	[Fact]
	public void AtExit_AFlushOfAPinnedPanelWhileTheOthersCloseDoesNotShortenTheSavedList()
	{
		OpenFile("a.md");
		Panel("text-code").Pin();
		OpenFile("a.png");
		Panel("images").Pin();
		var second = Panel("images", pinned: true);
		second.CurrentLayout = new PreviewDockLayoutModel { Side = PreviewDockSide.Bottom, Size = 200 };
		second.OnClosing = () => second.ChangeLayout(second.CurrentLayout!);   // a panel flushes its layout while it closes, when the first panel is already gone

		_router.CloseAllForExit();

		Assert.Equal(["text-code*", "images*"], SavedPanels());   // the list is whole, as it was at the start of the exit
	}

	[Fact]
	public async Task Restart_APinnedPanel_KeepsItsSavedId_SoItsSavedNeighboursStillMatch()
	{
		await RestartAsync(
			SavedPanel("web", Url("https://example.com/a"), pinned: true, panelId: "web#pinnedaaa"),
			SavedPanel("web", Url("https://example.com/b"), pinned: true, panelId: "web#pinnedbbb"));

		Assert.Equal(["web#pinnedaaa", "web#pinnedbbb"], _dock.Created.Select(h => h.PanelId));
		Assert.Equal(["web#pinnedaaa", "web#pinnedbbb"], _settings.OpenPanels.Select(p => p.PanelId));   // saved again, unchanged
	}

	[Fact]
	public async Task Restart_APinnedPanelSavedWithoutAnId_GetsANewOne()
	{
		await RestartAsync(SavedPanel("web", Url("https://example.com/a"), pinned: true));

		Assert.False(string.IsNullOrEmpty(_dock.Created.Single().PanelId));
		Assert.Equal(_dock.Created.Single().PanelId, _settings.OpenPanels.Single().PanelId);
	}

	[Fact]
	public async Task Restart_ManyPinnedPanelsOfOneGroup_AllReopen()
	{
		await RestartAsync(
			SavedPanel("web", Url("https://example.com/a"), pinned: true),
			SavedPanel("web", Url("https://example.com/b"), pinned: true),
			SavedPanel("web", Url("https://example.com/c")));

		Assert.Equal(3, _dock.Created.Count);
		Assert.Equal(2, _dock.Created.Count(h => h.IsPinned));
	}

	[Fact]
	public async Task Restart_WithExternalWindowsActive_PinnedPanelsAndTheDefaultPanelReopen_GroupPanelsDoNot()
	{
		_monitors.Set(2);   // two monitors and the toggle on: external windows
		_settings.SetWindowsEnabled(true);
		_client.Files["ref-d.xyz"] = ("d.xyz", "text/plain", [1]);

		await RestartAsync(
			SavedPanel("web", Url("https://example.com/a"), pinned: true),
			SavedPanel("web", Url("https://example.com/b")),
			SavedPanel(DefaultId, File("d.xyz")));

		Assert.True(_router.IsActive);
		Assert.Equal(["web*", DefaultId], _dock.Created.Select(h => h.GroupId + (h.IsPinned ? "*" : string.Empty)));
		Assert.Equal(["web*", DefaultId], SavedPanels());
		Assert.Contains(_log.Messages, e => e.Contains("external windows are active"));
	}

	[Fact]
	public async Task Restart_SkipsAndLogs_AFileThatCannotBeReadAgain_AGoneGroup_ANonHttpAddress_AnItemNothingReopens()
	{
		_client.Files["ref-ok.png"] = ("ok.png", "image/png", [1]);
		var nothing = new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "x.png" };   // nothing says how to read it again

		await RestartAsync(
			SavedPanel("images", File("gone.png")),
			SavedPanel("web", Url("file:///C:/secret.txt")),
			SavedPanel("deleted-group", Url("https://example.com")),
			SavedPanel("images", nothing),
			SavedPanel("images", File("ok.png")));

		Assert.Equal("ok.png", _dock.Created.Single().Item!.FileName);
		Assert.Equal(["images"], SavedPanels());
		Assert.Contains(_log.Messages, e => e.Contains("gone.png"));
		Assert.Contains(_log.Messages, e => e.Contains("not http"));
		Assert.Contains(_log.Messages, e => e.Contains("no longer exists"));
		Assert.Contains(_log.Messages, e => e.Contains("nothing says how to read it again"));
	}

	[Fact]
	public async Task Restart_AnAutoCloseGroupPanelOfAnotherSession_IsNotReopened_ThePanelOfTheShownSessionIs()
	{
		_settings.SetAutoClose("web", true);
		_router.OnSessionShown("n1");
		_client.Files["ref-a.md"] = ("a.md", "text/markdown", [1]);

		await RestartAsync(
			SavedPanel("web", Url("https://example.com/a", "n9")),
			SavedPanel("images", File("a.png", "n9")),
			SavedPanel("text-code", File("a.md", "n1")));

		Assert.Equal(["text-code"], _dock.Created.Select(h => h.GroupId));   // images: file unreadable here; web: another session
		Assert.Contains(_log.Messages, e => e.Contains("belongs to another session"));
	}

	[Fact]
	public async Task Restart_APinnedPanelOfAnAutoCloseGroup_Reopens_ItNeverAutoCloses()
	{
		_settings.SetAutoClose("web", true);
		_router.OnSessionShown("n1");

		await RestartAsync(SavedPanel("web", Url("https://example.com/a", "n9"), pinned: true));

		Assert.Single(_dock.Created);
	}

	[Fact]
	public async Task Restart_APanelTheUserClosedBeforeExit_IsNotReopened()
	{
		_client.Files["ref-a.md"] = ("a.md", "text/markdown", [1]);
		OpenFile("a.png");
		OpenFile("a.md");
		Panel("images").RaiseClosed(byUser: true);
		var saved = _settings.OpenPanels.ToArray();

		await RestartAsync(saved);

		Assert.Equal(["text-code"], SavedPanels());   // a.md only; the closed panel never was in the list
	}

	[Fact]
	public async Task Restart_ReopensEachGroupOnce_AndTheDefaultPanelOnce()
	{
		_client.Files["ref-x.xyz"] = ("x.xyz", "text/plain", [1]);
		_client.Files["ref-y.xyz"] = ("y.xyz", "text/plain", [1]);

		await RestartAsync(
			SavedPanel("web", Url("https://example.com/a")),
			SavedPanel("web", Url("https://example.com/b")),
			SavedPanel(DefaultId, File("x.xyz")),
			SavedPanel(DefaultId, File("y.xyz")));

		Assert.Equal(["web", DefaultId], _dock.Created.Select(h => h.GroupId));   // two web panels of one group and two default panels: one slot each
		Assert.Equal("https://example.com/a", _dock.Created[0].Item!.Url);
		Assert.Equal("x.xyz", _dock.Created[1].Item!.FileName);
	}

	[Fact]
	public async Task Restart_AFailingDockFactory_SkipsThePanel_AndKeepsGoing()
	{
		_router.Dispose();
		_settings.SaveOpenPanels([SavedPanel("web", Url("https://example.com/a")), SavedPanel("images", File("a.png"))]);
		_dock.ThrowOnCreate = true;
		_router = NewRouter();

		await _router.RestoreAsync(_client);

		Assert.Empty(_settings.OpenPanels);
	}

	[Fact]
	public async Task Restore_KeepsTheSavedPanelsThatAreNotReachedYet_InTheList()
	{
		_router.Dispose();
		_settings.SaveOpenPanels([SavedPanel("web", Url("https://example.com/a")), SavedPanel("images", File("slow.png"))]);
		_client.Files["ref-slow.png"] = ("slow.png", "image/png", [1]);
		var listWhenTheFirstPanelIsCreated = new List<string>();
		_dock.OnCreate = _ =>
		{
			if (listWhenTheFirstPanelIsCreated.Count == 0)
				listWhenTheFirstPanelIsCreated.AddRange(SavedPanels());
		};
		_router = NewRouter();

		await _router.RestoreAsync(_client);

		Assert.Equal(["web", "images"], listWhenTheFirstPanelIsCreated);   // the one being opened and the one not reached yet are both still saved
		Assert.Equal(["web", "images"], SavedPanels());
	}

	// ---- item 2: mode changes ----

	[Fact]
	public void ExternalWindowsBecomeActive_GroupPanelsClose_PinnedAndDefaultStay_TheListFollows_NothingReopens()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		Panel("text-code").Pin();
		OpenFile("b.md");
		OpenFile("data.xyz");

		_monitors.Set(2);
		_settings.SetWindowsEnabled(true);
		_router.SetEnabled(true);

		Assert.True(_router.IsActive);
		Assert.Equal(["text-code*", DefaultId], _dock.Open.Select(h => h.GroupId + (h.IsPinned ? "*" : string.Empty)));
		Assert.Equal(["text-code*", DefaultId], SavedPanels());

		_router.SetEnabled(false);   // inactive again: nothing reopens by itself
		Assert.Equal(["text-code*", DefaultId], _dock.Open.Select(h => h.GroupId + (h.IsPinned ? "*" : string.Empty)));
		Assert.Equal(["text-code*", DefaultId], SavedPanels());
	}

	[Fact]
	public void ExternalWindowsBecomeInactive_GroupWindowsClose_PinnedWindowsStay_NoPanelAppears()
	{
		_monitors.Set(2);
		_settings.SetWindowsEnabled(true);
		_router.SetEnabled(true);
		_router.TryOpenFile(File("a.png"), new PreviewTarget("ref-a.png", "a.png"), _client);
		_router.TryOpenFile(File("a.md"), new PreviewTarget("ref-a.md", "a.md"), _client);
		_windows.Open.Single(w => w.GroupId == "text-code").Pin();
		Assert.Empty(_dock.Created.Where(h => h.GroupId is "images" or "text-code"));

		_router.SetEnabled(false);

		Assert.Equal(["text-code"], _windows.Open.Select(w => w.GroupId));
		Assert.True(_windows.Open.Single().IsPinned);
		Assert.Empty(_dock.Open.Where(h => h.GroupId is "images" or "text-code"));
		Assert.Equal(["text-code*"], _settings.OpenWindows.Select(w => w.GroupId + (w.Pinned ? "*" : string.Empty)));
	}

	[Fact]
	public void TheGroupPanelsAreAskedToCloseByTheRouter_NotByTheUser_SoTheyKeepTheirLayout()
	{
		OpenFile("a.png");
		var panel = Panel("images");

		_monitors.Set(2);
		_router.SetEnabled(true);

		Assert.Equal(1, panel.CloseCalls);   // Close() is the call that saves the panel's layout before it closes (DockedPreviewPanelHandle)
		Assert.True(panel.IsClosed);
	}

	[Fact]
	public void ExitSavesThePanelListBeforeTheyClose_AndTheClosingDoesNotEmptyIt()
	{
		OpenFile("a.png");
		OpenFile("data.xyz");

		_router.CloseAllForExit();

		Assert.Empty(_dock.Open);
		Assert.Equal(["images", DefaultId], SavedPanels());
	}
}
