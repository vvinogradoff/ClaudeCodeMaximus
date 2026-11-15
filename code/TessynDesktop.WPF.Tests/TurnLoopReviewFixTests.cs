using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The fixes of the W1a review (L1 to L9): what is sent, a clean switch between sessions, stale answers, double sends, the hand-off from live row to entry, replays and the real engine's frames.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnLoopReviewFixTests
{
	private const string Node = "a";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();

	private SessionPaneViewModel CreatePane() =>
		new(_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			LiveFlushInterval = TimeSpan.Zero,
		};

	private static CatalogResult Catalog() => new(
		[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus 5.5", null), new ModelOptionDto("sonnet", "Sonnet 5.5", null)],
		["default", "max", "high", "medium", "low"],
		[new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);

	private FakeSession AddSession(string id, int entries = 4)
	{
		var session = FakeSession.WithEntries(entries);
		_client.Sessions[id] = session;
		return session;
	}

	// ---- L1: only what the user picked is sent ----

	[Fact]
	public async Task L1_ARestoredLastUsedValue_IsShownButNeverSent()
	{
		_client.Catalog = Catalog();
		AddSession(Node).LastUsed = new LastUsedSettingsDto("sonnet", "high", "work", "Work");
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		Assert.Equal(("sonnet", "high", "work"), (pane.SelectedModel!.Id, pane.SelectedEffort!.Id, pane.SelectedProfile!.Id));
		pane.InputText = "just type";

		await pane.SendCommand.ExecuteAsync(null);

		var send = _client.TurnSends.Single();
		Assert.Equal((null, null, null), (send.ModelId, send.Effort, send.ProfileId));
	}

	[Fact]
	public async Task L1_OnlyThePickedFieldsAreSent_AndAQueuedSendKeepsWhatWasPickedWhenItWasPressed()
	{
		_client.Catalog = Catalog();
		AddSession(Node).LastUsed = new LastUsedSettingsDto("sonnet", "high", "work", "Work");
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();

		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");
		pane.InputText = "first";
		await pane.SendCommand.ExecuteAsync(null);
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "low");
		pane.InputText = "second";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Equal([("opus", null, null), ("opus", "low", null)], _client.TurnSends.Select(s => (s.ModelId, s.Effort, s.ProfileId)));
	}

	[Fact]
	public async Task L1_APickedDefaultOption_IsSentExplicitly()
	{
		_client.Catalog = Catalog();
		AddSession(Node).LastUsed = new LastUsedSettingsDto("sonnet", "high", "work", "Work");
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "default");
		pane.InputText = "x";

		await pane.SendCommand.ExecuteAsync(null);

		Assert.Equal("default", _client.TurnSends.Single().ProfileId);
	}

	[Fact]
	public async Task L1_ARefilledCatalog_DoesNotEraseAPick()
	{
		_client.Catalog = Catalog();
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");

		await pane.LoadCatalogAsync();
		pane.InputText = "x";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Equal("opus", _client.TurnSends.Single().ModelId);
		Assert.Equal("opus", pane.SelectedModel!.Id);
	}

	// ---- L2: nothing of the previous session while the next loads ----

	[Fact]
	public async Task L2_OpeningAnotherSession_ResetsTheSelectors_AndSendWaitsForItsDetail()
	{
		_client.Catalog = Catalog();
		AddSession("a").LastUsed = new LastUsedSettingsDto("opus", "high", "work", "Work");
		AddSession("b").LastUsed = new LastUsedSettingsDto("sonnet", "low", "default", "[Sys] Default");
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		await pane.LoadCatalogAsync();
		Assert.Equal("opus", pane.SelectedModel!.Id);
		pane.InputText = "draft";

		_client.SessionGetGate = new TaskCompletionSource();
		var opening = pane.OpenAsync("b");

		Assert.Equal(("default", "default", "default"), (pane.SelectedModel!.Id, pane.SelectedEffort!.Id, pane.SelectedProfile!.Id));
		pane.InputText = "typed while loading";
		Assert.False(pane.SendCommand.CanExecute(null));

		_client.SessionGetGate.SetResult();
		await opening;

		Assert.Equal(("sonnet", "low", "default"), (pane.SelectedModel!.Id, pane.SelectedEffort!.Id, pane.SelectedProfile!.Id));
		Assert.True(pane.SendCommand.CanExecute(null));
	}

	[Fact]
	public async Task L2_ACatalogReloadWhileTheNextSessionLoads_DoesNotApplyThePreviousSessionsSettings()
	{
		_client.Catalog = Catalog();
		AddSession("a").LastUsed = new LastUsedSettingsDto("opus", "high", "work", "Work");
		AddSession("b");
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		await pane.LoadCatalogAsync();

		_client.SessionGetGate = new TaskCompletionSource();
		var opening = pane.OpenAsync("b");
		await pane.LoadCatalogAsync();

		Assert.Equal(("default", "default", "default"), (pane.SelectedModel!.Id, pane.SelectedEffort!.Id, pane.SelectedProfile!.Id));
		_client.SessionGetGate.SetResult();
		await opening;
	}

	[Fact]
	public async Task L2_ADetailThatCannotBeRead_DoesNotLeaveSendDisabledForGood()
	{
		AddSession("a");
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		_client.Sessions.Remove("b");   // session.get for b throws (KeyNotFound in the fake)

		await pane.OpenAsync("b");
		pane.InputText = "x";

		Assert.True(pane.SendCommand.CanExecute(null));
	}

	// ---- L3: a stale session.get must not set the running flag ----

	[Fact]
	public async Task L3_AnOpenWhoseDetailAnswersAfterTheTurnEnded_DoesNotShowAStuckRunningRow()
	{
		var session = AddSession(Node);
		session.Running = true;
		using var pane = CreatePane();
		_client.SessionGetGate = new TaskCompletionSource();

		var opening = pane.OpenAsync(Node);   // the request is sent while the turn runs
		_client.RaiseTurnCompleted(Node, "t1");   // the turn ends; the answer (running: true) is still on its way
		_client.SessionGetGate.SetResult();
		await opening;

		Assert.False(pane.IsRunning);
		Assert.False(pane.StopCommand.CanExecute(null));
		Assert.Null(_surface.LastLive);
	}

	[Fact]
	public async Task L3_AStateChangeThatArrivedAfterTheRequestWins_AlsoAfterAResync()
	{
		var session = AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		session.Running = true;
		_client.SessionGetGate = new TaskCompletionSource();

		_client.RaiseResync();   // re-reads the detail: the answer will say running
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(session.State(Node) with { Running = false }));
		_client.SessionGetGate.SetResult();
		await pane.WhenIdleAsync();
		await Task.Delay(50);

		Assert.False(pane.IsRunning);
		Assert.Null(_surface.LastLive);
	}

	[Fact]
	public async Task L3_WithoutAPushInBetween_TheDetailsRunningFlagApplies()
	{
		var session = AddSession(Node);
		session.Running = true;
		using var pane = CreatePane();

		await pane.OpenAsync(Node);

		Assert.True(pane.IsRunning);
		Assert.Equal(Constants.TurnTexts.Working, _surface.LastLive!.Status);
	}

	// ---- L4: a double send sends once, the input is taken before the round trip ----

	[Fact]
	public async Task L4_TwoPressesBeforeTheAnswer_SendOnce_AndTextTypedMeanwhileIsKept()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		pane.InputText = "once";
		_client.TurnSendGate = new TaskCompletionSource();

		var first = pane.SendCommand.ExecuteAsync(null);
		pane.SendCommand.Execute(null);   // key repeat
		Assert.Equal(string.Empty, pane.InputText);
		pane.InputText = "typed during the round trip";
		_client.TurnSendGate.SetResult();
		await first;

		Assert.Equal(1, _client.TurnSends.Count);
		Assert.Equal("typed during the round trip", pane.InputText);
	}

	[Fact]
	public async Task L4_AFailedSend_GivesTheTextBack_UnlessSomethingElseWasTyped()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.TurnSendException = new InvalidOperationException("down");
		pane.InputText = "keep me";
		await pane.SendCommand.ExecuteAsync(null);
		Assert.Equal("keep me", pane.InputText);

		_client.TurnSendGate = new TaskCompletionSource();
		_client.TurnSendException = null;
		_client.TurnSendHandler = _ => new TurnSendResult("t", false, false);
		var send = pane.SendCommand.ExecuteAsync(null);
		pane.InputText = "new text";
		_client.TurnSendGate.SetResult();
		await send;

		Assert.Equal("new text", pane.InputText);
		Assert.Equal(Constants.TurnTexts.NotAccepted, pane.Error);
	}

	// ---- L5: the entry is applied before the live row goes ----

	[Fact]
	public async Task L5_OnCompletion_ThePersistedEntryIsApplied_BeforeTheLiveRowIsCleared()
	{
		var session = AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnDelta(Node, "t1", 1, "answer");
		session.Entries.Add(FakeSession.MakeEntry(4));
		_surface.Calls.Clear();

		_client.RaiseTurnCompleted(Node, "t1");
		await pane.WhenIdleAsync();

		var appended = _surface.Calls.FindIndex(c => c.StartsWith("append:", StringComparison.Ordinal));
		var cleared = _surface.Calls.FindIndex(c => c == "live:none");
		Assert.True(appended >= 0 && cleared > appended, "calls: " + string.Join(", ", _surface.Calls));
	}

	// ---- L6: the live row survives "back to the bottom" ----

	[Fact]
	public async Task L6_BackToTheBottom_KeepsTheLiveRow()
	{
		var session = AddSession(Node, 50);
		session.Entries = [.. session.Entries.Select(e => e with { EntryId = $"f{Node}:{e.Index}", Content = $"entry {e.Index} has some quoted text inside" })];
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.JumpToEntryAsync(new AttentionRefDto { Kind = "entry", NodeId = Node, EntryId = $"f{Node}:5", Offset = 6, Length = 4 });
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);

		await pane.BackCommand.ExecuteAsync(null);

		Assert.Equal(Constants.TurnTexts.Thinking, _surface.LastLive?.Status);
	}

	// ---- L7 to L9: queued row, replays, leaks ----

	[Fact]
	public async Task L7_AQueuedRow_NamesTheWait_NotThePrompt()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnStarted(Node, "t2");
		_client.RaiseTurnStatus(Node, "t2", TurnState.Queued);
		_client.RaiseTurnStarted(Node, "t3");
		_client.RaiseTurnStatus(Node, "t3", TurnState.Queued);

		Assert.Equal([string.Format(Constants.TurnTexts.QueuedRow, 1), string.Format(Constants.TurnTexts.QueuedRow, 2)], _surface.LastLive!.Queued);
	}

	[Fact]
	public async Task Idempotency_ARepeatedDeltaSeq_AddsNothing()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnDelta(Node, "t1", 1, "one ");
		_client.RaiseTurnDelta(Node, "t1", 2, "two ");
		var updates = _surface.LiveViews.Count;

		_client.RaiseTurnDelta(Node, "t1", 2, "two ");   // a replay
		_client.RaiseTurnDelta(Node, "t1", 1, "one ");

		Assert.Equal("one two ", _surface.LastLive!.Text);
		Assert.Equal(updates, _surface.LiveViews.Count);
	}

	[Fact]
	public async Task L9_ErrorsOfSessionsThatAreNotOpen_AreBounded()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);

		for (var i = 0; i < Constants.TurnLimits.RememberedErrorSessions + 15; i++)
		{
			_client.RaiseTurnStarted("other" + i, "t");
			_client.RaiseTurnCompleted("other" + i, "t", error: "boom");
		}

		// The oldest are gone: opening the first one shows no stale error, the newest still shows its error.
		_client.Sessions["other0"] = FakeSession.WithEntries(1);
		_client.Sessions["other" + (Constants.TurnLimits.RememberedErrorSessions + 14)] = FakeSession.WithEntries(1);
		await pane.OpenAsync("other0");
		Assert.Null(_surface.LastLive);
		await pane.OpenAsync("other" + (Constants.TurnLimits.RememberedErrorSessions + 14));
		Assert.Equal("boom", _surface.LastLive?.Error);
	}

	// ---- review test 9: a queued send behind an error completion ----

	[Fact]
	public async Task AQueuedTurnBehindATurnThatEndedInAnError_ShowsTheErrorAndTheQueuedRowTogether()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnStarted(Node, "t2");
		_client.RaiseTurnStatus(Node, "t2", TurnState.Queued);

		_client.RaiseTurnCompleted(Node, "t1", error: "claude exited with code 1");

		var live = _surface.LastLive!;
		Assert.Equal("claude exited with code 1", live.Error);
		Assert.Single(live.Queued);
		Assert.Equal(Constants.TurnTexts.Waiting, live.Status);
	}

	// ---- the real engine's golden frames (TurnStreamWireTests) through the view model ----

	[Fact]
	public async Task Golden_ANormalTurn_StartsStreamsUsesToolsAndEnds()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);

		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t1","clientRequestId":"req-42"}""");
		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"t1","state":"running"}""");
		_client.RaiseRaw("turn.delta", """{"nodeId":"a","turnId":"t1","seq":1,"kind":"text","text":"Let me look. "}""");
		_client.RaiseRaw("turn.tool", """{"nodeId":"a","turnId":"t1","toolUseId":"tu_1","name":"Read","status":"started","summary":"a.txt"}""");
		_client.RaiseRaw("turn.tool", """{"nodeId":"a","turnId":"t1","toolUseId":"tu_1","name":"Read","status":"result","summary":"contents","isError":false}""");
		_client.RaiseRaw("turn.delta", """{"nodeId":"a","turnId":"t1","seq":2,"kind":"text","text":"Found it."}""");
		_client.RaiseRaw("turn.usage", """{"nodeId":"a","turnId":"t1","inputTokens":100,"outputTokens":50,"costUsd":0.25}""");

		var live = _surface.LastLive!;
		Assert.Equal("Let me look. Found it.", live.Text);
		Assert.Equal([new("tu_1", "Read", "a.txt", true, false)], live.Tools);
		Assert.Equal("100 in / 50 out, $0.2500", live.Usage);

		_client.RaiseRaw("turn.completed", """{"nodeId":"a","cancelled":false,"turnId":"t1"}""");
		await pane.WhenIdleAsync();

		Assert.Null(_surface.LastLive);
		Assert.False(pane.IsRunning);
	}

	[Theory]
	[InlineData("context", Constants.TurnTexts.ResumingSession)]
	[InlineData("auth", Constants.TurnTexts.RetryingAuth)]
	public async Task Golden_ARetryingStatus_ShowsItsReason_ThenTheTurnRunsAgain(string detail, string expected)
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t1"}""");
		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"t1","state":"running"}""");

		_client.RaiseRaw("turn.status", $$"""{"nodeId":"a","turnId":"t1","state":"retrying","detail":"{{detail}}"}""");
		Assert.Equal(expected, _surface.LastLive!.Status);

		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"t1","state":"running"}""");
		Assert.Equal(Constants.TurnTexts.Thinking, _surface.LastLive!.Status);
	}

	[Fact]
	public async Task Golden_ANeverRanCompletion_RemovesTheQueuedTurnWithoutAnError()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t1"}""");
		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"t1","state":"running"}""");
		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t2"}""");
		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"t2","state":"queued"}""");
		Assert.Single(_surface.LastLive!.Queued);

		_client.RaiseRaw("turn.completed", """{"nodeId":"a","cancelled":true,"turnId":"t2","neverRan":true}""");

		Assert.Empty(_surface.LastLive!.Queued);
		Assert.Null(_surface.LastLive.Error);
		Assert.Equal(Constants.TurnTexts.Thinking, _surface.LastLive.Status);   // the running turn is untouched
		Assert.True(pane.IsRunning);
	}

	[Fact]
	public async Task Golden_AnErrorCompletion_LeavesAnErrorRow()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t1"}""");
		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"t1","state":"running"}""");

		_client.RaiseRaw("turn.completed", """{"nodeId":"a","cancelled":false,"error":"claude exited with code 1","turnId":"t1"}""");
		await pane.WhenIdleAsync();

		Assert.Equal("claude exited with code 1", _surface.LastLive!.Error);
		Assert.False(pane.IsRunning);
	}

	[Fact]
	public async Task Golden_AGapInTheDeltaSequence_ShowsWhereTextWasDropped()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t1"}""");
		_client.RaiseRaw("turn.delta", """{"nodeId":"a","turnId":"t1","seq":1,"kind":"text","text":"before"}""");
		_client.RaiseRaw("turn.delta", """{"nodeId":"a","turnId":"t1","seq":4,"kind":"text","text":"after"}""");

		Assert.Equal("before" + Constants.TurnTexts.GapMarker + "after", _surface.LastLive!.Text);
	}

	[Fact]
	public async Task Golden_AToolResultWhoseStartWasMissed_AndAFailingTool_DoNotBreakTheRow()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"t1"}""");
		_client.RaiseRaw("turn.tool", """{"nodeId":"a","turnId":"t1","toolUseId":"lost","name":"Bash","status":"result","summary":"x","isError":false}""");
		_client.RaiseRaw("turn.tool", """{"nodeId":"a","turnId":"t1","toolUseId":"u2","name":"Bash","status":"started","summary":"false"}""");
		_client.RaiseRaw("turn.tool", """{"nodeId":"a","turnId":"t1","toolUseId":"u2","name":"Bash","status":"result","summary":"exit 1","isError":true}""");

		Assert.Equal([new("u2", "Bash", "false", true, true)], _surface.LastLive!.Tools);
	}

	[Fact]
	public async Task Golden_ATurnOfAnotherClient_WithNoRequestId_ShowsTheSameRow()
	{
		AddSession(Node);
		using var pane = CreatePane();
		await pane.OpenAsync(Node);

		_client.RaiseRaw("turn.started", """{"nodeId":"a","turnId":"theirs"}""");
		_client.RaiseRaw("turn.status", """{"nodeId":"a","turnId":"theirs","state":"running"}""");
		_client.RaiseRaw("turn.delta", """{"nodeId":"a","turnId":"theirs","seq":1,"kind":"thinking","text":"hmm"}""");

		Assert.Equal("hmm", _surface.LastLive!.Thinking);
		Assert.True(pane.IsRunning);
	}
}
