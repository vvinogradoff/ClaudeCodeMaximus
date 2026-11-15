using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>Tests for the Low findings 1, 2, 3 and 5 of the final P1.I review.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FinalReviewFixTests
{
	// -- #1 parsers never throw on an odd line ---------------------------------------------------------------------------------------

	[Theory]
	[InlineData("""{"type":"assistant","message":{"content":["a string",5,null,{"type":5},{"type":"tool_use","id":7,"name":["x"],"input":3}]}}""")]
	[InlineData("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":{"a":1}}]}}""")]
	[InlineData("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":9,"content":{"x":1},"is_error":"yes"}]}}""")]
	[InlineData("""{"type":"assistant","message":{"content":"a plain string with tool_use"}}""")]
	[InlineData("""{"type":"assistant","message":"tool_use"}""")]
	[InlineData("""{"type":7,"message":{"content":[]}}""")]
	public void OddLines_NeverThrow_InAnyParserEntryPoint(string line)
	{
		var ex = Record.Exception(() =>
		{
			ClaudeActivityEventParser.Parse(line);
			ClaudeStreamEventParser.TryParseEvent(line);
			ClaudeStreamEventParser.ParseEvents(line);
		});

		Assert.Null(ex);
	}

	[Theory]
	[InlineData("""{"type":"result","is_error":"yes"}""")]
	[InlineData("""{"type":"result","usage":{"input_tokens":"many"},"total_cost_usd":"free"}""")]
	[InlineData("""{"type":"result","subtype":5}""")]
	[InlineData("""{"type":"system","summary":{"a":1}}""")]
	public void OddPrimaryLines_YieldNoEvent_InsteadOfThrowing(string line)
	{
		Assert.Empty(ClaudeStreamEventParser.ParseEvents(line).Where(e => e.Type == "never"));
		Assert.Null(Record.Exception(() => ClaudeStreamEventParser.TryParseEvent(line)));
	}

	[Fact]
	public void AnOddBlock_DoesNotHideItsWellFormedNeighbours_WhenTheLineStillParses()
	{
		const string line = """{"type":"assistant","message":{"content":["junk",{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"ls"}}]}}""";

		var evt = Assert.Single(ClaudeActivityEventParser.Parse(line));

		Assert.Equal(("t1", "Bash", "ls"), (evt.ToolUseId, evt.ToolName, evt.ToolSummary));
	}

	// -- #5 a turn that never ran stays off the relay -----------------------------------------------------------------------------------

	private static RemoteNotification Completed(string json) => new("turn.completed", JsonNode.Parse(json)!);

	[Fact]
	public void TheRelayFilter_DropsTheTurnStreamAndANeverRanCompletion_ButKeepsEverythingElse()
	{
		Assert.All(RemoteProtocol.TurnStream.LocalOnlyEvents, m => Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(new RemoteNotification(m, new JsonObject()))));
		Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(Completed("""{"nodeId":"n","cancelled":true,"turnId":"t","neverRan":true}""")));

		Assert.False(RemoteProtocol.TurnStream.IsLocalOnly(Completed("""{"nodeId":"n","cancelled":false}""")));
		Assert.False(RemoteProtocol.TurnStream.IsLocalOnly(Completed("""{"nodeId":"n","cancelled":true,"turnId":"t"}""")));
		Assert.False(RemoteProtocol.TurnStream.IsLocalOnly(new RemoteNotification("session.changed", new JsonObject())));
	}

	[Fact]
	public void ANeverRanCompletionFromThePublisher_IsMarked_ARanOneIsNotPublishedByIt()
	{
		using var bus = new RemoteEventBus();
		var published = new List<RemoteNotification>();
		bus.Published += published.Add;
		using var sut = new TurnEventPublisher(bus, TimeSpan.FromMinutes(1));

		sut.Publish(new TurnFinishedProgress("n", "t1", Cancelled: true, null, Ran: false));

		var completed = Assert.Single(published);
		Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(completed));
		Assert.True(completed.Params["neverRan"]!.GetValue<bool>());
	}

	[Fact]
	public void APhoneStartedHeadlessTurnsCompletion_ThroughTheFileService_IsStillRelayed()
	{
		// RemoteNotificationPublisher builds it without NeverRan: the web's turn.completed is unchanged.
		var payload = System.Text.Json.JsonSerializer.SerializeToNode(new TurnCompletedPayload("n", false, null, "t"), TdcpJsonOptions.Default)!;

		Assert.False(RemoteProtocol.TurnStream.IsLocalOnly(new RemoteNotification("turn.completed", payload)));
		Assert.Null(payload["neverRan"]);
	}

	// -- #3 late items of a finished turn --------------------------------------------------------------------------------------------

	[Fact]
	public async Task ALateItemOfAFinishedTurn_IsIgnored_NoStrayDeltaNoNewChannel()
	{
		using var bus = new RemoteEventBus();
		var published = new List<RemoteNotification>();
		bus.Published += n => { lock (published) published.Add(n); };
		using var sut = new TurnEventPublisher(bus, TimeSpan.FromMilliseconds(10));
		sut.Publish(new TurnStartedProgress("n", "t1", null));
		sut.Publish(new TurnFinishedProgress("n", "t1", false, null, Ran: true));

		sut.Publish(new TurnDeltaProgress("n", "t1", TurnDeltaKind.Text, "late"));
		sut.Publish(new TurnToolProgress("n", "t1", "x", "Bash", TurnToolStatus.Started, null, null));
		await Task.Delay(120);

		lock (published) Assert.Equal(["turn.started"], published.Select(p => p.Method));
	}

	[Fact]
	public void TheClosedTurnMemory_IsBounded_AndOldIdsAreForgotten()
	{
		using var bus = new RemoteEventBus();
		var published = new List<RemoteNotification>();
		bus.Published += published.Add;
		using var sut = new TurnEventPublisher(bus, TimeSpan.FromMinutes(1));

		for (var i = 0; i <= Constants.Turns.Limits.ClosedTurnsRemembered; i++)
			sut.Publish(new TurnFinishedProgress("n", "t" + i, false, null, Ran: true));

		sut.Publish(new TurnDeltaProgress("n", "t0", TurnDeltaKind.Text, "forgotten id works again"));
		sut.Publish(new TurnFinishedProgress("n", "t0", false, null, Ran: true));

		Assert.Single(published, p => p.Method == "turn.delta");
	}

	// -- #2 a disposed client is not kept alive by the engine -----------------------------------------------------------------------

	[Fact]
	public async Task AReleasedObserver_IsNoLongerToldAboutTheNodesIdleTransition()
	{
		using var h = new TurnEngineHarness();
		var observer = new RecordingTurnObserver();
		h.Process.ScriptEvents(Evt.Result());
		await h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		h.Engine.ReleaseObserver(TurnEngineHarness.NodeId, observer);
		h.Process.ScriptEvents(Evt.Result());
		await h.Run(TurnEngineHarness.Node(), "remote").Within();

		Assert.Equal(1, observer.Snapshot().Count(c => c == "idle"));
	}

	[Fact]
	public async Task ReleasingAnotherObserver_KeepsTheCurrentOne()
	{
		using var h = new TurnEngineHarness();
		var current = new RecordingTurnObserver();
		h.Process.ScriptEvents(Evt.Result());
		await h.Run(TurnEngineHarness.Node(), "Hi", current).Within();

		h.Engine.ReleaseObserver(TurnEngineHarness.NodeId, new RecordingTurnObserver());
		h.Process.ScriptEvents(Evt.Result());
		await h.Run(TurnEngineHarness.Node(), "remote").Within();

		Assert.Equal(2, current.Snapshot().Count(c => c == "idle"));
	}
}
