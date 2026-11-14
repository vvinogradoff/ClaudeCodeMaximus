using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Safety net for the two transient failures the interactive path retries (P1.I stage 1): "No conversation found" (once, with the history
/// as context) and the ISSUE.010 OAuth refresh race (once, silently, after a short delay) - one-shot and streaming.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineRetryTests : IDisposable
{
	private const string NoConversation = "No conversation found with session ID old";
	private static readonly string _race = "API Error: " + Constants.Auth.OAuthRefreshRaceMarker + ".";

	private readonly TurnEngineHarness _h = new();

	public void Dispose() => _h.Dispose();

	[Fact]
	public async Task NoConversationFound_Result_RelaunchesOnceWithTheHistoryAsContext_AndWritesNoError()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		var observer = new RecordingTurnObserver();
		_h.Files.Seed(Constants.SessionFile.RoleAssistant, "earlier answer");
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.ScriptEvents(Evt.Assistant("recovered"), Evt.Result("new"));

		var outcome = await _h.Run(node, "Hi", observer).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal("old", _h.Process.Calls[0].SessionId);
		Assert.Null(_h.Process.Calls[1].SessionId);
		Assert.Contains("conversation history from a previous session", _h.Process.Calls[1].Message);
		Assert.Contains("[Assistant]: earlier answer", _h.Process.Calls[1].Message);
		Assert.Contains("[Human]: Hi", _h.Process.Calls[1].Message);
		Assert.Equal(
			["append:USER:Hi", "append:ASSISTANT:recovered", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
		Assert.Equal("new", node.ClaudeSessionId);
		Assert.Contains("contextRetrying", observer.Snapshot());
	}

	[Fact]
	public async Task NoConversationFound_ClearsTheStoredSessionIdAtOnce()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		string? seenByRetry = "unset";
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.Script((call, _, _) =>
		{
			seenByRetry = node.ClaudeSessionId;
			return Task.CompletedTask;
		});

		await _h.Run(node).Within();

		Assert.Null(seenByRetry);
		Assert.Null(node.ClaudeSessionId);
	}

	[Fact]
	public async Task NoConversationFound_SystemError_RetriesToo_AndTheStderrEchoIsSuppressed()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(Evt.SystemError(NoConversation), Evt.SystemError("claude exited with code 1: " + NoConversation));
		_h.Process.ScriptEvents(Evt.Result("new"));

		var outcome = await _h.Run(node).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task NoConversationFound_AgainOnTheRetry_IsNotRetriedASecondTime()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));

		var outcome = await _h.Run(node).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task AnotherErrorOnTheContextRetry_IsAnOrdinaryError()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.ScriptEvents(Evt.ResultError("Usage limit reached"));

		var outcome = await _h.Run(node).Within();

		Assert.Equal(["append:USER:Hi", "append:SYSTEM:Usage limit reached", "completed:cancelled=False:error=Usage limit reached"], _h.Files.Log);
		Assert.Equal("Usage limit reached", outcome.ErrorText);
	}

	[Fact]
	public async Task OAuthRefreshRace_Result_RelaunchesOnceAfterTheDelay_WithTheSameSessionAndMessage_WritingNothing()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		_h.Process.ScriptEvents(Evt.ResultError(_race));
		_h.Process.ScriptEvents(Evt.Assistant("fine"), Evt.Result("s1"));

		var watch = Stopwatch.StartNew();
		var outcome = await _h.Run(node).Within();
		watch.Stop();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal(_h.Process.Calls[0], _h.Process.Calls[1]);
		Assert.True(watch.ElapsedMilliseconds >= Constants.Auth.OAuthRefreshRaceRetryDelayMs - 100, $"retried after only {watch.ElapsedMilliseconds} ms");
		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:fine", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task OAuthRefreshRace_SystemError_IsRetriedToo_AndNotWritten()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		_h.Process.ScriptEvents(Evt.SystemError(_race));
		_h.Process.ScriptEvents(Evt.Result("s1"));

		await _h.Run(node).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
	}

	[Fact]
	public async Task OAuthRefreshRace_PersistingOnTheRetry_IsNotRetriedAgain_AndReportsNoError()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		_h.Process.ScriptEvents(Evt.ResultError(_race));
		_h.Process.ScriptEvents(Evt.ResultError(_race));

		var outcome = await _h.Run(node).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task OAuthRefreshRace_ThenNoConversation_RunsTheContextRetryAsAThirdLaunch()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		_h.Process.ScriptEvents(Evt.ResultError(_race));
		_h.Process.ScriptEvents(Evt.ResultError(NoConversation));
		_h.Process.ScriptEvents(Evt.Result("s2"));

		await _h.Run(node).Within();

		Assert.Equal(["s1", "s1", null], _h.Process.Calls.Select(c => c.SessionId));
		Assert.Equal("s2", node.ClaudeSessionId);
	}

	[Fact]
	public async Task Streaming_NoConversationFound_IsNotRetried_ButClearsTheSessionId_AndWritesNothing()
	{
		var node = TurnEngineHarness.Node(sessionId: "old", streaming: true);
		_h.Streaming.ScriptEvents(Evt.ResultError(NoConversation));

		var outcome = await _h.Run(node).Within();

		Assert.Single(_h.Streaming.Sends);
		Assert.Empty(_h.Process.Calls);
		Assert.Null(node.ClaudeSessionId);
		Assert.Equal(["append:USER:Hi", "completed:cancelled=False:error=null"], _h.Files.Log);
		Assert.Null(outcome.ErrorText);
	}

	[Fact]
	public async Task Streaming_OAuthRefreshRace_WritesTheTurnAgainOnceAfterTheDelay()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1", streaming: true);
		_h.Streaming.ScriptEvents(Evt.ResultError(_race));
		_h.Streaming.ScriptEvents(Evt.Assistant("fine"), Evt.Result("s1"));

		await _h.Run(node).Within();

		Assert.Equal(2, _h.Streaming.Sends.Count);
		Assert.Equal(_h.Streaming.Sends[0].Message, _h.Streaming.Sends[1].Message);
		Assert.Single(_h.Streaming.EnsureCalls);
		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:fine", "completed:cancelled=False:error=null"], _h.Files.Log);
	}
}
