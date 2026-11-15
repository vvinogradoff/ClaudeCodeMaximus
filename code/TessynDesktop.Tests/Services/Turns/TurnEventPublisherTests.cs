using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>The batching and bounded buffering of the turn stream (P1.I stage 3): 50 ms batches, merged deltas, ordered tool events, drop-oldest deltas.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEventPublisherTests : IDisposable
{
	private const string Node = "node-1";
	private const string Turn = "turn-1";

	private readonly RemoteEventBus _bus = new();
	private readonly List<RemoteNotification> _published = [];

	public TurnEventPublisherTests()
	{
		_bus.Published += n =>
		{
			lock (_published) _published.Add(n);
		};
	}

	public void Dispose() => _bus.Dispose();

	private List<RemoteNotification> Snapshot()
	{
		lock (_published) return [.. _published];
	}

	private static TurnDeltaProgress Delta(string text, TurnDeltaKind kind = TurnDeltaKind.Text) => new(Node, Turn, kind, text);

	private static TurnToolProgress Tool(string id, TurnToolStatus status) => new(Node, Turn, id, "Bash", status, "ls", null);

	private static string Text(RemoteNotification n) => n.Params["text"]!.GetValue<string>();

	private static long Seq(RemoteNotification n) => n.Params["seq"]!.GetValue<long>();

	[Fact]
	public void Started_IsPublishedAtOnce()
	{
		using var sut = new TurnEventPublisher(_bus);

		sut.Publish(new TurnStartedProgress(Node, Turn, "req-1"));

		var started = Assert.Single(Snapshot());
		Assert.Equal("turn.started", started.Method);
		Assert.Equal(("node-1", "turn-1", "req-1"), (
			started.Params["nodeId"]!.GetValue<string>(), started.Params["turnId"]!.GetValue<string>(), started.Params["clientRequestId"]!.GetValue<string>()));
	}

	[Fact]
	public async Task Deltas_AreBatched_ConsecutiveOnesOfAKindMerge_AndNothingGoesOutBeforeTheWindowEnds()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMilliseconds(80));

		sut.Publish(Delta("Hel"));
		sut.Publish(Delta("lo "));
		sut.Publish(Delta("world"));

		Assert.Empty(Snapshot());

		await Waits.Until(() => Snapshot().Count == 1);
		var delta = Snapshot()[0];
		Assert.Equal("turn.delta", delta.Method);
		Assert.Equal("Hello world", Text(delta));
		Assert.Equal(1, Seq(delta));
		Assert.Equal("text", delta.Params["kind"]!.GetValue<string>());
	}

	[Fact]
	public async Task AKindChange_StartsANewDelta_AndToolEventsKeepTheirPlaceBetweenDeltas()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMilliseconds(30));

		sut.Publish(Delta("thinking...", TurnDeltaKind.Thinking));
		sut.Publish(Delta("Let me look. "));
		sut.Publish(Tool("tu_1", TurnToolStatus.Started));
		sut.Publish(Tool("tu_1", TurnToolStatus.Result));
		sut.Publish(Delta("Found it."));

		await Waits.Until(() => Snapshot().Count == 5);

		Assert.Equal(["turn.delta", "turn.delta", "turn.tool", "turn.tool", "turn.delta"], Snapshot().Select(n => n.Method));
		Assert.Equal(["thinking", "text"], Snapshot().Take(2).Select(n => n.Params["kind"]!.GetValue<string>()));
		Assert.Equal([1L, 2L, 3L], new[] { Seq(Snapshot()[0]), Seq(Snapshot()[1]), Seq(Snapshot()[4]) });
		Assert.Equal(["started", "result"], Snapshot().Skip(2).Take(2).Select(n => n.Params["status"]!.GetValue<string>()));
	}

	[Fact]
	public async Task StatusAndUsage_AreBatchedToo_InOrder()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMilliseconds(30));

		sut.Publish(new TurnStatusProgress(Node, Turn, TurnState.Running, null));
		sut.Publish(Delta("x"));
		sut.Publish(new TurnUsageProgress(Node, Turn, 10, 20, 0.25));

		await Waits.Until(() => Snapshot().Count == 3);

		Assert.Equal(["turn.status", "turn.delta", "turn.usage"], Snapshot().Select(n => n.Method));
		Assert.Equal("running", Snapshot()[0].Params["state"]!.GetValue<string>());
		Assert.Equal((10, 20, 0.25), (
			Snapshot()[2].Params["inputTokens"]!.GetValue<int>(), Snapshot()[2].Params["outputTokens"]!.GetValue<int>(), Snapshot()[2].Params["costUsd"]!.GetValue<double>()));
	}

	[Fact]
	public void Finished_FlushesWhatIsBuffered_BeforeItReturns_ForATurnThatRan()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1));

		sut.Publish(Delta("last words"));
		sut.Publish(Tool("tu_9", TurnToolStatus.Started));
		sut.Publish(new TurnFinishedProgress(Node, Turn, false, null, Ran: true));

		Assert.Equal(["turn.delta", "turn.tool"], Snapshot().Select(n => n.Method));
	}

	[Fact]
	public void Finished_ForATurnThatNeverRan_PublishesTheTurnCompletedItselfAfterTheFlush()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1));

		sut.Publish(new TurnStatusProgress(Node, Turn, TurnState.Queued, null));
		sut.Publish(new TurnFinishedProgress(Node, Turn, Cancelled: true, null, Ran: false));

		Assert.Equal(["turn.status", "turn.completed"], Snapshot().Select(n => n.Method));
		var completed = Snapshot()[1].Params;
		Assert.Equal(("node-1", true, "turn-1"), (completed["nodeId"]!.GetValue<string>(), completed["cancelled"]!.GetValue<bool>(), completed["turnId"]!.GetValue<string>()));
	}

	[Fact]
	public void Finished_ForATurnThatRan_LeavesTurnCompletedToTheFileServiceEvent()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1));

		sut.Publish(new TurnFinishedProgress(Node, Turn, false, null, Ran: true));

		Assert.Empty(Snapshot());
	}

	[Fact]
	public void WhenTheBufferOverflows_TheOldestDeltasAreDropped_TheirSeqStaysUsed_AndToolEventsSurvive()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1), maxBufferedItems: 4);

		// Alternating kinds never merge, so each is an item of its own: d1 t d2 t d3 t d4 t d5 (five deltas, two tools in between).
		sut.Publish(Delta("1", TurnDeltaKind.Text));
		sut.Publish(Tool("a", TurnToolStatus.Started));
		sut.Publish(Delta("2", TurnDeltaKind.Thinking));
		sut.Publish(Delta("3", TurnDeltaKind.Text));
		sut.Publish(Tool("a", TurnToolStatus.Result));
		sut.Publish(Delta("4", TurnDeltaKind.Thinking));
		sut.Publish(Delta("5", TurnDeltaKind.Text));
		sut.Publish(new TurnFinishedProgress(Node, Turn, false, null, Ran: true));

		var deltas = Snapshot().Where(n => n.Method == "turn.delta").ToList();
		var tools = Snapshot().Where(n => n.Method == "turn.tool").ToList();

		Assert.Equal(2, tools.Count);
		Assert.True(deltas.Count < 5, "the oldest deltas were dropped");
		Assert.Equal("5", Text(deltas[^1]));
		Assert.Equal(5, Seq(deltas[^1]));
		var seqs = deltas.Select(Seq).ToList();
		Assert.Equal(seqs.OrderBy(s => s), seqs);
		Assert.True(seqs[0] > 1, "a gap in seq tells the client to re-read the entry");
	}

	[Fact]
	public void TheNewestDeltaIsNeverDropped_EvenWhenItAloneExceedsTheCharacterLimit()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1), maxBufferedDeltaChars: 10);

		sut.Publish(Delta(new string('a', 100)));
		sut.Publish(new TurnFinishedProgress(Node, Turn, false, null, Ran: true));

		Assert.Equal(100, Text(Assert.Single(Snapshot())).Length);
	}

	[Fact]
	public void TwoTurnsOfOneSession_KeepSeparateSequences()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1));

		sut.Publish(new TurnDeltaProgress(Node, "t-a", TurnDeltaKind.Text, "a"));
		sut.Publish(new TurnDeltaProgress(Node, "t-b", TurnDeltaKind.Text, "b"));
		sut.Publish(new TurnFinishedProgress(Node, "t-a", false, null, Ran: true));
		sut.Publish(new TurnFinishedProgress(Node, "t-b", false, null, Ran: true));

		Assert.Equal([("t-a", 1L), ("t-b", 1L)], Snapshot().Select(n => (n.Params["turnId"]!.GetValue<string>(), Seq(n))));
	}

	[Fact]
	public void AThrowingBusSubscriber_DoesNotBreakThePublisher()
	{
		_bus.Published += _ => throw new InvalidOperationException("subscriber failed");
		using var sut = new TurnEventPublisher(_bus);

		sut.Publish(new TurnStartedProgress(Node, Turn, null));

		Assert.Single(Snapshot());
	}

	[Fact]
	public void NullFieldsAreOmittedFromThePayload()
	{
		using var sut = new TurnEventPublisher(_bus, TimeSpan.FromMinutes(1));

		sut.Publish(new TurnStartedProgress(Node, Turn, null));
		sut.Publish(new TurnToolProgress(Node, Turn, "t", "Read", TurnToolStatus.Started, null, null));
		sut.Publish(new TurnFinishedProgress(Node, Turn, false, null, Ran: true));

		JsonObject started = Snapshot()[0].Params.AsObject();
		JsonObject tool = Snapshot()[1].Params.AsObject();
		Assert.False(started.ContainsKey("clientRequestId"));
		Assert.False(tool.ContainsKey("summary"));
		Assert.False(tool.ContainsKey("isError"));
	}
}
