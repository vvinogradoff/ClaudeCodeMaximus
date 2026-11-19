using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The restore of the open docked panels (FR.29.22, FR.29.23) against damaged saved data (one bad entry never stops the others or the windows) and against what cannot reopen: an entry skipped
/// for a transient reason stays in the saved list for the next start, one skipped for a permanent reason leaves it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRestoreResilienceTests : IDisposable
{
	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly CapturingLogger<PreviewSettingsService> _settingsLog = new();
	private readonly PreviewSettingsService _settings;
	private readonly FakeMonitorWatcher _monitors = new(1);
	private readonly FakePreviewWindowFactory _windows = new();
	private readonly FakePreviewDockFactory _dock = new();
	private readonly FakeTessynClient _client = new();
	private readonly CapturingLogger<PreviewRouter> _log = new();
	private PreviewRouter? _router;

	public PreviewRestoreResilienceTests()
	{
		_settings = new PreviewSettingsService(_store, _settingsLog);
		_client.Files["ref-a.png"] = ("a.png", "image/png", [1]);
		_client.Files["ref-b.md"] = ("b.md", "text/markdown", [1]);
	}

	public void Dispose() => _router?.Dispose();

	private PreviewRouter NewRouter(bool withDock = true) =>
		_router = new PreviewRouter(_settings, new PreviewGroupMatcher(), _monitors, _windows, new UrlPolicy(), new ImmediateUiDispatcher(), _log, null, withDock ? _dock : null);

	private static PreviewItemModel File(string name) => new() { Kind = PreviewItemKind.File, FileName = name, RefId = "ref-" + name, SourceNodeId = "n1" };

	private static OpenPreviewPanelModel Panel(string group, PreviewItemModel item, bool pinned = false) => new() { GroupId = group, Item = item, Pinned = pinned };

	/// <summary>Writes the panel list as a hand-edited or half-written file could hold it, past the service's own checks.</summary>
	private void WriteRaw(params OpenPreviewPanelModel[] panels) => _store.Update<PreviewDockSettings>(s => s.OpenPanels = panels.ToList());

	private string[] Saved() => _settings.OpenPanels.Select(p => p.GroupId + (p.Pinned ? "*" : string.Empty)).ToArray();

	// ---- finding 1: damaged entries ----

	[Fact]
	public void TheSavedPanels_LeaveOutDamagedEntries_AndWarnOnce()
	{
		WriteRaw(
			Panel("images", null!),
			Panel("images", File("a.png")),
			Panel(string.Empty, File("b.md")),
			Panel("web", new PreviewItemModel { Kind = (PreviewItemKind)9, Url = "https://example.com" }),
			Panel("text-code", File("b.md")));

		Assert.Equal(["images", "text-code"], Saved());
		_ = _settings.OpenPanels;   // read again: still one warning for the run
		Assert.Single(_settingsLog.Messages, m => m.Contains("damaged"));
	}

	[Fact]
	public void TheSavedWindows_LeaveOutDamagedEntries()
	{
		_store.Update<PreviewWindowsSettings>(s => s.OpenWindows =
		[
			new OpenPreviewWindowModel { GroupId = "images", Item = null! },
			new OpenPreviewWindowModel { GroupId = "images", Item = File("a.png") },
		]);

		Assert.Equal(["images"], _settings.OpenWindows.Select(w => w.GroupId));
	}

	[Fact]
	public async Task Restore_ANullItem_DoesNotStopTheOtherPanels_NorTheRestoreItself()
	{
		WriteRaw(Panel("images", null!), Panel("images", File("a.png")), Panel("text-code", File("b.md")));
		var router = NewRouter();

		await router.RestoreAsync(_client);

		Assert.Equal(["images", "text-code"], _dock.Created.Select(h => h.GroupId));
		Assert.Equal(["images", "text-code"], Saved());
	}

	[Fact]
	public async Task Restore_ADamagedList_StillLetsALaterRestoreCallRun_NothingThrows()
	{
		WriteRaw(Panel("images", null!), Panel("nonexistent", File("a.png")), Panel("text-code", File("b.md")));
		var router = NewRouter();

		await router.RestoreAsync(_client);   // no exception: the restore is fire-and-forget in the app

		Assert.Equal(["text-code"], _dock.Created.Select(h => h.GroupId));
	}

	// ---- finding 3: transient reasons stay, permanent ones go ----

	[Fact]
	public async Task Restore_ADaemonErrorThatIsNotARefusal_KeepsTheEntryForTheNextStart()
	{
		WriteRaw(Panel("images", File("a.png")), Panel("text-code", File("b.md")));
		_client.FileReadHandler = (handle, _, _) => handle == "ref-a.png" ? throw new IOException("the daemon is not reachable") : new FileReadResult("b.md", "text/markdown", 1, DateTimeOffset.UnixEpoch, 0, 1, "utf8", "x", true);
		var router = NewRouter();

		await router.RestoreAsync(_client);

		Assert.Equal(["text-code"], _dock.Created.Select(h => h.GroupId));
		Assert.Equal(["images", "text-code"], Saved().OrderBy(g => g, StringComparer.Ordinal).ToArray());
		Assert.Contains(Saved(), g => g == "images");   // kept
		Assert.Equal("a.png", _settings.OpenPanels.Single(p => p.GroupId == "images").Item.FileName);
	}

	[Fact]
	public async Task Restore_ARefusedOrMissingFile_DropsTheEntry()
	{
		WriteRaw(Panel("images", File("gone.png")), Panel("text-code", File("b.md")));   // ref-gone.png: the daemon answers with an error
		var router = NewRouter();

		await router.RestoreAsync(_client);

		Assert.Equal(["text-code"], Saved());
	}

	[Fact]
	public async Task Restore_WithoutADockingArea_KeepsEveryPanelForTheNextStart()
	{
		WriteRaw(Panel("images", File("a.png")), Panel("text-code", File("b.md"), pinned: true));
		var router = NewRouter(withDock: false);

		await router.RestoreAsync(_client);

		Assert.Equal(["images", "text-code*"], Saved());
	}

	[Fact]
	public async Task Restore_AGroupDeleted_DropsTheEntry()
	{
		WriteRaw(Panel("deleted-group", File("a.png")), Panel("images", File("a.png")));
		var router = NewRouter();

		await router.RestoreAsync(_client);

		Assert.Equal(["images"], Saved());
	}

	[Fact]
	public async Task Restore_GroupPanelsWhileExternalWindowsAreActive_AreDropped_NothingReopensByItself()
	{
		_monitors.Set(2);
		_settings.SetWindowsEnabled(true);
		WriteRaw(Panel("images", File("a.png")), Panel("text-code", File("b.md"), pinned: true));
		var router = NewRouter();

		await router.RestoreAsync(_client);

		Assert.Equal(["text-code*"], Saved());
	}

	[Fact]
	public async Task Restore_AnAutoCloseGroupOfAnotherSession_IsDropped()
	{
		var groups = _settings.Groups.Select(g => g.Id == "images" ? new PreviewGroupModel { Id = g.Id, Name = g.Name, Patterns = g.Patterns, AutoClose = true } : g).ToList();
		_settings.SaveGroups(groups);
		var other = File("a.png");
		other.SourceNodeId = "another-session";
		WriteRaw(Panel("images", other), Panel("text-code", File("b.md")));
		var router = NewRouter();
		router.OnSessionShown("n1");

		await router.RestoreAsync(_client);

		Assert.Equal(["text-code"], Saved());
	}

	[Fact]
	public async Task AKeptEntry_StaysInTheList_WhenAnotherPanelIsOpenedLater()
	{
		WriteRaw(Panel("images", File("a.png")));
		_client.FileReadHandler = (_, _, _) => throw new TimeoutException("slow daemon");
		var router = NewRouter();
		await router.RestoreAsync(_client);
		_client.FileReadHandler = null;
		_client.Files["ref-b.md"] = ("b.md", "text/markdown", [1]);

		router.TryOpenFile(File("b.md"), new PreviewTarget("ref-b.md", "b.md"), _client);

		Assert.Equal(["text-code", "images"], Saved().OrderByDescending(g => g == "text-code").ToArray());
	}
}
