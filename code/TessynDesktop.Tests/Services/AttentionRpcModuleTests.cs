using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>attention.list / reply / dismiss (contract sections 3.17 to 3.19) over a real Attention Ledger.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionRpcModuleTests : IDisposable
{
	private readonly RemoteSessionEnv _env = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly RecordingSendService _send = new();
	private readonly AttentionRpcModule _module;

	public AttentionRpcModuleTests()
	{
		_env.Models.Models = [new ClaudeModelInfo("claude-opus-5-5", "opus", "Opus 5.5")];
		_module = new AttentionRpcModule(_env.Registry, _attention.Service, new AttentionReplyService(_attention.Service, _send), _env.Options);
	}

	public void Dispose()
	{
		_env.Dispose();
		_attention.Dispose();
	}

	private SessionNodeModel Node => _env.Session;

	private AttentionItemModel Post(string title, AttentionKind kind = AttentionKind.Ask, string body = "") =>
		_attention.Service.Post(Node, [AttentionTestEnv.Req(title, kind, body: body)]).Single().Item;

	private AttentionItemModel Item(string id) => _attention.Service.GetItems(Node.NodeId).Single(i => i.Id == id);

	private Task<JsonNode?> Call(string method, string extra = "") =>
		RemoteTestHelpers.CallAsync(_module, method, $"{{\"nodeId\":\"{Node.NodeId}\"{(extra.Length == 0 ? string.Empty : "," + extra)}}}");

	// ── attention.list ──────────────────────────────────────────────────────────

	[Fact]
	public async Task List_ReturnsTheLedgerInAttentionOrderWithTheSummary()
	{
		var todo     = Post("todo item", AttentionKind.Todo);
		var critical = Post("critical item", AttentionKind.Critical, body: "details");
		var ask      = Post("ask item", AttentionKind.Ask);

		var json = (await Call("attention.list"))!;

		Assert.Equal(Node.NodeId, (string?)json["nodeId"]);
		Assert.Equal([critical.Id, ask.Id, todo.Id], json["items"]!.AsArray().Select(i => (string)i!["id"]!));
		Assert.Equal(3, (int?)json["summary"]!["openCount"]);
		Assert.Equal("critical", (string?)json["summary"]!["topKind"]);
		var first = json["items"]![0]!;
		Assert.Equal("critical", (string?)first["kind"]);
		Assert.Equal("open", (string?)first["status"]);
		Assert.Equal("details", (string?)first["body"]);
		Assert.Equal(critical.Number, (int?)first["number"]);
	}

	[Fact]
	public async Task List_OmitsAnEmptyBody_AndPutsClosedItemsAfterOpenOnes()
	{
		var dismissed = Post("dismissed one");
		var open      = Post("open one", AttentionKind.Todo);
		_attention.Service.CloseByUser(Node, dismissed.Id);

		var json = (await Call("attention.list"))!;

		Assert.Equal([open.Id, dismissed.Id], json["items"]!.AsArray().Select(i => (string)i!["id"]!));
		Assert.False(json["items"]![0]!.AsObject().ContainsKey("body"));
		Assert.Equal("dismissed", (string?)json["items"]![1]!["status"]);
		Assert.NotNull(json["items"]![1]!["closedUtc"]);
		Assert.Equal(1, (int?)json["summary"]!["openCount"]);
	}

	[Fact]
	public async Task List_AnEmptyLedger_IsEmpty()
	{
		var json = (await Call("attention.list"))!;

		Assert.Empty(json["items"]!.AsArray());
		Assert.Equal(0, (int?)json["summary"]!["openCount"]);
	}

	// ── attention.reply ─────────────────────────────────────────────────────────

	[Fact]
	public async Task Reply_MarksTheItemsAnsweredBeforeTheSend_AndSendsTheDesktopPromptWithLedgerTitles()
	{
		var a = Post("First question");
		var b = Post("Second question", AttentionKind.Todo);
		var statusesAtSend = new List<AttentionStatus>();
		_send.OnSend = () => statusesAtSend.AddRange(_attention.Service.GetItems(Node.NodeId).Select(i => i.Status));

		var json = (await Call("attention.reply",
			$"\"answers\":[{{\"itemId\":\"{a.Id}\",\"text\":\"  yes  \"}},{{\"itemId\":\"{b.Id}\",\"text\":\"line1\\nline2\"}}]"))!;

		Assert.Equal([AttentionStatus.Answered, AttentionStatus.Answered], statusesAtSend);
		Assert.Equal([a.Id, b.Id], json["answeredIds"]!.AsArray().Select(i => (string)i!));
		Assert.Empty(json["skippedIds"]!.AsArray());
		Assert.True((bool?)json["turnStarted"]);

		var expectedPrompt = AttentionPromptBuilder.BuildAnswersPrompt(
		[
			new AttentionAnswerModel { ItemId = a.Id, Title = "First question", Answer = "yes" },
			new AttentionAnswerModel { ItemId = b.Id, Title = "Second question", Answer = "line1\nline2" },
		]);
		var sent = Assert.Single(_send.Sends);
		Assert.Equal(expectedPrompt, sent.Text);
		Assert.Same(Node, sent.Node);
		Assert.Null(sent.Options);

		Assert.Equal("yes", Item(a.Id).Answer);
		Assert.Equal(AttentionStatus.Answered, Item(b.Id).Status);
	}

	[Fact]
	public async Task Reply_SkipsUnknownClosedBlankAndRepeatedItems_AndAnswersTheRest()
	{
		var good    = Post("good");
		var closed  = Post("closed");
		var blank   = Post("blank");
		_attention.Service.CloseByUser(Node, closed.Id);

		var json = (await Call("attention.reply",
			$"\"answers\":[{{\"itemId\":\"{good.Id}\",\"text\":\"ok\"}},{{\"itemId\":\"A99\",\"text\":\"x\"}}," +
			$"{{\"itemId\":\"{closed.Id}\",\"text\":\"x\"}},{{\"itemId\":\"{blank.Id}\",\"text\":\"   \"}},{{\"itemId\":\"{good.Id}\",\"text\":\"again\"}}]"))!;

		Assert.Equal([good.Id], json["answeredIds"]!.AsArray().Select(i => (string)i!));
		Assert.Equal(["A99", closed.Id, blank.Id, good.Id], json["skippedIds"]!.AsArray().Select(i => (string)i!));
		Assert.True((bool?)json["turnStarted"]);
		Assert.Equal(AttentionStatus.Open, Item(blank.Id).Status);
		Assert.Equal(AttentionStatus.Dismissed, Item(closed.Id).Status);
		Assert.Equal("ok", Item(good.Id).Answer);
		Assert.DoesNotContain("again", _send.Sends.Single().Text);
	}

	[Fact]
	public async Task Reply_WithNothingToSend_StartsNoTurnAndMarksNothing()
	{
		var item = Post("blank only");

		var json = (await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"\"}},{{\"itemId\":\"A99\",\"text\":\"x\"}}]"))!;

		Assert.False((bool?)json["turnStarted"]);
		Assert.Empty(json["answeredIds"]!.AsArray());
		Assert.Empty(_send.Sends);
		Assert.Equal(AttentionStatus.Open, Item(item.Id).Status);
	}

	[Fact]
	public async Task Reply_EmptyAnswerList_StartsNoTurn()
	{
		var json = (await Call("attention.reply", "\"answers\":[]"))!;

		Assert.False((bool?)json["turnStarted"]);
		Assert.Empty(_send.Sends);
	}

	[Fact]
	public async Task Reply_DecidedItemsCountLikeOnTheDesktop()
	{
		var decided = Post("decision", AttentionKind.Decided);

		var json = (await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{decided.Id}\",\"text\":\"fine\"}}]"))!;

		Assert.True((bool?)json["turnStarted"]);
		Assert.Equal(AttentionStatus.Answered, Item(decided.Id).Status);
	}

	[Fact]
	public async Task Reply_PassesTheModelEffortAndProfileOptionsToTheSend()
	{
		var item = Post("q");
		_env.Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });

		await Call("attention.reply",
			$"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"a\"}}],\"modelId\":\"claude-opus-5-5\",\"effort\":\"low\",\"profileId\":\"p1\"");

		Assert.Equal(new TurnOptionsModel("claude-opus-5-5", "low", "Work"), _send.Sends.Single().Options);
	}

	[Fact]
	public async Task Reply_InvalidOptions_AreRejectedBeforeAnythingIsMarked()
	{
		var item = Post("q");

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => Call("attention.reply",
			$"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"a\"}}],\"modelId\":\"nope\""));

		Assert.Equal(AttentionStatus.Open, Item(item.Id).Status);
		Assert.Empty(_send.Sends);
	}

	[Theory]
	[InlineData("")]
	[InlineData("\"answers\":\"x\"")]
	[InlineData("\"answers\":[{\"text\":\"x\"}]")]
	[InlineData("\"answers\":[{\"itemId\":\"A1\"}]")]
	[InlineData("\"answers\":[{\"itemId\":\"A1\",\"text\":5}]")]
	[InlineData("\"answers\":[\"A1\"]")]
	public async Task Reply_InvalidAnswers_AreInvalidParams(string extra)
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => Call("attention.reply", extra));
	}

	[Fact]
	public async Task EveryMethod_RejectsAnUnknownSession()
	{
		foreach (var (method, extra) in new[] { ("attention.list", ""), ("attention.reply", ",\"answers\":[]"), ("attention.dismiss", ",\"itemId\":\"A1\"") })
		{
			var error = await Assert.ThrowsAsync<TdcpException>(() =>
				RemoteTestHelpers.CallAsync(_module, method, "{\"nodeId\":\"nope\"" + extra + "}"));
			Assert.Equal("Session not found", error.Message);
		}
	}

	// ── attention.dismiss ───────────────────────────────────────────────────────

	[Fact]
	public async Task Dismiss_ClosesAnOpenItem_ForEveryKind()
	{
		var ask  = Post("ask", AttentionKind.Ask);
		var todo = Post("todo", AttentionKind.Todo);

		var first  = (await Call("attention.dismiss", $"\"itemId\":\"{ask.Id}\""))!;
		var second = (await Call("attention.dismiss", $"\"itemId\":\"{todo.Id}\""))!;

		Assert.True((bool?)first["dismissed"]);
		Assert.True((bool?)second["dismissed"]);
		Assert.Equal(AttentionStatus.Dismissed, Item(ask.Id).Status);
		Assert.Equal(AttentionStatus.Dismissed, Item(todo.Id).Status);
	}

	[Fact]
	public async Task Dismiss_UnknownOrAlreadyClosedItem_ReportsFalse()
	{
		var item = Post("once");
		await Call("attention.dismiss", $"\"itemId\":\"{item.Id}\"");

		var again   = (await Call("attention.dismiss", $"\"itemId\":\"{item.Id}\""))!;
		var unknown = (await Call("attention.dismiss", "\"itemId\":\"A99\""))!;

		Assert.False((bool?)again["dismissed"]);
		Assert.False((bool?)unknown["dismissed"]);
	}

	[Fact]
	public async Task Dismiss_RequiresAnItemId()
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => Call("attention.dismiss"));
	}
}
