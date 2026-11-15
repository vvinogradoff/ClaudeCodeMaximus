using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Transcript;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The turn loop of the session pane (W1a): turn.send / turn.stop, the live row driven by turn.* events, queued sends and resync.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnLoopViewModelTests
{
	private const string Node = "a";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();

	private SessionPaneViewModel CreatePane(TimeSpan? flush = null) =>
		new(_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			LiveFlushInterval = flush ?? TimeSpan.Zero,
		};

	private async Task<SessionPaneViewModel> OpenedAsync(string id = Node, bool running = false)
	{
		var session = FakeSession.WithEntries(4);
		session.Running = running;
		_client.Sessions[id] = session;
		var pane = CreatePane();
		await pane.OpenAsync(id);
		return pane;
	}

	// ---- sending ----

	[Fact]
	public async Task Send_GoesThroughTurnSendWithTheSelectionAndARequestId()
	{
		_client.Catalog = Catalog();
		using var pane = await OpenedAsync();
		await pane.LoadCatalogAsync();
		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "high");
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "work");
		pane.InputText = " do it ";

		await pane.SendCommand.ExecuteAsync(null);

		var send = Assert.Single(_client.TurnSends);
		Assert.Equal(new FakeTurnSend(Node, "do it", "opus", "high", "work", send.ClientRequestId), send);
		Assert.False(string.IsNullOrEmpty(send.ClientRequestId));
		Assert.Equal(0, _client.Calls.Count(c => c.StartsWith("session.send", StringComparison.Ordinal)));
		Assert.Equal(string.Empty, pane.InputText);
	}

	[Fact]
	public async Task EachSendKeepsTheSelectionThatWasSetWhenItWasPressed()
	{
		_client.Catalog = Catalog();
		using var pane = await OpenedAsync();
		await pane.LoadCatalogAsync();
		_client.TurnSendHandler = send => new TurnSendResult("t" + _client.TurnSends.Count, true, Queued: _client.TurnSends.Count > 1);

		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "low");
		pane.InputText = "first";
		await pane.SendCommand.ExecuteAsync(null);
		pane.SelectedModel = pane.Models.Single(m => m.Id == "sonnet");
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "max");
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "work");
		pane.InputText = "second";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Equal(["opus", "sonnet"], _client.TurnSends.Select(s => s.ModelId));
		Assert.Equal(["low", "max"], _client.TurnSends.Select(s => s.Effort));
		Assert.Equal([null, "work"], _client.TurnSends.Select(s => s.ProfileId));   // only what the user picked is sent
	}

	[Fact]
	public async Task Send_WhileATurnRuns_IsAllowedAndTheSecondShowsAsAQueuedRow()
	{
		using var pane = await OpenedAsync();
		_client.TurnSendHandler = send => new TurnSendResult("t" + _client.TurnSends.Count, true, Queued: _client.TurnSends.Count > 1);
		pane.InputText = "first";
		await pane.SendCommand.ExecuteAsync(null);
		_client.RaiseTurnStarted(Node, "t1", _client.TurnSends[0].ClientRequestId);
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		pane.InputText = "second prompt that waits";
		Assert.True(pane.SendCommand.CanExecute(null));
		await pane.SendCommand.ExecuteAsync(null);
		_client.RaiseTurnStarted(Node, "t2", _client.TurnSends[1].ClientRequestId);
		_client.RaiseTurnStatus(Node, "t2", TurnState.Queued);

		var live = _surface.LastLive!;
		Assert.Equal([string.Format(Constants.TurnTexts.QueuedRow, 1)], live.Queued);   // the prompt itself is the persisted user entry
		Assert.Equal(Constants.TurnTexts.Thinking, live.Status);

		_client.RaiseTurnCompleted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t2", TurnState.Running);

		Assert.Empty(_surface.LastLive!.Queued);
		Assert.NotNull(_surface.LastLive.StartedAtUnixMs);
	}

	[Fact]
	public async Task Send_RefusedOrFailed_ShowsTheErrorAndKeepsTheInput()
	{
		using var pane = await OpenedAsync();
		_client.TurnSendHandler = _ => new TurnSendResult("t", false, false);
		pane.InputText = "x";
		await pane.SendCommand.ExecuteAsync(null);
		Assert.Equal(Constants.TurnTexts.NotAccepted, pane.Error);
		Assert.Equal("x", pane.InputText);

		_client.TurnSendHandler = null;
		_client.TurnSendException = new InvalidOperationException("boom");
		await pane.SendCommand.ExecuteAsync(null);
		Assert.Contains("boom", pane.Error);
		Assert.Equal("x", pane.InputText);
	}

	[Fact]
	public async Task Stop_CallsTurnStopWhileATurnRuns()
	{
		using var pane = await OpenedAsync();
		Assert.False(pane.StopCommand.CanExecute(null));
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		Assert.True(pane.IsRunning);
		Assert.True(pane.StopCommand.CanExecute(null));

		await pane.StopCommand.ExecuteAsync(null);

		Assert.Contains("turn.stop:a", _client.Calls);
	}

	[Fact]
	public async Task Stop_FailureIsShown()
	{
		using var pane = await OpenedAsync(running: true);
		_client.TurnStopException = new InvalidOperationException("nope");

		await pane.StopCommand.ExecuteAsync(null);

		Assert.Contains("nope", pane.Error);
	}

	// ---- the live row ----

	[Fact]
	public async Task Events_BuildTheLiveRow_StatusTimerTextToolsAndUsage()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);

		var live = _surface.LastLive!;
		Assert.Equal(Constants.TurnTexts.Thinking, live.Status);
		Assert.NotNull(live.StartedAtUnixMs);
		Assert.True(pane.IsRunning);
		Assert.Equal("Running", pane.StateText);

		_client.RaiseTurnDelta(Node, "t1", 1, "pondering", TurnDeltaKind.Thinking);
		_client.RaiseTurnDelta(Node, "t1", 2, "Hello ");
		_client.RaiseTurnDelta(Node, "t1", 3, "world");
		_client.RaiseTurnTool(Node, "t1", "u1", "Bash", TurnToolStatus.Started, "ls -la");
		_client.RaiseTurnTool(Node, "t1", "u2", "Read", TurnToolStatus.Started, "a.cs");
		_client.RaiseTurnTool(Node, "t1", "u1", "Bash", TurnToolStatus.Result, "ok", isError: false);
		_client.RaiseTurnTool(Node, "t1", "u2", "Read", TurnToolStatus.Result, "denied", isError: true);
		_client.RaiseTurnUsage(Node, "t1", 1200, 340, 0.0123);

		live = _surface.LastLive!;
		Assert.Equal(Constants.TurnTexts.Responding, live.Status);
		Assert.Equal("Hello world", live.Text);
		Assert.Equal("pondering", live.Thinking);
		Assert.Equal([new TranscriptLiveTool("u1", "Bash", "ls -la", true, false), new TranscriptLiveTool("u2", "Read", "a.cs", true, true)], live.Tools);
		Assert.Equal("1200 in / 340 out, $0.0123", live.Usage);
	}

	[Fact]
	public async Task OutputWithoutAStatusEvent_StillMarksTheTurnRunning()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnDelta(Node, "t1", 1, "x");

		Assert.True(pane.IsRunning);
		Assert.NotNull(_surface.LastLive!.StartedAtUnixMs);
	}

	[Theory]
	[InlineData(RemoteProtocol.TurnStream.RetryContext, Constants.TurnTexts.ResumingSession)]
	[InlineData(RemoteProtocol.TurnStream.RetryAuth, Constants.TurnTexts.RetryingAuth)]
	[InlineData("other", Constants.TurnTexts.Retrying)]
	public async Task RetryStatus_ShowsItsReason(string detail, string expected)
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Retrying, detail);

		Assert.Equal(expected, _surface.LastLive!.Status);
	}

	[Fact]
	public async Task Completed_RemovesTheLiveRow_AndReadsTheNewEntries()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnDelta(Node, "t1", 1, "answer");
		_client.Sessions[Node].Entries.Add(FakeSession.MakeEntry(4));
		var before = _client.Calls.Count(c => c.Contains("after=3", StringComparison.Ordinal));

		_client.RaiseTurnCompleted(Node, "t1");
		await pane.WhenIdleAsync();

		Assert.Null(_surface.LastLive);
		Assert.False(pane.IsRunning);
		Assert.True(_client.Calls.Count(c => c.Contains("after=3", StringComparison.Ordinal)) > before);
		Assert.Equal(5, _surface.PageIndexes.Count);
	}

	[Fact]
	public async Task CompletedWithAnError_ShowsAnErrorRow_UntilTheNextSend()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnCompleted(Node, "t1", error: "claude exited with code 1");

		Assert.Equal("claude exited with code 1", _surface.LastLive!.Error);
		Assert.False(pane.IsRunning);

		pane.InputText = "again";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Null(_surface.LastLive);
	}

	[Fact]
	public async Task Cancelled_ClearsTheRowWithoutAnError()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnCompleted(Node, "t1", cancelled: true);

		Assert.Null(_surface.LastLive);
		Assert.False(pane.IsRunning);
	}

	[Fact]
	public async Task ATurnOfAnotherSession_DoesNotTouchTheOpenPage_ButShowsWhenThatSessionOpens()
	{
		using var pane = await OpenedAsync();
		_client.Sessions["b"] = FakeSession.WithEntries(2);
		var liveCalls = _surface.LiveViews.Count;

		_client.RaiseTurnStarted("b", "tb");
		_client.RaiseTurnStatus("b", "tb", TurnState.Running);
		_client.RaiseTurnDelta("b", "tb", 1, "for b");
		Assert.Equal(liveCalls, _surface.LiveViews.Count);
		Assert.False(pane.IsRunning);

		await pane.OpenAsync("b");

		Assert.Equal("for b", _surface.LastLive!.Text);
		Assert.True(pane.IsRunning);
	}

	[Fact]
	public async Task EveryLoadOfThePage_IsFollowedByTheCurrentLiveRow()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		var count = _surface.LiveViews.Count;

		_surface.RaisePageRestarted();
		Assert.True(_surface.LiveViews.Count > count);
		Assert.Equal(Constants.TurnTexts.Thinking, _surface.LastLive!.Status);

		count = _surface.LiveViews.Count;
		_client.RaiseNotification(RemoteProtocol.Events.SessionReset, new SessionEpochPayload(Node, 2));
		await pane.WhenIdleAsync();
		Assert.True(_surface.LiveViews.Count > count);
		Assert.NotNull(_surface.LastLive);
	}

	// ---- running flag from the daemon, reconnect ----

	[Fact]
	public async Task ARunningSessionWithoutKnownTurns_ShowsAGenericWorkingRow()
	{
		using var pane = await OpenedAsync();
		var session = _client.Sessions[Node];

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(session.State(Node) with { Running = true }));

		Assert.True(pane.IsRunning);
		Assert.Equal(Constants.TurnTexts.Working, _surface.LastLive!.Status);
		Assert.NotNull(_surface.LastLive.StartedAtUnixMs);

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(session.State(Node) with { Running = false }));

		Assert.False(pane.IsRunning);
		Assert.Null(_surface.LastLive);
	}

	[Fact]
	public async Task AResyncDuringATurn_RereadsTheSession_AndShowsItsFinalState()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnDelta(Node, "t1", 1, "partial");
		var session = _client.Sessions[Node];
		session.Running = true;

		_client.RaiseResync();
		await pane.WhenIdleAsync();

		// The stream may have lost events: the preview is dropped and the generic row shows while the daemon says the session runs.
		Assert.Equal(Constants.TurnTexts.Working, _surface.LastLive!.Status);
		Assert.Equal(string.Empty, _surface.LastLive.Text);
		Assert.True(pane.IsRunning);

		// The turn ended while the client was away: the session state says idle and the entry is there.
		session.Running = false;
		session.Entries.Add(FakeSession.MakeEntry(4));
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(session.State(Node)));
		_client.RaiseNotification(RemoteProtocol.Events.SessionAppended, new SessionEpochPayload(Node, session.Epoch));
		await pane.WhenIdleAsync();

		Assert.Null(_surface.LastLive);
		Assert.False(pane.IsRunning);
		Assert.Contains(4, _surface.PageIndexes);
	}

	[Fact]
	public async Task AResumedStream_ReplaysTurnEventsWithoutAResync_AndContinuesTheRow()
	{
		using var pane = await OpenedAsync();
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnDelta(Node, "t1", 1, "before ");
		_client.SetState(TessynDesktop.Client.TessynClientState.Reconnecting);
		_client.SetState(TessynDesktop.Client.TessynClientState.Connected);
		_client.RaiseTurnDelta(Node, "t1", 2, "after");

		Assert.Equal("before after", _surface.LastLive!.Text);
		Assert.True(pane.IsRunning);
	}

	// ---- batching ----

	[Fact]
	public async Task ABurstOfDeltas_IsMergedIntoFewPageUpdates()
	{
		using var pane = CreatePane(TimeSpan.FromMilliseconds(60));
		_client.Sessions[Node] = FakeSession.WithEntries(2);
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		await Task.Delay(200);
		var before = _surface.LiveViews.Count;

		for (var i = 1; i <= 200; i++)
			_client.RaiseTurnDelta(Node, "t1", i, "x");
		await Task.Delay(400);

		var updates = _surface.LiveViews.Count - before;
		Assert.InRange(updates, 1, 5);
		Assert.Equal(new string('x', 200), _surface.LastLive!.Text);
	}

	[Fact]
	public async Task StreamingFiveThousandDeltas_StaysWithinTheUiBudget()
	{
		// The UI thread does one small state update per notification and one page update per flush window; this guards against per-delta page work.
		using var pane = CreatePane(TimeSpan.FromMilliseconds(80));
		_client.Sessions[Node] = FakeSession.WithEntries(2);
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		await Task.Delay(150);
		var before = _surface.LiveViews.Count;

		var watch = System.Diagnostics.Stopwatch.StartNew();
		for (var i = 1; i <= 5000; i++)
			_client.RaiseTurnDelta(Node, "t1", i, "token ");
		watch.Stop();
		await Task.Delay(300);

		Assert.True(watch.ElapsedMilliseconds < 1500, $"5000 deltas took {watch.ElapsedMilliseconds} ms to apply");
		Assert.True(_surface.LiveViews.Count - before < 25, $"{_surface.LiveViews.Count - before} page updates for one burst");
		Assert.Equal(Constants.TurnLimits.PreviewTextChars, _surface.LastLive!.Text.Length);
	}

	// ---- the state classes ----

	[Fact]
	public void PreviewText_KeepsTheNewestCharacters_AndANoteOfTheGap()
	{
		var turn = new TurnLiveState("t");
		turn.AddDelta(new TurnDeltaPayload(Node, "t", 1, TurnDeltaKind.Text, new string('a', Constants.TurnLimits.PreviewTextChars)));
		turn.AddDelta(new TurnDeltaPayload(Node, "t", 2, TurnDeltaKind.Text, "END"));
		Assert.False(turn.HasGap);
		turn.AddDelta(new TurnDeltaPayload(Node, "t", 5, TurnDeltaKind.Text, "!"));

		Assert.Equal(Constants.TurnLimits.PreviewTextChars, turn.Text.Length);
		Assert.EndsWith("END" + Constants.TurnTexts.GapMarker + "!", turn.Text);
		Assert.True(turn.HasGap);
	}

	[Fact]
	public void ToolList_KeepsTheNewestTools()
	{
		var turn = new TurnLiveState("t");
		for (var i = 0; i < Constants.TurnLimits.PreviewTools + 5; i++)
			turn.ApplyTool(new TurnToolPayload(Node, "t", "u" + i, "Bash", TurnToolStatus.Started));

		Assert.Equal(Constants.TurnLimits.PreviewTools, turn.Tools.Count);
		Assert.Equal("u" + (Constants.TurnLimits.PreviewTools + 4), turn.Tools[^1].Id);
		turn.ApplyTool(new TurnToolPayload(Node, "t", "unknown", "Bash", TurnToolStatus.Result));
		Assert.DoesNotContain(turn.Tools, t => t.Id == "unknown");
	}

	// ---- selectors ----

	[Fact]
	public async Task Selectors_FillFromTheCatalog_AndRestoreWhatTheSessionWasLastUsedWith()
	{
		_client.Catalog = Catalog();
		var session = FakeSession.WithEntries(2);
		session.LastUsed = new LastUsedSettingsDto("sonnet", "high", "work", "Work");
		_client.Sessions[Node] = session;
		using var pane = CreatePane();

		await pane.OpenAsync(Node);
		await pane.InitializeAsync();

		Assert.Equal(["default", "opus", "sonnet"], pane.Models.Select(m => m.Id));
		Assert.Equal(["Default", "Max", "High", "Medium", "Low"], pane.Efforts.Select(e => e.Label));
		Assert.Equal("sonnet", pane.SelectedModel!.Id);
		Assert.Equal("high", pane.SelectedEffort!.Id);
		Assert.Equal("work", pane.SelectedProfile!.Id);
	}

	[Fact]
	public async Task AnUnknownLastUsedId_FallsBackToTheDefaultOption()
	{
		_client.Catalog = Catalog();
		var session = FakeSession.WithEntries(2);
		session.LastUsed = new LastUsedSettingsDto("gone-model", "ultra", "deleted-profile", "x");
		_client.Sessions[Node] = session;
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();

		Assert.Equal("default", pane.SelectedModel!.Id);
		Assert.Equal("default", pane.SelectedEffort!.Id);
		Assert.Equal("default", pane.SelectedProfile!.Id);
	}

	[Fact]
	public async Task APickIsRememberedPerSession_AndLastUsedDoesNotOverwriteIt()
	{
		_client.Catalog = Catalog();
		_client.Sessions["a"] = FakeSession.WithEntries(2);
		_client.Sessions["b"] = FakeSession.WithEntries(2);
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		await pane.LoadCatalogAsync();
		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");

		await pane.OpenAsync("b");
		Assert.Equal("default", pane.SelectedModel!.Id);
		pane.SelectedModel = pane.Models.Single(m => m.Id == "sonnet");
		await pane.OpenAsync("a");

		Assert.Equal("opus", pane.SelectedModel!.Id);
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(_client.Sessions["a"].State("a")));
		Assert.Equal("opus", pane.SelectedModel!.Id);
	}

	[Fact]
	public async Task ACatalogThatCannotBeRead_LeavesTheSelectorsEmpty_AndSendLetsTheDaemonChoose()
	{
		_client.CatalogException = new InvalidOperationException("no catalog");
		using var pane = await OpenedAsync();
		await pane.LoadCatalogAsync();
		pane.InputText = "go";

		await pane.SendCommand.ExecuteAsync(null);

		Assert.Empty(pane.Models);
		var send = _client.TurnSends.Single();
		Assert.Null(send.ModelId);
		Assert.Null(send.Effort);
		Assert.Null(send.ProfileId);
	}

	[Fact]
	public void SelectorOption_CutsTheStartOfALongLabel_AndMarksSystemProfiles()
	{
		var shortOne = new SelectorOptionViewModel("m", "Opus 5");
		var longOne = new SelectorOptionViewModel("m", "A very long provider prefix / claude-opus-5-5-20261101");
		var system = new SelectorOptionViewModel("default", "[Sys] Default");

		Assert.Equal("Opus 5", shortOne.ShortLabel);
		Assert.Equal(Constants.TurnLimits.SelectorLabelChars, longOne.ShortLabel.Length);
		Assert.StartsWith("...", longOne.ShortLabel);
		Assert.EndsWith("claude-opus-5-5-20261101", longOne.ShortLabel);
		Assert.Equal("A very long provider prefix / claude-opus-5-5-20261101", longOne.Label);
		Assert.True(system.IsSystem);
		Assert.False(shortOne.IsSystem);
	}

	private static CatalogResult Catalog() => new(
		[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus 5.5", null), new ModelOptionDto("sonnet", "Sonnet 5.5", null)],
		["default", "max", "high", "medium", "low"],
		[new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);
}
