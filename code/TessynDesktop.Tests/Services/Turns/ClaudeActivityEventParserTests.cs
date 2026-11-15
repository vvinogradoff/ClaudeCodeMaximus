using System;
using System.Linq;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>The activity events of a stream-json line (P1.I stage 3): thinking, tool_use and tool_result, next to the unchanged primary event.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeActivityEventParserTests
{
	private const string ToolUseLine =
		"""{"type":"assistant","message":{"content":[{"type":"text","text":"Reading."},{"type":"tool_use","id":"tu_1","name":"Read","input":{"file_path":"C:\\proj\\a.txt"}}]}}""";

	private const string ToolOnlyLine =
		"""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"tu_2","name":"Bash","input":{"command":"dotnet build\nsecond line","description":"Build"}}]}}""";

	[Fact]
	public void AssistantMessageWithTextAndToolUse_YieldsTheTextEventUnchanged_ThenTheToolEvent()
	{
		var events = ClaudeStreamEventParser.ParseEvents(ToolUseLine);

		Assert.Equal(["assistant", "tool_use"], events.Select(e => e.Type));
		Assert.Equal("Reading.", events[0].Content);
		Assert.Equal(("tu_1", "Read", @"C:\proj\a.txt"), (events[1].ToolUseId, events[1].ToolName, events[1].ToolSummary));
		Assert.Null(events[1].Content);
	}

	[Fact]
	public void ToolUseOnlyMessage_WasDroppedByTheOldParser_NowYieldsTheToolEvent()
	{
		Assert.Null(ClaudeStreamEventParser.TryParseEvent(ToolOnlyLine));

		var events = ClaudeStreamEventParser.ParseEvents(ToolOnlyLine);

		var evt = Assert.Single(events);
		Assert.Equal(("tool_use", "tu_2", "Bash"), (evt.Type, evt.ToolUseId, evt.ToolName));
		Assert.Equal("dotnet build", evt.ToolSummary);
	}

	[Theory]
	[InlineData("""{"command":"ls"}""", "ls")]
	[InlineData("""{"file_path":"a.cs","command":"ignored"}""", "ignored")]
	[InlineData("""{"pattern":"TODO","path":"src"}""", "src")]
	[InlineData("""{"url":"https://example.com"}""", "https://example.com")]
	[InlineData("""{"description":"Explore the repo"}""", "Explore the repo")]
	[InlineData("""{"unknown":"x"}""", null)]
	[InlineData("""{"command":42}""", null)]
	public void ToolSummary_IsTheFirstWellKnownTextInput(string input, string? expected)
	{
		var line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t","name":"X","input":""" + input + """}]}}""";

		Assert.Equal(expected, ClaudeStreamEventParser.ParseEvents(line).Single().ToolSummary);
	}

	[Fact]
	public void LongToolSummary_IsCutAtTheLimit()
	{
		var line = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"t\",\"name\":\"Bash\",\"input\":{\"command\":\"" + new string('x', 500) + "\"}}]}}";

		var summary = ClaudeStreamEventParser.ParseEvents(line).Single().ToolSummary!;

		Assert.Equal(Constants.Turns.Limits.ToolSummaryMaxChars + Constants.Turns.Ellipsis.Length, summary.Length);
		Assert.EndsWith(Constants.Turns.Ellipsis, summary);
	}

	[Fact]
	public void ThinkingBlock_YieldsAThinkingEvent_ABlankOneNothing()
	{
		const string thinking = """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Let me check the file."},{"type":"text","text":"Done."}]}}""";
		const string blank = """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"  ","signature":"abc"}]}}""";

		var events = ClaudeStreamEventParser.ParseEvents(thinking);

		Assert.Equal(["assistant", "thinking"], events.Select(e => e.Type));
		Assert.Equal("Let me check the file.", events[1].Content);
		Assert.Empty(ClaudeStreamEventParser.ParseEvents(blank));
	}

	[Fact]
	public void ToolResult_StringContent_AndTextBlockContent_AreSummarisedByTheirFirstLine()
	{
		const string asString = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"tu_1","content":"first line\nsecond"}]}}""";
		const string asBlocks = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"tu_2","content":[{"type":"text","text":"\n  hello  \nworld"}]}]}}""";

		var one = ClaudeStreamEventParser.ParseEvents(asString).Single();
		var two = ClaudeStreamEventParser.ParseEvents(asBlocks).Single();

		Assert.Equal(("tool_result", "tu_1", "first line", false), (one.Type, one.ToolUseId, one.ToolSummary, one.ToolIsError));
		Assert.Equal(("tu_2", "hello"), (two.ToolUseId, two.ToolSummary));
	}

	[Fact]
	public void ToolResultError_KeepsTheTurnErrorFlagFalse()
	{
		const string line = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"tu_1","content":"boom","is_error":true}]}}""";

		var evt = ClaudeStreamEventParser.ParseEvents(line).Single();

		Assert.True(evt.ToolIsError);
		Assert.False(evt.IsError);
	}

	[Theory]
	[InlineData("""{"type":"user","message":{"role":"user","content":"a plain prompt mentioning tool_use"}}""")]
	[InlineData("""{"type":"system","subtype":"init"}""")]
	[InlineData("""{"type":"assistant","message":{"content":[{"type":"text","text":"talks about \"tool_use\" blocks"}]}}""")]
	[InlineData("not json with tool_use in it")]
	[InlineData("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\"")]
	public void LinesWithoutActivity_YieldNoActivityEvents_AndNeverThrow(string line)
	{
		var activity = ClaudeActivityEventParser.Parse(line);

		Assert.Empty(activity);
	}

	[Fact]
	public void TheUsualLines_AreParsedExactlyAsBefore()
	{
		const string assistant = """{"type":"assistant","message":{"content":[{"type":"text","text":"hi"}]}}""";
		const string result = """{"type":"result","subtype":"success","session_id":"s1","usage":{"input_tokens":3,"output_tokens":4},"total_cost_usd":0.5}""";

		var events = ClaudeStreamEventParser.ParseEvents(assistant).Concat(ClaudeStreamEventParser.ParseEvents(result)).ToList();

		Assert.Equal(["assistant", "result"], events.Select(e => e.Type));
		Assert.Equal(("s1", 3, 4, 0.5), (events[1].SessionId, events[1].InputTokens, events[1].OutputTokens, events[1].CostUsd));
	}

	[Fact]
	public void ToolEventsOfOneMessage_KeepTheirBlockOrder()
	{
		const string line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"a","name":"Read","input":{}},{"type":"tool_use","id":"b","name":"Grep","input":{}}]}}""";

		Assert.Equal(["a", "b"], ClaudeStreamEventParser.ParseEvents(line).Select(e => e.ToolUseId));
	}
}
