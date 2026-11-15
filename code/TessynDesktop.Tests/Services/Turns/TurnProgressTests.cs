using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>Sink that keeps every progress item it is handed.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingProgressSink : ITurnProgressSink
{
	private readonly object _gate = new();
	private readonly List<TurnProgress> _items = [];

	public IReadOnlyList<TurnProgress> Items
	{
		get { lock (_gate) return [.. _items]; }
	}

	public void Publish(TurnProgress progress)
	{
		lock (_gate) _items.Add(progress);
	}

	/// <summary>The items as short readable lines.</summary>
	public IReadOnlyList<string> Lines() => [.. Items.Select(Describe)];

	public static string Describe(TurnProgress p) => p switch
	{
		TurnStartedProgress s => $"started:{s.ClientRequestId ?? "-"}",
		TurnStatusProgress s => $"status:{s.State}{(s.Detail == null ? string.Empty : ":" + s.Detail)}",
		TurnDeltaProgress d => $"delta:{d.Kind}:{d.Text}",
		TurnToolProgress t => $"tool:{t.Status}:{t.Name}:{t.ToolUseId}:{t.Summary ?? "-"}{(t.IsError == true ? ":error" : string.Empty)}",
		TurnUsageProgress u => $"usage:{u.InputTokens}/{u.OutputTokens}/{u.CostUsd}",
		TurnFinishedProgress f => $"finished:cancelled={f.Cancelled}:error={f.Error ?? "null"}:ran={f.Ran}",
		_ => p.GetType().Name,
	};
}

