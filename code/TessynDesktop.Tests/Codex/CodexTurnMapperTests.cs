using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Events;
using TessynDesktop.Tests.Codex.Fakes;

namespace TessynDesktop.Tests.Codex;

/// <summary><see cref="CodexTurnMapper"/> over the canned turn scripts (the FR.33.11 table and FR.33.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnMapperTests
{
	private const string Thread = "thread-1";
	private const string Turn   = "turn-1";

	private readonly FakeCodexScripts _scripts = new();

	// TC.CX.14
	[Fact]
	public void TextTurn_GivesDeltas_ThenOneAssistantMessagePerCompletedAgentMessage()
	{
		var events = Map(_scripts.TextTurn(Thread, Turn, "First note.", "The answer."));

		var deltas = events.OfType<CodexTextDeltaEvent>().Select(e => e.Text).ToList();
		Assert.Equal("First note.The answer.", string.Concat(deltas));

		var messages = events.OfType<CodexAssistantMessageEvent>().ToList();
		Assert.Equal(["First note.", "The answer."], messages.Select(m => m.Text));
		Assert.Equal(["commentary", "final_answer"], messages.Select(m => m.Phase));
		Assert.All(messages, m => Assert.False(m.Interrupted));
		Assert.IsType<CodexTurnCompletedEvent>(events[^1]);
	}

	// TC.CX.15
	[Fact]
	public void Completion_JoinsTheTexts_AndSumsTheLastBreakdowns_WithReasoningInOutput()
	{
		var mapper = new CodexTurnMapper(Thread, Turn);
		var events = Map(_scripts.TextTurn(Thread, Turn, "One.", "Two."), mapper);

		var done = Assert.IsType<CodexTurnCompletedEvent>(events[^1]);
		Assert.Equal(CodexTurnOutcome.Completed, done.Outcome);
		Assert.Equal("One.\n\nTwo.", done.AssistantText);
		Assert.Equal(15600 + 15601, done.InputTokens);
		Assert.Equal((40 + 10) + (41 + 10), done.OutputTokens);
		Assert.Equal(475000, done.ContextWindow);
		Assert.Null(done.Error);
		Assert.Same(done, mapper.Completion);

		var usage = events.OfType<CodexUsageEvent>().First();
		Assert.Equal(new CodexUsageEvent(15600, 50, 475000), usage);
	}

	// TC.CX.14
	[Fact]
	public void ToolTurn_GivesToolLines_WithTheOneLineSummaries()
	{
		var events = Map(_scripts.ToolTurn(Thread, Turn));

		var started = events.OfType<CodexToolStartedEvent>().Select(e => (e.Kind, e.Summary)).ToList();
		Assert.Equal(
		[
			(CodexToolKind.McpTool, "cmx.post_attention"),
			(CodexToolKind.Command, "$ cmd /c echo spike-cmd-ok"),
			(CodexToolKind.FileChange, @"edit 2 file(s): C:\Proj\Alpha\spike-edit.txt"),
			(CodexToolKind.Command, "$ Get-Content missing.txt"),
			(CodexToolKind.WebSearch, "search \"codex app-server\""),
			(CodexToolKind.ImageView, @"view C:\Proj\Alpha\shot.png"),
		], started);

		var finished = events.OfType<CodexToolFinishedEvent>().Select(e => (e.Summary, e.Succeeded)).ToList();
		Assert.Contains(("$ cmd /c echo spike-cmd-ok", true), finished);
		Assert.Contains(("$ Get-Content missing.txt (exit 1)", false), finished);
		Assert.Contains(("cmx.post_attention", true), finished);
		Assert.Equal(6, finished.Count);
	}

	// TC.CX.14
	[Fact]
	public void ToolTurn_GivesPlanProgress_Thinking_AndAnUnmappedItemLine()
	{
		var events = Map(_scripts.ToolTurn(Thread, Turn));

		var progress = events.OfType<CodexProgressEvent>().ToList();
		Assert.Contains(new CodexProgressEvent(CodexProgressKind.Plan, "Plan: 1/3 - Run the command"), progress);
		Assert.Contains(new CodexProgressEvent(CodexProgressKind.UnmappedItem, "Codex: collabAgentToolCall"), progress);
		Assert.Equal("Checking the file", Assert.Single(events.OfType<CodexThinkingDeltaEvent>()).Text);
		Assert.Equal(["I'll run the three checks in order.", "All three checks passed."], events.OfType<CodexAssistantMessageEvent>().Select(m => m.Text));
	}

	// TC.CX.14
	[Fact]
	public void CompactionTurn_GivesACompaction_AndTheTurnIdIsLearnedFromTurnStarted()
	{
		var mapper = new CodexTurnMapper(Thread);
		var events = Map(_scripts.CompactionTurn(Thread, "compact-turn"), mapper);

		Assert.Equal("compact-turn", mapper.TurnId);
		Assert.Equal("cc-1", Assert.Single(events.OfType<CodexCompactionEvent>()).ItemId);
		Assert.Equal(CodexTurnOutcome.Completed, mapper.Completion!.Outcome);
	}

	// TC.CX.17
	[Fact]
	public void InterruptedTurn_WritesTheStreamedText_WithAnInterruptedLine()
	{
		var steps = _scripts.StreamingThenHang(Thread, Turn, "Part one. ", "Part two").Where(s => s.Kind == FakeCodexStepKind.Notify).ToList();
		steps.Add(_scripts.TurnCompleted(Thread, Turn, "interrupted"));

		var events = Map(steps);

		var message = Assert.Single(events.OfType<CodexAssistantMessageEvent>());
		Assert.True(message.Interrupted);
		Assert.Equal("Part one. Part two\n[interrupted]", message.Text);
		var done = Assert.IsType<CodexTurnCompletedEvent>(events[^1]);
		Assert.Equal(CodexTurnOutcome.Interrupted, done.Outcome);
		Assert.Equal(message.Text, done.AssistantText);
	}

	// TC.CX.17
	[Fact]
	public void InterruptedTurn_WithoutDeltas_WritesNothing()
	{
		var steps = _scripts.StreamingThenHang(Thread, Turn).Where(s => s.Kind == FakeCodexStepKind.Notify).ToList();
		steps.Add(_scripts.TurnCompleted(Thread, Turn, "interrupted"));

		var events = Map(steps);

		Assert.Empty(events.OfType<CodexAssistantMessageEvent>());
		Assert.Equal(CodexTurnOutcome.Interrupted, ((CodexTurnCompletedEvent)events[^1]).Outcome);
	}

	// TC.CX.20
	[Theory]
	[InlineData("usageLimitExceeded", CodexErrorKind.Limit)]
	[InlineData("rateLimitExceeded", CodexErrorKind.Limit)]
	[InlineData("unauthorized", CodexErrorKind.Auth)]
	[InlineData("modelNotFound", CodexErrorKind.Other)]
	[InlineData("serverOverloaded", CodexErrorKind.Other)]
	public void FailedTurn_IsClassifiedByCodexErrorInfo_AndReportedOnce(string code, CodexErrorKind kind)
	{
		var events = Map(_scripts.FailedTurn(Thread, Turn, code, "You've hit your usage limit."));

		var error = Assert.Single(events.OfType<CodexTurnErrorEvent>());
		Assert.Equal(kind, error.Kind);
		Assert.Equal(code, error.Code);
		Assert.Equal($"Codex error: You've hit your usage limit. ({code})", error.SystemText);
		var done = Assert.IsType<CodexTurnCompletedEvent>(events[^1]);
		Assert.Equal(CodexTurnOutcome.Failed, done.Outcome);
		Assert.Same(error, done.Error);
	}

	// TC.CX.20: the text alone never sets a limit
	[Fact]
	public void ErrorWithoutCodexErrorInfo_MentioningUsageLimit_IsOther()
	{
		var events = Map(_scripts.FailedTurn(Thread, Turn, null, "the usage limit was mentioned but not hit"));

		var error = Assert.Single(events.OfType<CodexTurnErrorEvent>());
		Assert.Equal(CodexErrorKind.Other, error.Kind);
		Assert.Null(error.Code);
		Assert.Equal("Codex error: the usage limit was mentioned but not hit", error.SystemText);
	}

	// TC.CX.20
	[Fact]
	public void AnObjectCodexErrorInfo_IsClassifiedByItsMemberName()
	{
		var info   = new JsonObject { ["httpConnectionFailed"] = new JsonObject { ["httpStatusCode"] = 502 } };
		var events = Map(_scripts.FailedTurn(Thread, Turn, info, "Bad gateway"));

		var error = Assert.Single(events.OfType<CodexTurnErrorEvent>());
		Assert.Equal("httpConnectionFailed", error.Code);
		Assert.Equal(CodexErrorKind.Other, error.Kind);
	}

	// TC.CX.22
	[Fact]
	public void ContextWindowExceeded_WritesTheCompactHint()
	{
		var events = Map(_scripts.FailedTurn(Thread, Turn, "contextWindowExceeded", "too long"));

		var error = Assert.Single(events.OfType<CodexTurnErrorEvent>());
		Assert.Equal(CodexErrorKind.ContextWindowExceeded, error.Kind);
		Assert.Equal("Codex error: the conversation is longer than the model's context window. Compact the session (Compact now) and send again.", error.SystemText);
	}

	// TC.CX.22
	[Fact]
	public void ErrorThatWillRetry_IsOnlyAProgressLine()
	{
		var events = Map(_scripts.RetryThenCompleted(Thread, Turn));

		Assert.Contains(new CodexProgressEvent(CodexProgressKind.Retry, "Codex is retrying: Reconnecting... 1/5"), events);
		Assert.Empty(events.OfType<CodexTurnErrorEvent>());
		Assert.Equal(CodexTurnOutcome.Completed, ((CodexTurnCompletedEvent)events[^1]).Outcome);
	}

	[Fact]
	public void FailedTurnWithoutAnErrorNotification_ReportsTheTurnError()
	{
		var events = Map(
		[
			_scripts.TurnStarted(Thread, Turn),
			_scripts.TurnCompleted(Thread, Turn, "failed", _scripts.TurnError("usageLimitExceeded", "limit")),
		]);

		Assert.Equal(CodexErrorKind.Limit, Assert.Single(events.OfType<CodexTurnErrorEvent>()).Kind);
	}

	[Fact]
	public void NotificationsOfOtherThreadsAndTurns_AreIgnored()
	{
		var mapper = new CodexTurnMapper(Thread, Turn);
		var other  = Map(_scripts.TextTurn("thread-2", "turn-9", "Not mine."), mapper)
			.Concat(Map(_scripts.TextTurn(Thread, "turn-0", "Older turn."), mapper))
			.ToList();

		Assert.Empty(other);
		Assert.False(mapper.IsCompleted);
	}

	[Fact]
	public void AfterCompletion_NothingMoreIsMapped()
	{
		var mapper = new CodexTurnMapper(Thread, Turn);
		Map(_scripts.TextTurn(Thread, Turn, "Done."), mapper);

		Assert.Empty(Map([_scripts.Delta(Thread, Turn, "late", "late text")], mapper));
	}

	[Fact]
	public void AnAgentMessageCompletedWithoutText_UsesItsDeltas()
	{
		var events = Map(
		[
			_scripts.ItemStarted(Thread, Turn, _scripts.AgentMessage("m", "", null)),
			_scripts.Delta(Thread, Turn, "m", "from deltas"),
			_scripts.ItemCompleted(Thread, Turn, _scripts.AgentMessage("m", "", null)),
		]);

		Assert.Equal("from deltas", Assert.Single(events.OfType<CodexAssistantMessageEvent>()).Text);
	}

	[Fact]
	public void ALongCommand_IsCutToOneLine()
	{
		var command = "Write-Output '" + new string('a', 300) + "'\nsecond line";
		var events  = Map([_scripts.ItemStarted(Thread, Turn, _scripts.CommandExecution("c", command, "inProgress", null))]);

		var summary = Assert.Single(events.OfType<CodexToolStartedEvent>()).Summary;
		Assert.Equal(160, summary.Length);
		Assert.EndsWith("...", summary);
		Assert.DoesNotContain('\n', summary);
	}

	private List<CodexTurnEvent> Map(IEnumerable<FakeCodexStep> steps, CodexTurnMapper? mapper = null)
	{
		mapper ??= new CodexTurnMapper(Thread, Turn);
		var events = new List<CodexTurnEvent>();
		foreach (var step in steps.Where(s => s.Kind == FakeCodexStepKind.Notify))
		{
			using var document = JsonDocument.Parse(step.Params!.ToJsonString());
			events.AddRange(mapper.Map(new CodexNotification(step.Method, document.RootElement.Clone())));
		}
		return events;
	}
}
