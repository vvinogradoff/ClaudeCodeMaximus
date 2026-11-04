using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.FV.02, 03, 04, 11, 12, 13 — <see cref="AttentionService"/> (FR.23.2–FR.23.5, FR.23.8, FR.23.13, FR.23.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionServiceTests : IDisposable
{
	private readonly AttentionTestEnv _env = new();
	private readonly SessionNodeModel _node = AttentionTestEnv.Node();

	public void Dispose() => _env.Dispose();

	private AttentionService Svc => _env.Service;

	private AttentionItemModel Post(string title, AttentionKind kind = AttentionKind.Ask, string? key = null) =>
		Svc.Post(_node, [AttentionTestEnv.Req(title, kind, key)]).Single().Item;

	private AttentionItemModel Get(string id) => Svc.GetItems(_node.NodeId).Single(i => i.Id == id);

	// ── TC.FV.02 ids ────────────────────────────────────────────────────────────

	[Fact]
	public void Post_ThreeItems_GetSequentialIdsAndPersistNextNumber()   // TC.FV.02-1
	{
		var a = Post("one");
		var b = Post("two");
		var c = Post("three");

		Assert.Equal(["A1", "A2", "A3"], new[] { a.Id, b.Id, c.Id });
		Assert.Equal([1, 2, 3], new[] { a.Number, b.Number, c.Number });
		Assert.All(new[] { a, b, c }, i => Assert.Equal(AttentionStatus.Open, i.Status));
		Assert.Equal(4, new AttentionLedgerStore(_env.Root).Load(_node.NodeId).NextNumber);
	}

	[Fact]
	public void Post_AfterClosingTheLast_NeverReusesItsNumber()   // TC.FV.02-2
	{
		Post("one");
		Post("two");
		var third = Post("three");
		Svc.ResolveBySession(_node, third.Id, AttentionStatus.Resolved, "done");

		Assert.Equal("A4", Post("four").Id);
	}

	[Fact]
	public void NewServiceOverSameRoot_ContinuesAtPersistedNextNumber()   // TC.FV.02-3
	{
		Post("one");
		Post("two");

		var restarted = _env.NewServiceOverSameRoot();

		Assert.Equal("A3", restarted.Post(_node, [AttentionTestEnv.Req("three")]).Single().Item.Id);
	}

	[Fact]
	public void Post_TwoNodes_EachStartAtA1()   // TC.FV.02-4
	{
		var other = AttentionTestEnv.Node("node-2");

		Assert.Equal("A1", Post("one").Id);
		Assert.Equal("A1", Svc.Post(other, [AttentionTestEnv.Req("one")]).Single().Item.Id);
	}

	[Fact]
	public void GetItems_OrdersOpenBySeverityThenNumberThenClosedNewestFirst()   // TC.FV.02-5
	{
		Post("a1", AttentionKind.Alert);
		Post("a2", AttentionKind.Critical);
		Post("a3", AttentionKind.Ask);
		Post("a4", AttentionKind.Todo);
		Post("a5", AttentionKind.Ask);
		Post("a6", AttentionKind.Decided);
		Post("a7", AttentionKind.Ask);
		Post("a8", AttentionKind.Ask);
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		Svc.ResolveBySession(_node, "A7", AttentionStatus.Resolved, null);   // closed earlier
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		Svc.ResolveBySession(_node, "A8", AttentionStatus.Resolved, null);   // closed later

		var ids = Svc.GetItems(_node.NodeId).Select(i => i.Id).ToArray();

		Assert.Equal(["A2", "A3", "A5", "A4", "A1", "A6", "A8", "A7"], ids);
	}

	// ── TC.FV.03 dedupe ─────────────────────────────────────────────────────────

	[Fact]
	public void Post_SameKeyDifferentCase_UpdatesInPlace()   // TC.FV.03-1
	{
		var created = Svc.Post(_node, [AttentionTestEnv.Req("Old", AttentionKind.Ask, key: "K", body: "b1")]).Single().Item;
		_env.Clock.Advance(TimeSpan.FromMinutes(5));

		var update = Svc.Post(_node, [AttentionTestEnv.Req(
			"New title", AttentionKind.Critical, key: "k", body: "b2", options: ["x"], recommendation: "x")]).Single();

		Assert.False(update.IsCreated);
		var item = Assert.Single(Svc.GetItems(_node.NodeId));
		Assert.Equal("A1", item.Id);
		Assert.Equal(AttentionKind.Critical, item.Kind);
		Assert.Equal("New title", item.Title);
		Assert.Equal("b2", item.Body);
		Assert.Equal(["x"], item.Options);
		Assert.Equal("x", item.Recommendation);
		Assert.True(item.UpdatedUtc > created.UpdatedUtc);
		Assert.Equal(created.CreatedUtc, item.CreatedUtc);
		Assert.Equal(2, new AttentionLedgerStore(_env.Root).Load(_node.NodeId).NextNumber);
	}

	[Fact]
	public void Post_SameNormalisedTitle_Matches()   // TC.FV.03-2
	{
		Post("deploy switch is open");

		var outcome = Svc.Post(_node, [AttentionTestEnv.Req("  Deploy   switch IS open ")]).Single();

		Assert.False(outcome.IsCreated);
		Assert.Single(Svc.GetItems(_node.NodeId));
	}

	[Fact]
	public void Post_KeyMatchWinsOverTitleMatch()   // TC.FV.03-3
	{
		Post("shared title", key: "other");
		Post("different", key: "K");

		var outcome = Svc.Post(_node, [AttentionTestEnv.Req("shared title", key: "K")]).Single();

		Assert.False(outcome.IsCreated);
		Assert.Equal("A2", outcome.Item.Id);
	}

	[Fact]
	public void Post_ItemsWithoutKeysAndDifferentTitles_StayTwoItems()   // TC.FV.03-4
	{
		Post("first");
		Post("second");

		Assert.Equal(2, Svc.GetItems(_node.NodeId).Count);
	}

	[Fact]
	public void Post_TitleMatchedItemReceivesTheNewKey()   // TC.FV.03-5
	{
		Post("same title");

		Svc.Post(_node, [AttentionTestEnv.Req("same title", key: "fresh-key")]);

		Assert.Equal("fresh-key", Get("A1").Key);
	}

	[Theory]
	[InlineData(AttentionStatus.Answered)]
	[InlineData(AttentionStatus.Resolved)]
	[InlineData(AttentionStatus.Dismissed)]
	public void Post_MatchIsClosed_CreatesNewItem(AttentionStatus closedAs)   // TC.FV.03-6
	{
		Post("same title", key: "K");
		if (closedAs == AttentionStatus.Dismissed)
			Svc.CloseByUser(_node, "A1");
		else if (closedAs == AttentionStatus.Answered)
			Svc.ResolveBySession(_node, "A1", AttentionStatus.Answered, "yes");
		else
			Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "done");

		var outcome = Svc.Post(_node, [AttentionTestEnv.Req("same title", key: "K")]).Single();

		Assert.True(outcome.IsCreated);
		Assert.Equal("A2", outcome.Item.Id);
	}

	[Fact]
	public void Post_SameTitleTwiceInOneCall_CreatesThenUpdates()   // TC.FV.03-7
	{
		var outcomes = Svc.Post(_node, [AttentionTestEnv.Req("dup"), AttentionTestEnv.Req("dup")]);

		Assert.True(outcomes[0].IsCreated);
		Assert.False(outcomes[1].IsCreated);
		Assert.Equal("A1", outcomes[1].Item.Id);
		Assert.Single(Svc.GetItems(_node.NodeId));
	}

	// ── TC.FV.04 session resolve ────────────────────────────────────────────────

	[Fact]
	public void ResolveBySession_Answered_SetsAnswerClosedAndIsCaseInsensitive()   // TC.FV.04-1
	{
		Post("one");
		Post("two");

		var result = Svc.ResolveBySession(_node, "a2", AttentionStatus.Answered, "Use plan B");

		Assert.Equal(AttentionResolveOutcome.Closed, result.Outcome);
		var item = Get("A2");
		Assert.Equal(AttentionStatus.Answered, item.Status);
		Assert.Equal("Use plan B", item.Answer);
		Assert.NotNull(item.ClosedUtc);
		Assert.False(item.IsUserCloseUnreported);
	}

	[Fact]
	public void ResolveBySession_Resolved_SetsNoteAndLeavesAnswerNull()   // TC.FV.04-2
	{
		Post("one");

		Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "no longer needed");

		var item = Get("A1");
		Assert.Equal(AttentionStatus.Resolved, item.Status);
		Assert.Equal("no longer needed", item.ResolutionNote);
		Assert.Null(item.Answer);
	}

	[Fact]
	public void ResolveBySession_AlreadyClosed_ChangesNothingAndStaysSilent()   // TC.FV.04-3
	{
		Post("one");
		Svc.ResolveBySession(_node, "A1", AttentionStatus.Answered, "first");
		var events = 0;
		Svc.AttentionChanged += (_, _) => events++;
		var entriesBefore = _env.Files.Appended.Count;

		var result = Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "second");

		Assert.Equal(AttentionResolveOutcome.AlreadyClosed, result.Outcome);
		Assert.Equal(AttentionStatus.Answered, result.Item!.Status);
		Assert.Equal("first", Get("A1").Answer);
		Assert.Null(Get("A1").ResolutionNote);
		Assert.Equal(entriesBefore, _env.Files.Appended.Count);
		Assert.Equal(0, events);
	}

	[Fact]
	public void ResolveBySession_UnknownId_ReportsUnknown()   // TC.FV.04-4
	{
		Post("one");

		var result = Svc.ResolveBySession(_node, "A99", AttentionStatus.Resolved, null);

		Assert.Equal(AttentionResolveOutcome.Unknown, result.Outcome);
		Assert.Null(result.Item);
	}

	[Fact]
	public void ResolveBySession_LongNote_IsCutTo2000()   // TC.FV.04-5
	{
		Post("one");

		Svc.ResolveBySession(_node, "A1", AttentionStatus.Answered, new string('n', 3000));

		Assert.Equal(2000, Get("A1").Answer!.Length);
	}

	[Fact]
	public void ResolveBySession_EmptyNote_IsAllowed()   // TC.FV.04-6
	{
		Post("one");

		var result = Svc.ResolveBySession(_node, "A1", AttentionStatus.Answered, null);

		Assert.Equal(AttentionResolveOutcome.Closed, result.Outcome);
		Assert.True(string.IsNullOrEmpty(Get("A1").Answer));
	}

	// ── TC.FV.11 user close ─────────────────────────────────────────────────────

	[Fact]
	public void CloseByUser_Todo_ResolvesWithDoneNoteAndWritesEntry()   // TC.FV.11-1
	{
		for (var n = 1; n < 5; n++)
			Post($"filler {n}");
		Post("rotate password", AttentionKind.Todo);   // A5

		Assert.True(Svc.CloseByUser(_node, "A5"));

		var item = Get("A5");
		Assert.Equal(AttentionStatus.Resolved, item.Status);
		Assert.Equal("Marked done by the user", item.ResolutionNote);
		Assert.True(item.IsUserCloseUnreported);
		Assert.NotNull(item.ClosedUtc);
		Assert.Equal("[Attention A5 marked done by the user]", _env.Files.Appended.Last().content);
		Assert.Equal(Constants.SessionFile.RoleSystem, _env.Files.Appended.Last().role);
		Assert.Equal(_node.FileName, _env.Files.Appended.Last().fileName);
	}

	[Theory]
	[InlineData(AttentionKind.Ask)]
	[InlineData(AttentionKind.Critical)]
	[InlineData(AttentionKind.Alert)]
	[InlineData(AttentionKind.Decided)]
	public void CloseByUser_OtherKinds_Dismiss(AttentionKind kind)   // TC.FV.11-2
	{
		Post("something", kind);

		Svc.CloseByUser(_node, "A1");

		var item = Get("A1");
		Assert.Equal(AttentionStatus.Dismissed, item.Status);
		Assert.True(item.IsUserCloseUnreported);
		Assert.Equal("[Attention A1 dismissed by the user]", _env.Files.Appended.Last().content);
	}

	[Fact]
	public void CloseByUser_ClosedItem_ChangesNothingAndWritesNoEntry()   // TC.FV.11-4
	{
		Post("one");
		Svc.CloseByUser(_node, "A1");
		var entries = _env.Files.Appended.Count;

		Assert.False(Svc.CloseByUser(_node, "A1"));
		Assert.False(Svc.CloseByUser(_node, "A9"));

		Assert.Equal(entries, _env.Files.Appended.Count);
	}

	[Fact]
	public void TakeUnreportedUserClosures_ReportsEachClosureOnce()   // TC.FV.11-5
	{
		Post("one");
		Post("two", AttentionKind.Todo);
		Svc.CloseByUser(_node, "A1");
		_env.Clock.Advance(TimeSpan.FromSeconds(1));
		Svc.CloseByUser(_node, "A2");

		var first  = Svc.TakeUnreportedUserClosures(_node.NodeId);
		var second = Svc.TakeUnreportedUserClosures(_node.NodeId);

		Assert.Equal(["A1", "A2"], first.Select(i => i.Id).ToArray());
		Assert.Empty(second);
		Assert.All(Svc.GetItems(_node.NodeId), i => Assert.False(i.IsUserCloseUnreported));
		Assert.All(new AttentionLedgerStore(_env.Root).Load(_node.NodeId).Items, i => Assert.False(i.IsUserCloseUnreported));
	}

	[Fact]
	public void TakeUnreportedUserClosures_SessionClosedItemsAreNeverReported()   // TC.FV.11-6
	{
		Post("one");
		Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "done");

		Assert.Empty(Svc.TakeUnreportedUserClosures(_node.NodeId));
	}

	[Fact]
	public void MarkAnsweredByUser_SetsAnsweredWithoutEntryOrClosure()   // TC.FV.11-7
	{
		Post("one");
		var entries = _env.Files.Appended.Count;

		Svc.MarkAnsweredByUser(_node.NodeId, [new AttentionAnswerModel { ItemId = "A1", Title = "one", Answer = "text sent" }]);

		var item = Get("A1");
		Assert.Equal(AttentionStatus.Answered, item.Status);
		Assert.Equal("text sent", item.Answer);
		Assert.NotNull(item.ClosedUtc);
		Assert.Equal(entries, _env.Files.Appended.Count);
		Assert.Empty(Svc.TakeUnreportedUserClosures(_node.NodeId));
	}

	[Fact]
	public void MarkAnsweredByUser_Batch_SkipsUnknownAndClosedItems()   // TC.FV.11-8
	{
		Post("one");
		Post("two");
		Post("three");
		Svc.ResolveBySession(_node, "A2", AttentionStatus.Resolved, "done");

		Svc.MarkAnsweredByUser(_node.NodeId,
		[
			new AttentionAnswerModel { ItemId = "A1", Answer = "a" },
			new AttentionAnswerModel { ItemId = "A2", Answer = "b" },
			new AttentionAnswerModel { ItemId = "A77", Answer = "c" },
			new AttentionAnswerModel { ItemId = "A3", Answer = "d" },
		]);

		Assert.Equal(AttentionStatus.Answered, Get("A1").Status);
		Assert.Equal(AttentionStatus.Resolved, Get("A2").Status);
		Assert.Null(Get("A2").Answer);
		Assert.Equal(AttentionStatus.Answered, Get("A3").Status);
	}

	// ── TC.FV.12 summary ────────────────────────────────────────────────────────

	[Fact]
	public void GetSummary_EmptyLedger_IsZeroWithNoTopKind()   // TC.FV.12-1
	{
		var summary = Svc.GetSummary(_node.NodeId);

		Assert.Equal(0, summary.OpenCount);
		Assert.Null(summary.TopKind);
	}

	[Fact]
	public void GetSummary_FollowsTheMostSevereOpenKindAsItemsClose()   // TC.FV.12-2
	{
		Post("alert", AttentionKind.Alert);
		Post("todo", AttentionKind.Todo);
		Post("ask", AttentionKind.Ask);
		Post("critical", AttentionKind.Critical);

		var s = Svc.GetSummary(_node.NodeId);
		Assert.Equal((4, AttentionKind.Critical), (s.OpenCount, s.TopKind!.Value));

		Svc.CloseByUser(_node, "A4");
		s = Svc.GetSummary(_node.NodeId);
		Assert.Equal((3, AttentionKind.Ask), (s.OpenCount, s.TopKind!.Value));

		Svc.CloseByUser(_node, "A3");
		s = Svc.GetSummary(_node.NodeId);
		Assert.Equal((2, AttentionKind.Todo), (s.OpenCount, s.TopKind!.Value));

		Svc.CloseByUser(_node, "A2");
		s = Svc.GetSummary(_node.NodeId);
		Assert.Equal((1, AttentionKind.Alert), (s.OpenCount, s.TopKind!.Value));
	}

	[Fact]
	public void GetSummary_DecidedIsNotCounted()   // TC.FV.12-3
	{
		Post("decided", AttentionKind.Decided);
		var s = Svc.GetSummary(_node.NodeId);
		Assert.Equal(0, s.OpenCount);
		Assert.Null(s.TopKind);

		Post("alert", AttentionKind.Alert);
		s = Svc.GetSummary(_node.NodeId);
		Assert.Equal((1, AttentionKind.Alert), (s.OpenCount, s.TopKind!.Value));
	}

	[Fact]
	public void GetSummary_ClosedItemsAreNotCounted()   // TC.FV.12-4
	{
		Post("a");
		Post("b");
		Post("c");
		Svc.ResolveBySession(_node, "A1", AttentionStatus.Answered, "x");
		Svc.ResolveBySession(_node, "A2", AttentionStatus.Resolved, "y");
		Svc.CloseByUser(_node, "A3");

		Assert.Equal(0, Svc.GetSummary(_node.NodeId).OpenCount);
	}

	// ── TC.FV.13 AttentionChanged ───────────────────────────────────────────────

	[Fact]
	public void AttentionChanged_RaisedForEveryKindOfChange()   // TC.FV.13-1
	{
		var nodes = new List<string>();
		Svc.AttentionChanged += (_, e) => nodes.Add(e.NodeId);

		Post("one");                                                         // create
		Post("one");                                                         // update
		Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "x");    // session resolve
		Post("two");
		Svc.CloseByUser(_node, "A2");                                        // user close
		Post("three");
		Svc.MarkAnsweredByUser(_node.NodeId, [new AttentionAnswerModel { ItemId = "A3", Answer = "a" }]);

		Assert.Equal(7, nodes.Count);
		Assert.All(nodes, n => Assert.Equal(_node.NodeId, n));
	}

	[Fact]
	public void AttentionChanged_NotRaisedWhenNothingChanged()   // TC.FV.13-2
	{
		Post("one");
		Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "x");
		var events = 0;
		Svc.AttentionChanged += (_, _) => events++;

		Svc.ResolveBySession(_node, "A1", AttentionStatus.Resolved, "again");
		Svc.ResolveBySession(_node, "A5", AttentionStatus.Resolved, "unknown");
		Svc.CloseByUser(_node, "A1");
		Svc.MarkAnsweredByUser(_node.NodeId, [new AttentionAnswerModel { ItemId = "A1", Answer = "late" }]);
		Svc.Post(_node, []);   // a rejected/empty post

		Assert.Equal(0, events);
	}

	[Fact]
	public void AttentionChanged_RaisedOncePerCall_NotPerItem()   // TC.FV.13-3
	{
		var events = 0;
		Svc.AttentionChanged += (_, _) => events++;

		Svc.Post(_node, [AttentionTestEnv.Req("a"), AttentionTestEnv.Req("b"), AttentionTestEnv.Req("c")]);

		Assert.Equal(1, events);
	}
}
