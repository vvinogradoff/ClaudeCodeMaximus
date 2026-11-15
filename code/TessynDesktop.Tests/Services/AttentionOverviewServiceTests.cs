using System;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Package J4: <see cref="AttentionOverviewService"/> behind <c>attention.listAll</c>, over a real Attention Ledger.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionOverviewServiceTests : IDisposable
{
	private readonly ImportTestEnv _env = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly AttentionOverviewService _sut;
	private readonly SessionNodeModel _a;
	private readonly SessionNodeModel _b;

	public AttentionOverviewServiceTests()
	{
		var directory = _env.AddDirectory("proj");
		_a   = _env.Registry.AddSession(directory, "Alpha");
		_b   = _env.Registry.AddSession(directory, "Beta");
		_sut = new AttentionOverviewService(new RemoteTreeReader(_env.Settings), _attention.Service);
	}

	public void Dispose()
	{
		_env.Dispose();
		_attention.Dispose();
	}

	private AttentionItemModel Post(SessionNodeModel node, string title, AttentionKind kind = AttentionKind.Ask) =>
		_attention.Service.Post(node, [AttentionTestEnv.Req(title, kind)]).Single().Item;

	private string[] Titles(Models.Remote.AttentionListAllResult result) => [.. result.Items.Select(i => i.Item.Title)];

	[Fact]
	public void TheItemsOfAllSessions_AreListed_MostUrgentFirst_ThenNewest()
	{
		Post(_a, "old ask");
		_attention.Clock.Advance(TimeSpan.FromMinutes(1));
		Post(_b, "new ask");
		_attention.Clock.Advance(TimeSpan.FromMinutes(1));
		Post(_a, "critical", AttentionKind.Critical);
		Post(_b, "todo", AttentionKind.Todo);

		var result = _sut.ListAll(null, null, null, null);

		Assert.Equal(["critical", "new ask", "old ask", "todo"], Titles(result));
		Assert.Equal([_a.NodeId, _b.NodeId, _a.NodeId, _b.NodeId], result.Items.Select(i => i.NodeId));
		Assert.Equal(["Alpha", "Beta", "Alpha", "Beta"], result.Items.Select(i => i.SessionName));
		Assert.Null(result.NextAfterId);
	}

	[Fact]
	public void ByDefaultOnlyOpenItemsAreListed_AClosedOneNeedsItsStatus()
	{
		var open = Post(_a, "open one");
		var closed = Post(_a, "closed one");
		_attention.Service.CloseByUser(_a, closed.Id);

		Assert.Equal(["open one"], Titles(_sut.ListAll(null, null, null, null)));
		Assert.Equal(["closed one"], Titles(_sut.ListAll("dismissed", null, null, null)));
		Assert.Empty(_sut.ListAll("Resolved", null, null, null).Items);
		Assert.NotNull(open);
	}

	[Fact]
	public void TheKinds_NarrowTheList_IgnoringCase()
	{
		Post(_a, "ask");
		Post(_a, "alert", AttentionKind.Alert);
		Post(_b, "todo", AttentionKind.Todo);

		var result = _sut.ListAll(null, ["Alert", "todo"], null, null);

		Assert.Equal(["todo", "alert"], Titles(result));   // urgency order: critical, ask, todo, alert, decided
	}

	[Fact]
	public void Paging_FollowsTheCursor_AcrossSessions_UntilTheLastPage()
	{
		for (var i = 0; i < 3; i++)
		{
			Post(_a, "a" + i);
			Post(_b, "b" + i);
			_attention.Clock.Advance(TimeSpan.FromSeconds(1));
		}

		var first = _sut.ListAll(null, null, 4, null);
		var second = _sut.ListAll(null, null, 4, first.NextAfterId);

		Assert.Equal(4, first.Items.Count);
		Assert.NotNull(first.NextAfterId);
		Assert.Equal(2, second.Items.Count);
		Assert.Null(second.NextAfterId);
		Assert.Equal(6, Titles(first).Concat(Titles(second)).Distinct().Count());   // every item exactly once
	}

	[Fact]
	public void TheLimit_IsClamped()
	{
		for (var i = 0; i < 3; i++)
			Post(_a, "x" + i);

		Assert.Single(_sut.ListAll(null, null, 0, null).Items);
		Assert.Equal(3, _sut.ListAll(null, null, 100_000, null).Items.Count);
	}

	[Theory]
	[InlineData("closed")]
	[InlineData("123")]
	public void AnUnknownStatus_IsRefused(string status) =>
		Assert.Throws<TdcpInvalidParamsException>(() => _sut.ListAll(status, null, null, null));

	[Fact]
	public void AnUnknownKind_IsRefused() =>
		Assert.Throws<TdcpInvalidParamsException>(() => _sut.ListAll(null, ["ask", "urgent"], null, null));

	[Fact]
	public void ACursorThatIsNotInTheList_IsRefused()
	{
		Post(_a, "x");

		Assert.Throws<TdcpInvalidParamsException>(() => _sut.ListAll(null, null, null, "nobody/A9"));
	}

	[Fact]
	public void ASessionWithoutItems_IsLeftOut_AndAnEmptyTreeGivesAnEmptyList()
	{
		Assert.Empty(_sut.ListAll(null, null, null, null).Items);
		Post(_b, "only");

		Assert.Equal([_b.NodeId], _sut.ListAll(null, null, null, null).Items.Select(i => i.NodeId));
	}

	[Fact]
	public void TheDto_CarriesTheItemAsTheSessionLedgerShowsIt()
	{
		_attention.Service.Post(_a, [AttentionTestEnv.Req("Which?", AttentionKind.Ask, body: "details", options: ["x", "y"], recommendation: "x")]);

		var item = Assert.Single(_sut.ListAll(null, null, null, null).Items).Item;

		Assert.Equal(("A1", 1, "Which?", "details", "x"), (item.Id, item.Number, item.Title, item.Body, item.Recommendation));
		Assert.Equal(["x", "y"], item.Options);
		Assert.Equal(Models.AttentionStatus.Open, item.Status);
	}
}
