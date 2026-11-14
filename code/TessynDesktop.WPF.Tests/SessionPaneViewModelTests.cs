using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Selection to paging, appends, reset and resync reloads, send/stop, tags and find of the session pane.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionPaneViewModelTests
{
	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly FakeUrlLauncher _launcher = new();

	private SessionPaneViewModel CreatePane(int cacheCapacity = Constants.Limits.MaxCachedTranscripts) =>
		new(_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), _launcher, new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, cacheCapacity);

	private FakeSession AddSession(string id, int entries, string name = "Session")
	{
		var session = FakeSession.WithEntries(entries, name);
		_client.Sessions[id] = session;
		return session;
	}

	[Fact]
	public async Task Open_LoadsTheNewestPageOfOneHundredAndScrollsToTheBottom()
	{
		AddSession("a", 250);
		using var pane = CreatePane();

		await pane.OpenAsync("a", "Tree label");

		Assert.Contains("session.entries:a:before=-:after=-:limit=100", _client.Calls);
		Assert.Equal("a", _surface.LoadedKey);
		Assert.Equal(Enumerable.Range(150, 100), _surface.PageIndexes);
		Assert.True(_surface.HasOlder);
		Assert.True(_surface.LastLoadToBottom);
		Assert.Equal("Session", pane.Title);
		Assert.True(pane.HasSession);
		Assert.False(pane.IsLoading);
	}

	[Fact]
	public async Task Open_ShortSessionHasNoOlderPage()
	{
		AddSession("a", 30);
		using var pane = CreatePane();

		await pane.OpenAsync("a");

		Assert.False(_surface.HasOlder);
		Assert.Equal(30, _surface.PageIndexes.Count);
	}

	[Fact]
	public async Task ScrollingToTheTop_LoadsOlderPagesUntilTheStart()
	{
		AddSession("a", 250);
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		_surface.RaiseOlderRequested();
		await pane.WhenIdleAsync();
		Assert.Contains("session.entries:a:before=150:after=-:limit=100", _client.Calls);
		Assert.Equal(Enumerable.Range(50, 200), _surface.PageIndexes);
		Assert.True(_surface.HasOlder);

		_surface.RaiseOlderRequested();
		await pane.WhenIdleAsync();
		Assert.Equal(Enumerable.Range(0, 250), _surface.PageIndexes);
		Assert.False(_surface.HasOlder);

		_surface.RaiseOlderRequested();
		await pane.WhenIdleAsync();
		Assert.Equal(250, _surface.PageIndexes.Count);
	}

	[Fact]
	public async Task SessionAppended_FetchesOnlyEntriesAfterTheLastLoadedOne()
	{
		var session = AddSession("a", 120);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		session.Entries.Add(FakeSession.MakeEntry(120));
		session.Entries.Add(FakeSession.MakeEntry(121));

		_client.RaiseNotification("session.appended", new SessionEpochPayload("a", 1));
		await pane.WhenIdleAsync();

		Assert.Contains("session.entries:a:before=-:after=119:limit=100", _client.Calls);
		Assert.Equal("append:2", _surface.Calls.Last());
		Assert.Equal(121, _surface.PageIndexes.Last());
	}

	[Fact]
	public async Task SessionAppended_ForAnotherSession_IsIgnored()
	{
		AddSession("a", 5);
		AddSession("b", 5);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		var calls = _client.Calls.Count;

		_client.RaiseNotification("session.appended", new SessionEpochPayload("b", 1));
		await pane.WhenIdleAsync();

		Assert.Equal(calls, _client.Calls.Count);
	}

	[Fact]
	public async Task SessionReset_ReloadsTheTailInFull()
	{
		var session = AddSession("a", 150);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		session.Epoch = 2;
		session.Entries = FakeSession.WithEntries(40).Entries;

		_client.RaiseNotification("session.reset", new SessionEpochPayload("a", 2));
		await pane.WhenIdleAsync();

		Assert.Equal(Enumerable.Range(0, 40), _surface.PageIndexes);
		Assert.Equal(2, _surface.Calls.Count(c => c.StartsWith("load:a", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task ClientResync_ReloadsTheCurrentSessionAndDropsTheCache()
	{
		AddSession("a", 10);
		AddSession("b", 10);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		await pane.OpenAsync("b");
		Assert.Equal(2, pane.Cache.Count);

		_client.RaiseResync();
		await pane.WhenIdleAsync();

		Assert.Equal(1, pane.Cache.Count);
		Assert.Equal(3, _surface.Calls.Count(c => c.StartsWith("load:", StringComparison.Ordinal)));
		Assert.Equal("b", _surface.LoadedKey);
	}

	[Fact]
	public async Task EpochChangeFoundWhilePaging_TriggersAFullReload()
	{
		var session = AddSession("a", 250);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		session.Epoch = 5;

		_surface.RaiseOlderRequested();
		await pane.WhenIdleAsync();

		Assert.Equal(2, _surface.Calls.Count(c => c.StartsWith("load:a", StringComparison.Ordinal)));
		Assert.Equal(Enumerable.Range(150, 100), _surface.PageIndexes);
	}

	[Fact]
	public async Task ReturningToACachedSession_ShowsItWithoutRefetchingItsPageAndCatchesUp()
	{
		var a = AddSession("a", 50);
		AddSession("b", 50);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		await pane.OpenAsync("b");
		a.Entries.Add(FakeSession.MakeEntry(50));
		_client.Calls.Clear();

		await pane.OpenAsync("a");

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("session.entries:a:before=-:after=-", StringComparison.Ordinal));
		Assert.Contains("session.entries:a:before=-:after=49:limit=100", _client.Calls);
		Assert.Equal(51, _surface.PageIndexes.Count);
	}

	[Fact]
	public async Task Lru_KeepsAtMostFourSessionsAndEvictsTheLeastRecentlyUsed()
	{
		for (var i = 0; i < 6; i++)
			AddSession("s" + i, 5);
		using var pane = CreatePane();

		for (var i = 0; i < 6; i++)
			await pane.OpenAsync("s" + i);

		Assert.Equal(4, pane.Cache.Count);
		Assert.Equal(["s5", "s4", "s3", "s2"], pane.Cache.NodeIds);
	}

	[Fact]
	public async Task SwitchingQuickly_OnlyTheLastSessionIsShown()
	{
		AddSession("a", 5);
		AddSession("b", 5);
		using var pane = CreatePane();

		var first = pane.OpenAsync("a");
		var second = pane.OpenAsync("b");
		await Task.WhenAll(first, second);

		Assert.Equal("b", pane.NodeId);
		Assert.Equal("b", _surface.LoadedKey);
	}

	[Fact]
	public async Task Send_PostsTheTextAndClearsTheInput()
	{
		AddSession("a", 3);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		pane.InputText = "  hello world  ";
		Assert.True(pane.SendCommand.CanExecute(null));

		await pane.SendCommand.ExecuteAsync(null);

		Assert.Contains("session.send:a:hello world", _client.Calls);
		Assert.Equal(string.Empty, pane.InputText);
		Assert.False(pane.SendCommand.CanExecute(null));
	}

	[Fact]
	public async Task Send_IsDisabledForBlankInputAndWithoutASession()
	{
		using var pane = CreatePane();
		pane.InputText = "text";
		Assert.False(pane.SendCommand.CanExecute(null));

		AddSession("a", 1);
		await pane.OpenAsync("a");
		pane.InputText = "   ";
		Assert.False(pane.SendCommand.CanExecute(null));
	}

	[Fact]
	public async Task Stop_IsEnabledOnlyWhileRunningAndCallsSessionStop()
	{
		var session = AddSession("a", 3);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		Assert.False(pane.StopCommand.CanExecute(null));

		_client.RaiseNotification("session.changed", new SessionChangedPayload(session.State("a") with { Running = true }));
		Assert.True(pane.IsRunning);
		Assert.Equal("Running", pane.StateText);
		await pane.StopCommand.ExecuteAsync(null);

		Assert.Contains("session.stop:a", _client.Calls);
	}

	[Fact]
	public async Task UnsentInput_IsKeptPerSessionWhileSwitching()
	{
		AddSession("a", 2);
		AddSession("b", 2);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		pane.InputText = "draft for a";

		await pane.OpenAsync("b");
		Assert.Equal(string.Empty, pane.InputText);
		await pane.OpenAsync("a");

		Assert.Equal("draft for a", pane.InputText);
	}

	[Fact]
	public async Task Header_ShowsTagChipsThatOpenTheirUrlInTheSystemBrowser()
	{
		var session = AddSession("a", 2);
		session.Tags =
		[
			new SessionTagDto { Kind = SessionTagKind.GitHubPullRequest, Label = "windows-app#196", Url = "https://github.com/tryhardstop/windows-app/pull/196" },
			new SessionTagDto { Kind = SessionTagKind.LinearIssue, Label = "HSE-240", Url = "javascript:alert(1)" },
		];
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		Assert.Equal(["windows-app#196", "HSE-240"], pane.Tags.Select(t => t.Label));
		pane.Tags[0].OpenCommand.Execute(null);
		pane.Tags[1].OpenCommand.Execute(null);

		Assert.Equal(["https://github.com/tryhardstop/windows-app/pull/196"], _launcher.Opened);
	}

	[Fact]
	public async Task LinkClicksFromThePage_OpenOnlyHttpUrls()
	{
		AddSession("a", 2);
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		_surface.RaiseLinkClicked("https://example.com/x");
		_surface.RaiseLinkClicked("file:///c:/windows/system32/calc.exe");
		_surface.RaiseLinkClicked("javascript:alert(1)");

		Assert.Equal(["https://example.com/x"], _launcher.Opened);
	}

	[Fact]
	public async Task ExpandingATruncatedEntry_FetchesItsFullContentAndReplacesIt()
	{
		var session = AddSession("a", 3);
		session.Entries[1] = session.Entries[1] with { Content = "cut", Truncated = true, FullLength = 9000 };
		session.FullEntries[1] = session.Entries[1] with { Content = "the full text", Truncated = null, FullLength = null };
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		_surface.RaiseExpandRequested(1);
		await pane.WhenIdleAsync();

		Assert.Contains("session.entry:a:1", _client.Calls);
		Assert.Contains(_surface.Calls, c => c.StartsWith("replace:1:", StringComparison.Ordinal) && c.Contains("the full text", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Find_SearchesTheLoadedEntriesAndTellsThePage()
	{
		var session = AddSession("a", 6);
		session.Entries[2] = session.Entries[2] with { Content = "Needle in here" };
		session.Entries[4] = session.Entries[4] with { Content = "another NEEDLE" };
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		pane.ShowFindCommand.Execute(null);
		Assert.True(pane.FindBarVisible);

		pane.FindText = "needle";
		await pane.FindCommand.ExecuteAsync(null);
		_surface.RaiseFindStatus(2, 2);

		Assert.Contains("find:needle:2,4", _surface.Calls);
		Assert.Equal("2 of 2", pane.FindStatus);

		pane.CloseFindCommand.Execute(null);
		Assert.False(pane.FindBarVisible);
		Assert.Equal("clearFind", _surface.Calls.Last());
	}

	[Fact]
	public async Task CtrlFInsideThePage_OpensTheFindBar()
	{
		AddSession("a", 2);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		var focused = 0;
		pane.FindFocusRequested += (_, _) => focused++;

		_surface.RaiseFindRequested();

		Assert.True(pane.FindBarVisible);
		Assert.Equal(1, focused);
	}

	[Fact]
	public async Task Selection_NeverCallsTheDaemonsSelectionMethods()
	{
		AddSession("a", 2);
		using var pane = CreatePane();

		await pane.OpenAsync("a");

		Assert.DoesNotContain(_client.Calls, c => c.Contains("etSelected", StringComparison.Ordinal) || c.Contains("etSelection", StringComparison.Ordinal));
	}

	[Fact]
	public async Task OpenFailure_ShowsAnErrorAndStaysUsable()
	{
		using var pane = CreatePane();

		await pane.OpenAsync("missing");

		Assert.NotNull(pane.Error);
		Assert.False(pane.IsLoading);
	}

	[Fact]
	public async Task WarmOpenOfARunningSession_WithAnAppendWhileSessionGetIsPending_HasNoDuplicates()
	{
		var a = AddSession("a", 20);
		AddSession("b", 5);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		await pane.OpenAsync("b");
		a.Entries.Add(FakeSession.MakeEntry(20));
		_client.SessionGetGate = new TaskCompletionSource();

		var open = pane.OpenAsync("a");
		_client.RaiseNotification("session.appended", new SessionEpochPayload("a", 1));
		_client.SessionGetGate.SetResult();
		await open;
		await pane.WhenIdleAsync();

		Assert.Equal(Enumerable.Range(0, 21), _surface.PageIndexes);
		Assert.True(pane.Cache.TryGet("a", out var model));
		Assert.Equal(Enumerable.Range(0, 21), model.Entries.Select(e => e.Index));
	}

	[Fact]
	public async Task AppendArrivingDuringAColdOpen_IsCaughtUpAfterTheLoad()
	{
		var a = AddSession("a", 10);
		using var pane = CreatePane();
		_client.SessionGetGate = new TaskCompletionSource();

		var open = pane.OpenAsync("a");
		a.Entries.Add(FakeSession.MakeEntry(10));
		_client.RaiseNotification("session.appended", new SessionEpochPayload("a", 1));
		_client.SessionGetGate.SetResult();
		await open;
		await pane.WhenIdleAsync();

		Assert.Equal(Enumerable.Range(0, 11), _surface.PageIndexes);
	}

	[Fact]
	public async Task OlderRequestInFlightForOneSession_DoesNotBlockPagingOfTheNext()
	{
		AddSession("a", 250);
		AddSession("b", 250);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		var gate = new TaskCompletionSource();
		_client.OlderGate = gate;
		_surface.RaiseOlderRequested();

		await pane.OpenAsync("b");
		_client.OlderGate = null;
		_surface.RaiseOlderRequested();
		await Task.Delay(50);
		gate.SetResult();
		await pane.WhenIdleAsync();

		Assert.Equal(Enumerable.Range(50, 200), _surface.PageIndexes);
	}

	[Fact]
	public async Task PageRestart_ReplaysTheCurrentSession()
	{
		AddSession("a", 30);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		var loads = _surface.Calls.Count(c => c.StartsWith("load:a", StringComparison.Ordinal));

		_surface.RaisePageRestarted();

		Assert.Equal(loads + 1, _surface.Calls.Count(c => c.StartsWith("load:a", StringComparison.Ordinal)));
		Assert.Equal(Enumerable.Range(0, 30), _surface.PageIndexes);
	}
}
