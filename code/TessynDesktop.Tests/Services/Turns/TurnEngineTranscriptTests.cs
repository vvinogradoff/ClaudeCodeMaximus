using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Safety net for the interactive turn path (P1.I stage 1): which entries a turn writes, in which order, with which metadata, and what
/// the observer is told. The expectations come from the former <c>TurnController</c> (<c>SendAsyncCore</c> and <c>HandleStreamEvent</c>),
/// not from the engine.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineTranscriptTests : IDisposable
{
	private readonly TurnEngineHarness _h = new();

	public void Dispose() => _h.Dispose();

	[Fact]
	public async Task OneShot_WritesUserEntry_ThenAssistantEntries_ThenReportsCompletion()
	{
		var node = TurnEngineHarness.Node();
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Assistant("hello"), Evt.Assistant("more"), Evt.Result("sess-1"));

		var outcome = await _h.Run(node, "Hi", observer).Within();

		Assert.Equal(
			["append:USER:Hi", "append:ASSISTANT:hello", "append:ASSISTANT:more", "completed:cancelled=False:error=null"],
			_h.Files.Log);
		Assert.False(outcome.Cancelled);
		Assert.Null(outcome.ErrorText);
		Assert.False(string.IsNullOrEmpty(outcome.TurnId));
		Assert.Equal("sess-1", node.ClaudeSessionId);
	}

	[Fact]
	public async Task UserEntry_CarriesTheProfileName_AssistantEntryOnlyTheFirstOnesModelAndEffort()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Assistant("b"), Evt.Result());

		await _h.Run(node, "Hi", settings: TurnEngineHarness.Chosen(model: "sonnet", effort: "low", profileName: "Work")).Within();

		var entries = _h.Files.Entries;
		Assert.Equal("Work", entries[0].ProfileName);
		Assert.Null(entries[0].ModelId);
		Assert.Equal(("sonnet", "low"), (entries[1].ModelId, entries[1].Effort));
		Assert.Equal((null, null), (entries[2].ModelId, entries[2].Effort));
	}

	[Fact]
	public async Task NoModelOrEffortChosen_FirstAssistantEntryIsStampedDefault()
	{
		var node = TurnEngineHarness.Node();
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Assistant("b"), Evt.Result());

		await _h.Run(node, "Hi", observer, TurnEngineHarness.Chosen(model: null, effort: null)).Within();

		Assert.Equal(("default", "default"), (_h.Files.Entries[1].ModelId, _h.Files.Entries[1].Effort));
		Assert.Equal([("a", "default", "default"), ("b", null, null)], observer.Assistants);
	}

	[Fact]
	public async Task UserMessageIsStoredClean_ProcessGetsTheMessagePlusTheInstructionBlock()
	{
		var node = TurnEngineHarness.Node();
		node.IsAutoCommit   = true;
		node.IsAutoDocument = true;
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "Fix it", isNewBranch: true).Within();

		Assert.Equal("Fix it", _h.Files.Entries[0].Content);
		var sent = Assert.Single(_h.Process.Calls).Message;
		Assert.Equal("Fix it" + _h.ExpectedBlock(node, isNewBranch: true, isNewSession: true), sent);
		Assert.Contains(Constants.Instructions.NewBranch, sent);
		Assert.Contains(Constants.Instructions.AutoCommitOn, sent);
	}

	[Fact]
	public async Task InstructionBlock_IsNewSessionOnlyWithoutClaudeSessionId()
	{
		var node = TurnEngineHarness.Node(sessionId: "sess-9");
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "Hi").Within();

		Assert.Equal("Hi" + _h.ExpectedBlock(node, isNewBranch: false, isNewSession: false), _h.Process.Calls[0].Message);
	}

	[Fact]
	public async Task FreshSessionWithHistory_WrapsTheMessageInTheContextPreamble()
	{
		var node = TurnEngineHarness.Node();
		_h.Files.Seed(Constants.SessionFile.RoleUser, "old question");
		_h.Files.Seed(Constants.SessionFile.RoleAssistant, "old answer");
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "Now this").Within();

		var sent = _h.Process.Calls[0].Message;
		Assert.Contains("[Human]: old question", sent);
		Assert.Contains("[Assistant]: old answer", sent);
		Assert.Contains("[Human]: Now this", sent);
		Assert.EndsWith("Now this" + _h.ExpectedBlock(node, false, true) + Environment.NewLine, sent);
	}

	[Fact]
	public async Task FreshSessionWithoutHistory_SendsTheMessageWithoutPreamble()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "First").Within();

		Assert.Equal("First" + _h.ExpectedBlock(node, false, true), _h.Process.Calls[0].Message);
	}

	[Fact]
	public async Task ExistingClaudeSession_ResumesIt_NoPreambleEvenWithHistory_AndAdoptsItFromOtherProfiles()
	{
		var node = TurnEngineHarness.Node(sessionId: "sess-9");
		_h.Files.Seed(Constants.SessionFile.RoleUser, "old question");
		_h.Files.Seed(Constants.SessionFile.RoleAssistant, "old answer");
		_h.Process.ScriptEvents(Evt.Result("sess-9"));

		await _h.Run(node, "Next", settings: TurnEngineHarness.Chosen(profileDir: @"C:\profiles\p2")).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.Equal("sess-9", call.SessionId);
		Assert.DoesNotContain("conversation history", call.Message);
		Assert.Equal([(@"C:\proj", "sess-9", @"C:\profiles\p2")], _h.Status.Adoptions);
	}

	[Fact]
	public async Task FreshSession_DoesNotTryToAdoptAnything()
	{
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Empty(_h.Status.Adoptions);
	}

	[Fact]
	public async Task ProcessIsLaunchedWithTheChosenSettings()
	{
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), settings: TurnEngineHarness.Chosen(
			model: "opus", effort: "max", profileDir: @"C:\profiles\p3", chrome: true, ollamaUrl: "http://localhost:11434")).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.Equal((@"C:\proj", "claude-test", "opus", "max", @"C:\profiles\p3"),
			(call.WorkingDirectory, call.ClaudePath, call.Model, call.Effort, call.ProfileConfigDir));
		Assert.Equal(("http://localhost:11434", true, false), (call.OllamaBaseUrl, call.Chrome, call.DisableTools));
		Assert.Null(call.SessionId);
	}

	[Fact]
	public async Task LazyTelegramTopic_IsRequestedOncePerTurn()
	{
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal([TurnEngineHarness.NodeId], _h.Telegram.TopicRequests);
	}

	[Fact]
	public async Task ObserverSeesTheUserEntryFirst_ThenEachEventInOrder()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Progress("[Read] a.txt"), Evt.Assistant("answer"), Evt.Result("s"));

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(
			["user:Work", "progress:[Read] a.txt", "assistant:answer", "authError:False", "sessionId", "resultEnded", "afterResponse", "beforeIdle", "idle"],
			observer.Snapshot());
	}

	[Fact]
	public async Task UserEntry_IsAppendedBeforeTheFirstAwait_SoACallerOnAUiThreadSeesItSynchronously()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var running = _h.Run(TurnEngineHarness.Node(), "Hi", observer);

		Assert.Equal(["append:USER:Hi"], _h.Files.Log);
		Assert.Equal(["user:Work"], observer.Snapshot());
		Assert.False(running.IsCompleted);

		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		await running.Within();
	}

	[Fact]
	public async Task CompactionEvent_AppendsASeparator()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Compact(), Evt.Assistant("after"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(1, _h.Files.CompactionSeparators);
		Assert.Equal(["append:USER:Hi", "append:COMPACTION", "append:ASSISTANT:after", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Contains("compaction", observer.Snapshot());
	}

	[Theory]
	[InlineData("task_progress")]
	[InlineData("task_started")]
	public async Task TaskProgress_IsShownButNeverPersisted(string subtype)
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Progress("[Bash] build", subtype), Evt.Result());

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Contains("progress:[Bash] build", observer.Snapshot());
	}

	[Fact]
	public async Task BlankAssistantContent_IsIgnored()
	{
		_h.Process.ScriptEvents(Evt.Assistant("  "), Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
	}

	[Fact]
	public async Task SystemError_IsWrittenAsASystemEntry_ButIsNotTheTurnsErrorText()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.SystemError("claude exited with code 2: boom"), Evt.Result());

		var outcome = await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(
			["append:USER:Hi", "append:SYSTEM:claude exited with code 2: boom", "completed:cancelled=False:error=null"],
			_h.Files.Log);
		Assert.Null(outcome.ErrorText);
		Assert.Contains("systemError:claude exited with code 2: boom", observer.Snapshot());
	}

	[Fact]
	public async Task SystemError_WithInvalidCredentials_RaisesTheAuthErrorFlag()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.SystemError("API Error: " + Constants.Auth.InvalidCredentialsMarker));

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		var calls = observer.Snapshot();
		Assert.True(calls.ToList().IndexOf("authError:True") < calls.ToList().FindIndex(c => c.StartsWith("systemError:", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task ResultError_IsWrittenAsASystemEntry_AndReportedAsTheTurnsErrorText()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.ResultError("Usage limit reached"));

		var outcome = await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(
			["append:USER:Hi", "append:SYSTEM:Usage limit reached", "completed:cancelled=False:error=Usage limit reached"],
			_h.Files.Log);
		Assert.Equal("Usage limit reached", outcome.ErrorText);
		Assert.Contains("resultError:Usage limit reached", observer.Snapshot());
	}

	[Fact]
	public async Task ResultError_WithInvalidCredentials_RaisesTheAuthErrorFlagBeforeTheRow()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.ResultError(Constants.Auth.InvalidCredentialsMarker + " (401)"));

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		var calls = observer.Snapshot().ToList();
		Assert.True(calls.IndexOf("authError:True") < calls.FindIndex(c => c.StartsWith("resultError:", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task ResultError_DoesNotTouchTheClaudeSessionId()
	{
		var node = TurnEngineHarness.Node(sessionId: "keep");
		_h.Process.ScriptEvents(Evt.ResultError("nope", sessionId: "other"));

		await _h.Run(node).Within();

		Assert.Equal("keep", node.ClaudeSessionId);
	}

	[Fact]
	public async Task ResultWithBlankErrorText_WritesNothingAndReportsNoError()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(new ClaudeStreamEvent { Type = "result", IsError = true });

		var outcome = await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
		Assert.Contains("resultEnded", observer.Snapshot());
	}

	[Fact]
	public async Task SuccessfulResult_StoresTheClaudeSessionId_AndClearsTheAuthError()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Result("new"));

		await _h.Run(node, "Hi", observer).Within();

		Assert.Equal("new", node.ClaudeSessionId);
		Assert.Equal(["user:Work", "authError:False", "sessionId", "resultEnded", "afterResponse", "beforeIdle", "idle"], observer.Snapshot());
	}

	[Fact]
	public async Task SuccessfulResultWithoutSessionId_KeepsTheStoredOne()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.Result(sessionId: null));

		await _h.Run(node, "Hi", observer).Within();

		Assert.Equal("old", node.ClaudeSessionId);
		Assert.DoesNotContain("sessionId", observer.Snapshot());
	}

	[Fact]
	public async Task TokensAndCostOfTheResult_AreNotCapturedByTheInteractivePath()
	{
		// The headless path records usage (SessionTurnServiceTests); the interactive one never did. Stage 3 adds turn.usage on top.
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result("s", inputTokens: 1200, outputTokens: 340, cost: 0.12));

		var outcome = await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task CompletionIsRaisedAfterTheLastEntry_WhileTheTurnLockIsStillHeld_ThenTheLockIsReleased()
	{
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		var completion = Assert.Single(_h.Files.Completions);
		Assert.Equal(0, completion.LockCount);
		Assert.Equal(1, _h.Turns.GetTurnLock(TurnEngineHarness.NodeId).CurrentCount);
		Assert.Equal("completed:cancelled=False:error=null", _h.Files.Log[^1]);
	}
}
