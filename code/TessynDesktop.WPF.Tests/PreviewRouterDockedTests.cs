using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The preview router docked target (FR.29.17–FR.29.19, FR.29.23): group panels, the default Preview panel, Pin, Auto-close and the mode changes, over fake panels.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRouterDockedTests : IDisposable
{
	private const string DefaultId = Constants.Previews.DefaultPanelId;

	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly PreviewSettingsService _settings;
	private readonly FakeMonitorWatcher _monitors = new(1);   // one monitor: docked mode
	private readonly FakePreviewWindowFactory _windows = new();
	private readonly FakePreviewDockFactory _dock = new();
	private readonly FakeTessynClient _client = new();
	private readonly PreviewRouter _router;

	public PreviewRouterDockedTests()
	{
		_settings = new PreviewSettingsService(_store);
		_router = NewRouter(_dock);
	}

	public void Dispose() => _router.Dispose();

	private PreviewRouter NewRouter(IPreviewDockFactory? dock) =>
		new(_settings, new PreviewGroupMatcher(), _monitors, _windows, new UrlPolicy(), new ImmediateUiDispatcher(), NullLogger<PreviewRouter>.Instance, null, dock);

	private static PreviewItemModel File(string name, string? node = "n1", string? focusItem = null) =>
		new() { Kind = PreviewItemKind.File, FileName = name, RefId = "ref-" + name, SourceNodeId = node, SourceFocusItemId = focusItem };

	private static PreviewItemModel Url(string? url, string? node = "n1") => new() { Kind = PreviewItemKind.Url, Url = url, SourceNodeId = node };

	private bool OpenFile(string name, string? node = "n1", string? focusItem = null) =>
		_router.TryOpenFile(File(name, node, focusItem), new PreviewTarget("ref-" + name, name), _client);

	private FakePreviewWindowHandle Panel(string id, bool pinned = false) => _dock.Open.Single(h => h.GroupId == id && h.IsPinned == pinned);

	private string[] OpenPanels() => _dock.Open.Select(h => h.GroupId + (h.IsPinned ? "*" : string.Empty)).ToArray();

	[Fact]
	public void DockedMode_IsNotActive_AndFileGoesToTheGroupPanel_NotAWindow()
	{
		Assert.False(_router.IsActive);
		Assert.True(OpenFile("a.png"));

		Assert.Equal("a.png", Panel("images").Item!.FileName);
		Assert.Empty(_windows.Created);
		Assert.Empty(_settings.OpenWindows);   // panels are not in the saved window list (stage 2)
	}

	[Fact]
	public void SameGroup_ReplacesInItsPanel_TwoGroups_TwoPanels()
	{
		OpenFile("a.png");
		OpenFile("b.png");
		OpenFile("a.md");
		Assert.True(_router.TryOpenUrl(Url("https://example.com/x")));

		Assert.Equal(["images", "text-code", "web"], OpenPanels());
		Assert.Equal(["a.png", "b.png"], Panel("images").Shown.Select(i => i.FileName));
	}

	[Fact]
	public void FileWithNoGroup_GoesToTheDefaultPanel_OneReused()
	{
		Assert.True(OpenFile("data.xyz"));
		Assert.True(OpenFile("other.bin"));

		var panel = _dock.Open.Single();
		Assert.Equal(DefaultId, panel.GroupId);
		Assert.Equal(["data.xyz", "other.bin"], panel.Shown.Select(i => i.FileName));
	}

	[Fact]
	public void VideoWithNoGroup_AndUrlWithNoGroup_AreNotRouted()
	{
		_settings.SaveGroups(_settings.Groups.Where(g => g.Id != "web").ToList());

		Assert.False(OpenFile("clip.mp4"));
		Assert.False(_router.TryOpenUrl(Url("https://example.com/x")));
		Assert.Empty(_dock.Created);
	}

	[Fact]
	public void WouldRouteFile_FollowsTheSameRules()
	{
		Assert.True(_router.WouldRouteFile("a.png"));
		Assert.True(_router.WouldRouteFile("data.xyz"));   // the default panel
		Assert.False(_router.WouldRouteFile("clip.MP4"));
		Assert.False(_router.WouldRouteFile(null));
	}

	[Fact]
	public void WithoutADockFactory_NothingRoutes_AsBefore()
	{
		using var plain = NewRouter(null);

		Assert.False(plain.WouldRouteFile("a.png"));
		Assert.False(plain.TryOpenFile(File("a.png"), new PreviewTarget("r", "a.png"), _client));
		Assert.False(plain.TryOpenUrl(Url("https://example.com/x")));
		Assert.Empty(_dock.Created);
	}

	[Fact]
	public void Pin_LeavesTheGroupSlot_NextItemOpensANewPanel()
	{
		OpenFile("a.png");
		Panel("images").Pin();
		OpenFile("b.png");

		Assert.Equal(2, _dock.Open.Count());
		Assert.Equal(["a.png"], Panel("images", pinned: true).Shown.Select(i => i.FileName));
		Assert.Equal(["b.png"], Panel("images").Shown.Select(i => i.FileName));
	}

	[Fact]
	public void DefaultPanel_CannotBePinned()
	{
		OpenFile("data.xyz");
		var panel = Panel(DefaultId);
		panel.Pin();
		OpenFile("more.xyz");

		Assert.Single(_dock.Created);   // still the one default panel
		Assert.Equal(2, panel.Shown.Count);
	}

	[Fact]
	public void Rule1_AnotherSession_ClosesAutoCloseGroupPanels_NotPinnedNotDefault()
	{
		_settings.SetAutoClose("images", true);
		_settings.SetAutoClose("text-code", true);
		OpenFile("a.png", node: "n1");
		OpenFile("a.md", node: "n1");
		Panel("text-code").Pin();
		OpenFile("b.md", node: "n1");
		OpenFile("data.xyz", node: "n1");

		_router.OnSessionShown("n2");

		Assert.Equal(["text-code*", DefaultId], OpenPanels());
	}

	[Fact]
	public void Rule1_SameSession_KeepsThePanel()
	{
		_settings.SetAutoClose("images", true);
		OpenFile("a.png", node: "n1");

		_router.OnSessionShown("n1");

		Assert.Single(_dock.Open);
	}

	[Fact]
	public void Rule2_ClosedFocusItem_ClosesItsGroupPanel_OnlyInAutoCloseGroups()
	{
		_settings.SetAutoClose("images", true);
		OpenFile("a.png", focusItem: "f1");
		OpenFile("a.md", focusItem: "f1");
		OpenFile("data.xyz", focusItem: "f1");

		_router.OnFocusItemsClosed("n1", ["f1"]);

		Assert.Equal(["text-code", DefaultId], OpenPanels());
	}

	[Fact]
	public void GroupRemoved_ClosesItsPanel_DefaultAndPinnedStay()
	{
		OpenFile("a.png");
		Panel("images").Pin();
		OpenFile("b.png");
		OpenFile("data.xyz");

		_settings.SaveGroups(_settings.Groups.Where(g => g.Id != "images").ToList());

		Assert.Equal(["images*", DefaultId], OpenPanels());
		Assert.True(_dock.Created[1].IsClosed);
	}

	[Fact]
	public void GroupRenamed_TellsItsPanel_DefaultPanelUntouched()
	{
		OpenFile("a.png");
		OpenFile("data.xyz");

		var groups = _settings.Groups.ToList();
		groups.Single(g => g.Id == "images").Name = "Pictures";
		_settings.SaveGroups(groups);

		Assert.Equal("Pictures", Panel("images").GroupName);
		Assert.Equal(Constants.Previews.DefaultPanelName, Panel(DefaultId).GroupName);
	}

	[Fact]
	public void ModeOn_ClosesGroupAndDefaultPanels_PinnedPanelsStay_NothingReopens()
	{
		OpenFile("a.png");
		OpenFile("a.md");
		Panel("text-code").Pin();
		OpenFile("b.md");
		OpenFile("data.xyz");

		_monitors.Set(2);

		Assert.True(_router.IsActive);
		Assert.Equal(["text-code*"], OpenPanels());
		Assert.Empty(_windows.Created);
	}

	[Fact]
	public void ModeOff_ClosesGroupWindows_PinnedWindowsStay_AndTheNextItemGoesToAPanel()
	{
		_monitors.Set(2);
		OpenFile("a.png");
		OpenFile("a.md");
		_windows.Open.Single(w => w.GroupId == "text-code").Pin();

		_monitors.Set(1);

		Assert.Equal(["text-code"], _windows.Open.Select(h => h.GroupId));
		Assert.Empty(_dock.Created);   // nothing reopens by itself
		OpenFile("b.png");
		Assert.Equal(["images"], OpenPanels());
	}

	[Fact]
	public void ActiveMode_UsesWindows_NotPanels()
	{
		_monitors.Set(2);

		Assert.True(OpenFile("a.png"));
		Assert.False(OpenFile("data.xyz"));   // no group: not routed, as before
		Assert.Single(_windows.Open);
		Assert.Empty(_dock.Created);
	}

	[Fact]
	public void CloseAllForExit_ClosesPanelsToo()
	{
		OpenFile("a.png");
		Panel("images").Pin();
		OpenFile("b.png");
		OpenFile("data.xyz");

		_router.CloseAllForExit();

		Assert.Empty(_dock.Open);
	}
}
