using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.23.18 and FR.26.5 on the session pane: entry references open the target session at the entry and highlight the quote, Back
/// returns across sessions, file references and local-path chips open the preview pane, URL references go through the URL policy.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionPaneNavigationTests : IDisposable
{
	private const string ChipPath = @"C:\work\plan.md";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly FakePreviewSurface _previewSurface = new();
	private readonly FakeUrlLauncher _urls = new();
	private readonly TempAppPaths _temp = new();
	private readonly SessionPaneViewModel _pane;

	public SessionPaneNavigationTests()
	{
		var ui = new ImmediateUiDispatcher();
		var policy = new ExternalOpenPolicy();
		var store = new PreviewFileStore(_temp.Paths);
		var preview = new FilePreviewViewModel(
			new FilePreviewLoader(_client, new FileKindClassifier(), store, policy), _previewSurface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeExternalFileLauncher(),
			policy, store, _urls, ui, NullLogger<FilePreviewViewModel>.Instance);
		_pane = new SessionPaneViewModel(
			_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), _urls, ui, new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance),
			NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(_client, ui, NullLogger<FocusViewModel>.Instance), preview, new MediaPreviewPolicy());
	}

	public void Dispose()
	{
		_pane.Dispose();
		_temp.Dispose();
	}

	private FakeSession AddSession(string id, int count)
	{
		var session = FakeSession.WithEntries(count, "Session " + id);
		session.Entries = [.. session.Entries.Select(e => e with { EntryId = $"f{id}:{e.Index}", Content = $"entry {e.Index} has some quoted text inside" })];
		_client.Sessions[id] = session;
		return session;
	}

	private static AttentionRefDto EntryRef(string nodeId, int index, int offset = 6, int length = 4) =>
		new() { Kind = "entry", NodeId = nodeId, EntryId = $"f{nodeId}:{index}", Offset = offset, Length = length };

	[Fact]
	public async Task EntryRef_InTheOpenSession_ScrollsToItAndHighlightsTheQuote_AndBackReturns()
	{
		AddSession("a", 50);
		await _pane.OpenAsync("a");
		_surface.RaiseViewChanged(10, atBottom: false);
		Assert.False(_pane.CanGoBack);

		await _pane.JumpToEntryAsync(EntryRef("a", 30));

		Assert.Equal("goto:30:" + "30 h" + ":hl", _surface.Calls[^1]);       // offset 6, length 4 of "entry 30 has ..."
		Assert.True(_pane.CanGoBack);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("session.entries.query", StringComparison.Ordinal)); // already loaded: no fetch

		await _pane.BackCommand.ExecuteAsync(null);

		Assert.Equal("goto:10:-:nohl", _surface.Calls[^1]);                  // scrolled back, nothing highlighted
		Assert.False(_pane.CanGoBack);
	}

	[Fact]
	public async Task Back_FromTheBottom_ReloadsTheViewAtTheBottom()
	{
		AddSession("a", 50);
		await _pane.OpenAsync("a");           // nothing scrolled: the view follows the bottom
		await _pane.JumpToEntryAsync(EntryRef("a", 5));

		await _pane.BackCommand.ExecuteAsync(null);

		Assert.Equal("load:a:50", _surface.Calls[^2]);
		Assert.Equal("live:none", _surface.Calls[^1]);   // the load cleared the page's live row: it is sent again (L6)
		Assert.True(_surface.LastLoadToBottom);
	}

	[Fact]
	public async Task EntryRef_InAnotherSession_OpensItAroundTheEntry_SelectsItInTheTree_AndBackWorksAcrossSessions()
	{
		AddSession("a", 50);
		AddSession("b", 300);
		var navigated = new List<string>();
		_pane.Navigated += (_, id) => navigated.Add(id);
		await _pane.OpenAsync("a");
		_surface.RaiseViewChanged(20, atBottom: false);

		await _pane.JumpToEntryAsync(EntryRef("b", 120));
		await _pane.WhenIdleAsync();

		Assert.Contains("session.entries.query:b:around=fb:120", _client.Calls);
		Assert.Equal("b", _pane.NodeId);
		Assert.Equal("b", _surface.LoadedKey);
		Assert.False(_surface.LastLoadToBottom);
		Assert.Contains(_surface.Calls, c => c.StartsWith("goto:120:", StringComparison.Ordinal) && c.EndsWith(":hl", StringComparison.Ordinal));
		Assert.Equal(["b"], navigated);
		Assert.Contains(120, _surface.PageIndexes);
		Assert.Equal(169, _surface.PageIndexes[^1]);         // only the around page: newer entries load on demand (V4)
		Assert.True(_surface.HasNewer);

		await _pane.BackCommand.ExecuteAsync(null);

		Assert.Equal("a", _pane.NodeId);
		Assert.Equal(["b", "a"], navigated);
		Assert.Equal("goto:20:-:nohl", _surface.Calls[^1]);   // the warm cache holds session a: scrolled back to the entry that was on top
		Assert.False(_pane.CanGoBack);
	}

	[Fact]
	public async Task BackStack_HoldsSeveralPlaces_InOrder()
	{
		AddSession("a", 50);
		AddSession("b", 50);
		await _pane.OpenAsync("a");
		_surface.RaiseViewChanged(3, atBottom: false);
		await _pane.JumpToEntryAsync(EntryRef("b", 10));
		_surface.RaiseViewChanged(8, atBottom: false);
		await _pane.JumpToEntryAsync(EntryRef("a", 40));
		_surface.RaiseViewChanged(39, atBottom: false);

		await _pane.BackCommand.ExecuteAsync(null);
		Assert.Equal("b", _pane.NodeId);
		Assert.Equal("goto:8:-:nohl", _surface.Calls[^1]);

		await _pane.BackCommand.ExecuteAsync(null);
		Assert.Equal("a", _pane.NodeId);
		Assert.Equal("goto:3:-:nohl", _surface.Calls[^1]);
		Assert.False(_pane.CanGoBack);
	}

	[Fact]
	public async Task EntryRef_ToAnEntryThatIsGone_ShowsANoticeAndChangesNothing()
	{
		AddSession("a", 20);
		AddSession("b", 20);
		await _pane.OpenAsync("a");

		await _pane.JumpToEntryAsync(new AttentionRefDto { Kind = "entry", NodeId = "b", EntryId = "fb:9999" });

		Assert.Equal(Constants.Texts.EntryGone, _pane.Notice);
		Assert.Equal("a", _pane.NodeId);
		Assert.False(_pane.CanGoBack);
		_pane.DismissNoticeCommand.Execute(null);
		Assert.Null(_pane.Notice);
	}

	[Fact]
	public async Task EntryRef_ToAnUnknownSession_ShowsANotice_WithoutBreakingThePane()
	{
		AddSession("a", 20);
		await _pane.OpenAsync("a");

		await _pane.JumpToEntryAsync(new AttentionRefDto { Kind = "entry", NodeId = "ghost", EntryId = "fghost:1" });

		Assert.StartsWith("Could not open the referenced entry", _pane.Notice);
		Assert.Equal("a", _pane.NodeId);
		Assert.False(_pane.CanGoBack);
	}

	[Fact]
	public async Task FocusItemEntryRef_JumpsThroughTheFocusView()
	{
		AddSession("a", 40);
		_client.Attention["a"] =
		[
			new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Which?", CreatedUtc = DateTimeOffset.UnixEpoch, Refs = [EntryRef("a", 12)] },
		];
		await _pane.OpenAsync("a");
		await _pane.Focus.WhenIdleAsync();
		Assert.True(_pane.HasSideContent);
		Assert.True(_pane.IsSidePaneVisible);
		Assert.Equal(1, _pane.FocusBadge);

		_pane.Focus.Items.Single().Refs.Single().OpenCommand.Execute(null);
		await Task.Yield();

		Assert.Contains(_surface.Calls, c => c.StartsWith("goto:12:", StringComparison.Ordinal));
		Assert.True(_pane.CanGoBack);
	}

	[Fact]
	public async Task FileRef_OpensThePreviewPaneWithTheRenderedMarkdown()
	{
		AddSession("a", 5);
		_client.Files["ref1"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# The plan\n\n- step one"));
		await _pane.OpenAsync("a");
		_pane.ToggleSideCommand.Execute(null);
		Assert.False(_pane.IsSidePaneVisible);

		await _pane.ActivateRefAsync(new AttentionRefDto { Kind = "file", RefId = "ref1", Name = "plan.md", Path = "docs/plan.md" });

		Assert.True(_pane.Preview!.IsOpen);
		Assert.Equal("plan.md", _pane.Preview.Title);
		Assert.Contains("<h1>The plan</h1>", _previewSurface.LastHtml);
		Assert.True(_pane.IsSidePaneVisible);          // opening a preview shows the side pane again
		Assert.Contains("file.read:ref1:0:" + Constants.Limits.PreviewChunkBytes, _client.Calls);
	}

	[Fact]
	public async Task UrlRef_OpensThroughTheUrlPolicy_HttpAndHttpsOnly()
	{
		AddSession("a", 5);
		await _pane.OpenAsync("a");

		await _pane.ActivateRefAsync(new AttentionRefDto { Kind = "url", Url = "https://github.com/org/repo/pull/1" });
		Assert.Equal(["https://github.com/org/repo/pull/1"], _urls.Opened);
		Assert.Null(_pane.Notice);

		await _pane.ActivateRefAsync(new AttentionRefDto { Kind = "url", Url = "file:///C:/Windows/System32/cmd.exe" });
		await _pane.ActivateRefAsync(new AttentionRefDto { Kind = "url", Url = "javascript:alert(1)" });

		Assert.Single(_urls.Opened);
		Assert.NotNull(_pane.Notice);
	}

	[Fact]
	public async Task LocalPathChip_AsksTheDaemonForAHandle_ThenOpensThePreview()
	{
		var session = AddSession("a", 10);
		session.Entries[3] = session.Entries[3] with { Content = $"The plan is at {ChipPath} now" };
		_client.Files["h1"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# Chip plan"));
		_client.PreviewHandles[$"a|fa:3|{ChipPath}"] = "h1";
		await _pane.OpenAsync("a");

		_surface.RaisePathClicked(3, ChipPath);
		await _pane.Preview!.WhenIdleAsync();

		Assert.Contains($"file.preview:a:fa:3:{ChipPath}", _client.Calls);
		Assert.True(_pane.Preview.IsOpen);
		Assert.Contains("<h1>Chip plan</h1>", _previewSurface.LastHtml);
		Assert.Null(_pane.Notice);
	}

	[Fact]
	public async Task LocalPathChip_WhenTheDaemonRefuses_ShowsANonBlockingMessage()
	{
		AddSession("a", 10);
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, ChipPath);       // no handle is issued: "The path does not occur in this entry"

		Assert.StartsWith("Cannot preview this file", _pane.Notice);
		Assert.Contains("does not occur", _pane.Notice);
		Assert.False(_pane.Preview!.IsOpen);
		Assert.True(_pane.HasSession);
	}

	[Theory]
	[InlineData(@"C:\Windows\System32")]
	[InlineData(@"relative\file.md")]
	[InlineData("C:\\a.md\nC:\\b.md")]
	public async Task LocalPathChip_WithAPathThatIsNotPathShaped_NeverReachesTheDaemon(string path)
	{
		AddSession("a", 10);
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, path);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("file.preview", StringComparison.Ordinal));
		Assert.NotNull(_pane.Notice);
	}

	[Fact]
	public async Task LocalPathChip_ForAnEntryWithoutAnId_ShowsAMessage()
	{
		var session = AddSession("a", 10);
		session.Entries[3] = session.Entries[3] with { EntryId = null };
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, ChipPath);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("file.preview", StringComparison.Ordinal));
		Assert.NotNull(_pane.Notice);
	}

	[Fact]
	public async Task SelectingAnotherSessionInTheTree_StartsAFreshTrail_AndClosesThePreview()
	{
		AddSession("a", 30);
		AddSession("b", 30);
		_client.Files["h"] = ("a.txt", "text/plain", System.Text.Encoding.UTF8.GetBytes("x"));
		await _pane.OpenAsync("a");
		await _pane.JumpToEntryAsync(EntryRef("a", 20));
		await _pane.Preview!.OpenAsync(new PreviewTarget("h", "a.txt"));
		Assert.True(_pane.CanGoBack);
		Assert.True(_pane.Preview.IsOpen);

		await _pane.OpenAsync("b");

		Assert.False(_pane.CanGoBack);
		Assert.False(_pane.Preview.IsOpen);
	}

	[Fact]
	public async Task OpeningTheSessionThatIsAlreadyOpen_DoesNotDisturbTheTrail_SoTheTreeCanFollowANavigation()
	{
		AddSession("a", 30);
		AddSession("b", 30);
		await _pane.OpenAsync("a");
		await _pane.JumpToEntryAsync(EntryRef("b", 5));
		var calls = _client.Calls.Count;

		await _pane.OpenAsync("b");      // what the tree does after Navigated selected the node

		Assert.Equal(calls, _client.Calls.Count);
		Assert.True(_pane.CanGoBack);
	}

	[Fact]
	public async Task AJump_IntoALongSession_LoadsOnlyTheAroundPage_AndNewerPagesComeOnDemand()
	{
		AddSession("a", 20);
		AddSession("long", 20_000);
		await _pane.OpenAsync("a");
		_client.Calls.Clear();

		await _pane.JumpToEntryAsync(EntryRef("long", 100));
		await _pane.WhenIdleAsync();

		Assert.False(_pane.IsLoading);                                                        // no catch-up behind the spinner
		Assert.Single(_client.Calls, c => c.StartsWith("session.entries.query:long", StringComparison.Ordinal));
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("session.entries:long:before=-:after=", StringComparison.Ordinal) && !c.Contains("after=-", StringComparison.Ordinal));
		Assert.True(_surface.HasNewer);
		Assert.Equal(149, _surface.PageIndexes[^1]);

		for (var page = 1; page <= 3; page++)
		{
			_surface.RaiseNewerRequested();
			await _pane.WhenIdleAsync();
			Assert.Equal(149 + page * 100, _surface.PageIndexes[^1]);                          // exactly one page per request
			Assert.True(_surface.HasNewer);
		}

		Assert.Equal(3, _client.Calls.Count(c => c.StartsWith("session.entries:long:before=-:after=", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task OnDemandPaging_EndsAtTheNewestEntry_AndLiveAppendsDoNotChaseTheTailMeanwhile()
	{
		AddSession("a", 20);
		var session = AddSession("b", 260);
		await _pane.OpenAsync("a");
		await _pane.JumpToEntryAsync(EntryRef("b", 100));         // around page 50..149
		session.Entries.Add(session.Entries[^1] with { Index = 260, EntryId = "fb:260" });
		_client.RaiseNotification(RemoteProtocol.Events.SessionAppended, new SessionEpochPayload("b", 1));
		await _pane.WhenIdleAsync();

		Assert.Equal(149, _surface.PageIndexes[^1]);                // a live append did not load the whole gap

		_surface.RaiseNewerRequested();
		await _pane.WhenIdleAsync();
		_surface.RaiseNewerRequested();
		await _pane.WhenIdleAsync();

		Assert.Equal(260, _surface.PageIndexes[^1]);
		Assert.False(_surface.HasNewer);
	}

	[Fact]
	public async Task AFailingNewerPage_IsNotRetriedByItself_UntilTheUserScrollsOrPressesRetry()
	{
		AddSession("a", 20);
		AddSession("b", 600);
		await _pane.OpenAsync("a");
		await _pane.JumpToEntryAsync(EntryRef("b", 100));
		_client.AfterIndexException = new InvalidOperationException("daemon down");
		_client.Calls.Clear();

		_surface.RaiseNewerRequested();
		await _pane.WhenIdleAsync();

		Assert.True(_pane.NewerFailed);
		Assert.Equal("newerFailed", _surface.Calls[^1]);                 // tells the page not to ask again by itself
		Assert.Single(_client.Calls, c => c.Contains("after=", StringComparison.Ordinal) && !c.Contains("after=-", StringComparison.Ordinal));
		Assert.DoesNotContain(_surface.Calls, c => c.StartsWith("append:", StringComparison.Ordinal));   // no Append that would re-arm the page

		await _pane.WhenIdleAsync();
		Assert.Single(_client.Calls, c => c.Contains("after=", StringComparison.Ordinal) && !c.Contains("after=-", StringComparison.Ordinal));   // still one

		_pane.RetryNewerCommand.Execute(null);                            // Retry asks at once, and fails again without looping
		await _pane.WhenIdleAsync();
		Assert.Equal(2, _client.Calls.Count(c => c.Contains("after=", StringComparison.Ordinal) && !c.Contains("after=-", StringComparison.Ordinal)));
		Assert.True(_pane.NewerFailed);

		_client.AfterIndexException = null;
		_surface.RaiseNewerRequested();                                   // the user scrolled again
		await _pane.WhenIdleAsync();
		Assert.False(_pane.NewerFailed);
		Assert.Equal(249, _surface.PageIndexes[^1]);
	}

	[Fact]
	public async Task AnAppendArrivingDuringTheLastOnDemandPage_IsPickedUpOnce_WhenThePageMakesTheViewCurrent()
	{
		AddSession("a", 20);
		var session = AddSession("b", 260);
		await _pane.OpenAsync("a");
		await _pane.JumpToEntryAsync(EntryRef("b", 100));         // 50..149
		_surface.RaiseNewerRequested();
		await _pane.WhenIdleAsync();                              // 150..249, more remain
		_client.AfterPageHook = () =>                             // while the last page (250..259) is on its way an entry is appended
		{
			session.Entries.Add(session.Entries[^1] with { Index = 260, EntryId = "fb:260" });
			_client.RaiseNotification(RemoteProtocol.Events.SessionAppended, new SessionEpochPayload("b", 1));   // ignored: the view still stops short
		};
		_client.Calls.Clear();

		_surface.RaiseNewerRequested();
		await _pane.WhenIdleAsync();

		Assert.Equal(260, _surface.PageIndexes[^1]);
		Assert.False(_surface.HasNewer);
		Assert.Equal(2, _client.Calls.Count(c => c.StartsWith("session.entries:b:before=-:after=", StringComparison.Ordinal)));   // the page, then exactly one catch-up
	}

	[Fact]
	public async Task Back_ToTheBottomOfASessionOpenedByAJump_ShowsTheNewestEntries()
	{
		AddSession("long", 1000);
		await _pane.OpenAsync("long");                              // the bottom view
		await _pane.JumpToEntryAsync(EntryRef("long", 5));          // loads around entry 5, which is far from the end
		Assert.True(_surface.HasNewer);

		await _pane.BackCommand.ExecuteAsync(null);

		Assert.Equal(999, _surface.PageIndexes[^1]);
		Assert.False(_surface.HasNewer);
		Assert.True(_surface.LastLoadToBottom);
	}

	[Fact]
	public async Task AJumpSupersededByATreeClick_RaisesNothing_AndLeavesTheTrailAlone()
	{
		AddSession("a", 40);
		AddSession("b", 40);
		AddSession("c", 40);
		var navigated = new List<string>();
		_pane.Navigated += (_, id) => navigated.Add(id);
		await _pane.OpenAsync("a");
		_client.AroundGate = new TaskCompletionSource();

		var jump = _pane.JumpToEntryAsync(EntryRef("b", 20));        // still loading the around page
		await _pane.OpenAsync("c");                                  // the user clicks another session in the tree
		_client.AroundGate.SetResult();
		await jump;

		Assert.Empty(navigated);
		Assert.Equal("c", _pane.NodeId);
		Assert.False(_pane.CanGoBack);
		Assert.Equal("c", _surface.LoadedKey);
	}

	[Fact]
	public async Task AJumpThatFails_RaisesNothing_AndPushesNothing()
	{
		AddSession("a", 40);
		AddSession("b", 40);
		var navigated = new List<string>();
		_pane.Navigated += (_, id) => navigated.Add(id);
		await _pane.OpenAsync("a");
		_client.AroundException = new InvalidOperationException("daemon error");

		await _pane.JumpToEntryAsync(EntryRef("b", 20));

		Assert.Empty(navigated);
		Assert.Equal("a", _pane.NodeId);
		Assert.False(_pane.CanGoBack);
		Assert.Contains("daemon error", _pane.Notice);
	}

	[Fact]
	public async Task NavigatedIsRaisedOnce_RightAfterThePageGotTheEntries()
	{
		AddSession("a", 40);
		AddSession("b", 400);
		var seen = new List<string>();
		_pane.Navigated += (_, id) => seen.Add(id + ":" + _surface.LoadedKey + ":" + _surface.Calls[^1].Split(':')[0]);
		await _pane.OpenAsync("a");

		await _pane.JumpToEntryAsync(EntryRef("b", 200));

		Assert.Equal(["b:b:goto"], seen);        // the surface was loaded and scrolled before the announcement; no catch-up preceded it
	}

	[Fact]
	public async Task OpeningASession_ShowsItsFocusItems_AndASideContentFlag()
	{
		AddSession("a", 5);
		AddSession("b", 5);
		_client.Attention["a"] = [new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Todo, Status = AttentionStatus.Open, Title = "t", CreatedUtc = DateTimeOffset.UnixEpoch }];

		await _pane.OpenAsync("a");
		await _pane.Focus.WhenIdleAsync();
		Assert.Single(_pane.Focus.Items);
		Assert.True(_pane.HasSideContent);

		await _pane.OpenAsync("b");
		await _pane.Focus.WhenIdleAsync();
		Assert.Empty(_pane.Focus.Items);
		Assert.False(_pane.HasSideContent);
		Assert.False(_pane.IsSidePaneVisible);
	}
}