/// <summary>What an engine turn reports to the progress sinks (P1.I stage 3), and that the turn id reaches the completion.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnProgressTests : IDisposable
{
	private readonly RecordingProgressSink _sink = new();
	private readonly TurnEngineHarness _h;

	public TurnProgressTests() => _h = new TurnEngineHarness(sinks: [_sink]);

	public void Dispose() => _h.Dispose();

	private static ClaudeStreamEvent ToolUse(string id, string name, string? summary = null) =>
		new() { Type = "tool_use", ToolUseId = id, ToolName = name, ToolSummary = summary };

	private static ClaudeStreamEvent ToolResult(string id, string? summary = null, bool isError = false) =>
		new() { Type = "tool_result", ToolUseId = id, ToolSummary = summary, ToolIsError = isError };

	private static ClaudeStreamEvent Thinking(string text) => new() { Type = "thinking", Content = text };

	[Fact]
	public async Task ATurn_ReportsStartedRunningDeltasToolsUsageAndFinished_InOrder()
	{
		_h.Process.ScriptEvents(
			Thinking("hmm"), Evt.Assistant("Looking."), ToolUse("t1", "Read", "a.txt"), ToolResult("t1", "contents"),
			Evt.Assistant("Done."), Evt.Result("s1", inputTokens: 11, outputTokens: 22, cost: 0.5));

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(
			["started:-", "status:Running", "delta:Thinking:hmm", "delta:Text:Looking.", "tool:Started:Read:t1:a.txt", "tool:Result:Read:t1:contents",
			 "delta:Text:Done.", "usage:11/22/0.5", "finished:cancelled=False:error=null:ran=True"],
			_sink.Lines());
	}

	[Fact]
	public async Task AllItemsCarryTheSameNodeAndTurnId_TheOneInTheOutcome_AndTheCompletion()
	{
		_h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		var outcome = await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.All(_sink.Items, p => Assert.Equal((TurnEngineHarness.NodeId, outcome.TurnId), (p.NodeId, p.TurnId)));
		Assert.Equal([outcome.TurnId], _h.Files.CompletionTurnIds);
	}

	[Fact]
	public async Task ACallersTurnIdAndClientRequestId_AreUsedAndEchoedByStarted()
	{
		_h.Process.ScriptEvents(Evt.Result());

		var outcome = await _h.Engine.RunAsync(new TurnRequest(
			TurnEngineHarness.Node(), "Hi", TurnEngineHarness.Chosen(), IsNewBranch: false, TurnId: "my-turn", ClientRequestId: "req-7")).Within();

		Assert.Equal("my-turn", outcome.TurnId);
		Assert.Equal("started:req-7", _sink.Lines()[0]);
		Assert.Equal(["my-turn"], _h.Files.CompletionTurnIds);
	}

	[Fact]
	public async Task ToolResultWithoutItsCall_IsNamedGenerically_AndAnErrorIsFlagged()
	{
		_h.Process.ScriptEvents(ToolResult("orphan", "oops", isError: true), Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Contains("tool:Result:tool:orphan:oops:error", _sink.Lines());
	}

	[Fact]
	public async Task AResultWithoutUsage_AndAnErrorResult_ReportNoUsage()
	{
		_h.Process.ScriptEvents(Evt.ResultError("Usage limit reached"));

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.DoesNotContain(_sink.Items, p => p is TurnUsageProgress);
		Assert.Equal("finished:cancelled=False:error=Usage limit reached:ran=True", _sink.Lines()[^1]);
	}

	[Fact]
	public async Task EventsNotOfTheInteractivePathsConcern_AreStillPersistedExactlyAsBefore()
	{
		_h.Process.ScriptEvents(ToolUse("t1", "Bash"), Evt.Assistant("a"), Evt.Result());

		await _h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "completed:cancelled=False:error=null"], _h.Files.Log);
	}

	[Fact]
	public async Task AQueuedTurn_ReportsQueuedThenRunning_AfterTheFirstEnds()
	{
		var node = TurnEngineHarness.Node();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_h.Process.Script(async (_, _, _) => await gate.Task);
		_h.Process.ScriptEvents(Evt.Assistant("second"));

		var first = _h.Run(node, "one");
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two");

		var secondLines = () => _sink.Items.Where(p => p.TurnId != Id(_sink.Items[0])).Select(RecordingProgressSink.Describe).ToList();
		await Waits.Until(() => secondLines().Contains("status:Queued"));
		Assert.DoesNotContain("status:Running", secondLines());

		gate.SetResult();
		await first.Within();
		await second.Within();

		Assert.Equal(["started:-", "status:Queued", "status:Running", "delta:Text:second", "finished:cancelled=False:error=null:ran=True"], secondLines());
	}

	private static string Id(TurnProgress p) => p.TurnId;

	[Fact]
	public async Task ARetry_ReportsRetryingWithItsReason_ThenRunningAgain()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Process.ScriptEvents(Evt.ResultError("No conversation found with session ID old"));
		_h.Process.ScriptEvents(Evt.Result("new"));

		await _h.Run(node).Within();

		Assert.Equal(["started:-", "status:Running", "status:Retrying:context", "status:Running", "finished:cancelled=False:error=null:ran=True"], _sink.Lines());
	}

	[Fact]
	public async Task AnAuthRaceRetry_ReportsRetryingWithTheAuthReason()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		_h.Process.ScriptEvents(Evt.ResultError(Constants.Auth.OAuthRefreshRaceMarker));
		_h.Process.ScriptEvents(Evt.Result("s1"));

		await _h.Run(node).Within();

		Assert.Contains("status:Retrying:auth", _sink.Lines());
		Assert.Equal("status:Running", _sink.Lines()[_sink.Lines().ToList().IndexOf("status:Retrying:auth") + 1]);
	}

	[Fact]
	public async Task AStoppedTurn_ReportsFinishedCancelled_AfterItsLastNotification()
	{
		_h.Process.Script(async (_, onEvent, ct) =>
		{
			onEvent(Evt.Assistant("partial"));
			await Task.Delay(Timeout.Infinite, ct);
		});

		var running = _h.Run(TurnEngineHarness.Node());
		await Waits.Until(() => _sink.Lines().Contains("delta:Text:partial"));
		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		await running.Within();

		Assert.Equal("finished:cancelled=True:error=null:ran=True", _sink.Lines()[^1]);
		Assert.Equal([_sink.Items[0].TurnId], _h.Files.CompletionTurnIds);
	}

	[Fact]
	public async Task AQueuedTurnStoppedBeforeItRan_ReportsFinishedWithRanFalse_AndNoFileServiceCompletion()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var first = _h.Run(node, "one");
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two");
		await Waits.Until(() => _sink.Lines().Contains("status:Queued"));

		_h.Engine.CancelRuns(TurnEngineHarness.NodeId);
		await first.Within();
		var secondOutcome = await second.Within();

		var secondFinished = _sink.Items.OfType<TurnFinishedProgress>().Single(f => f.TurnId == secondOutcome.TurnId);
		Assert.Equal((true, false), (secondFinished.Cancelled, secondFinished.Ran));
		Assert.Single(_h.Files.CompletionTurnIds);
	}

	[Fact]
	public async Task AFailureBeforeTheDispatch_ReportsFinishedWithItsMessage_AndRanFalse()
	{
		_h.Settings.Settings.AgentToolsEnabled = true;
		_h.Mcp.Failure = new InvalidOperationException("mcp config failed");

		await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Run(TurnEngineHarness.Node()).Within());

		var finished = Assert.IsType<TurnFinishedProgress>(_sink.Items[^1]);
		Assert.Equal(("mcp config failed", false), (finished.Error, finished.Ran));
	}

	[Fact]
	public async Task AFailureDuringTheDispatch_IsFinishedOnce_AsATurnThatRan()
	{
		_h.Process.Script((_, _, _) => throw new InvalidOperationException("spawn failed"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Run(TurnEngineHarness.Node()).Within());

		Assert.Single(_sink.Items.OfType<TurnFinishedProgress>());
		Assert.True(_sink.Items.OfType<TurnFinishedProgress>().Single().Ran);
	}

	[Fact]
	public async Task EventsThatArriveAfterTheTurnEnded_AreNotReportedUnderItsId()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(Evt.Result());
		await _h.Run(node).Within();
		var before = _sink.Items.Count;

		// A background notification of the still-alive streaming process reaches the finished turn's handler.
		_h.Streaming.EmitToPrimary(Evt.Assistant("a late background answer"));

		Assert.Equal(before, _sink.Items.Count);
		Assert.Contains("append:ASSISTANT:a late background answer", _h.Files.Log);
	}

	[Fact]
	public async Task AThrowingSink_NeverBreaksTheTurn()
	{
		using var h = new TurnEngineHarness(sinks: [new ThrowingSink(), _sink]);
		h.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		await h.Run(TurnEngineHarness.Node()).Within();

		Assert.Equal(["append:USER:Hi", "append:ASSISTANT:a", "completed:cancelled=False:error=null"], h.Files.Log);
		Assert.Contains("delta:Text:a", _sink.Lines());
	}

	[Fact]
	public async Task StreamingTurn_ReportsLikeAOneShotTurn()
	{
		_h.Streaming.ScriptEvents(Evt.Assistant("hi"), ToolUse("t", "Bash", "ls"), Evt.Result("s"));

		await _h.Run(TurnEngineHarness.Node(streaming: true)).Within();

		Assert.Equal(
			["started:-", "status:Running", "delta:Text:hi", "tool:Started:Bash:t:ls", "finished:cancelled=False:error=null:ran=True"], _sink.Lines());
	}

	private sealed class ThrowingSink : ITurnProgressSink
	{
		public void Publish(TurnProgress progress) => throw new InvalidOperationException("sink failed");
	}
}
