using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.FV.05–TC.FV.10 — the cmx attention tools (<c>post_attention</c>, <c>resolve_attention</c>,
/// <c>list_attention</c>) and the <c>notify_user</c> capture (FR.23.4–FR.23.9). The server is built the way
/// <see cref="AgentMcpServerNotifyUserTests"/> builds it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerAttentionToolsTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AttentionTestEnv _attention = new();
	private readonly FakeNotificationService _toasts = new();
	private readonly FakeTelegramBridge _telegram = new();
	private readonly AgentMcpServer _server;
	private readonly SessionNodeModel _top = AttentionTestEnv.Node("top");

	public AgentMcpServerAttentionToolsTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"cmx-attention-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_server = new AgentMcpServer(
			new AppSettingsService(_tempDir),
			new Lazy<ISessionTurnService>(() => new FakeTurnService()),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			new FakeSessionRegistry(),
			fileService: null!,
			modelService: null!,
			tdcpServer: null!,
			configMerger: null!,
			_telegram,
			_toasts,
			new FakeDirectoryLabelService(),
			_attention.Service,
			TagTestEnv.Unused());
	}

	public void Dispose()
	{
		_attention.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private Task<JsonNode> Call(string tool, JsonObject arguments, SessionNodeModel? caller = null) =>
		_server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, caller ?? _top);

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private static JsonObject Item(string kind, string title, string? body = null, string[]? options = null,
		string? recommendation = null, string? key = null)
	{
		var item = new JsonObject { ["kind"] = kind, ["title"] = title };
		if (body != null)
			item["body"] = body;
		if (options != null)
			item["options"] = new JsonArray(options.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
		if (recommendation != null)
			item["recommendation"] = recommendation;
		if (key != null)
			item["key"] = key;
		return item;
	}

	private Task<JsonNode> Post(JsonObject[] items, bool? notify = null, SessionNodeModel? caller = null)
	{
		var args = new JsonObject { ["items"] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray()) };
		if (notify != null)
			args["notify"] = notify.Value;
		return Call("post_attention", args, caller);
	}

	private async Task<string> PostText(params JsonObject[] items) => TextOf(await Post(items));

	private Task<JsonNode> Resolve(string id, string status, string? note = null, SessionNodeModel? caller = null)
	{
		var args = new JsonObject { ["id"] = id, ["status"] = status };
		if (note != null)
			args["note"] = note;
		return Call("resolve_attention", args, caller);
	}

	private async Task<string> ListText(string? status = null, SessionNodeModel? caller = null)
	{
		var args = new JsonObject();
		if (status != null)
			args["status"] = status;
		return TextOf(await Call("list_attention", args, caller));
	}

	private static async Task<Exception> ToolError(Func<Task<JsonNode>> action)
	{
		var ex = await Assert.ThrowsAnyAsync<Exception>(action);
		Assert.EndsWith("McpToolException", ex.GetType().Name);   // a file-local type, so the name is mangled
		return ex;
	}

	private static JsonNode ToolsList() =>
		(JsonNode)typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

	private AttentionItemModel[] Items() => _attention.Service.GetItems(_top.NodeId).ToArray();

	private string[] SystemEntries() =>
		_attention.Files.Appended.Where(e => e.role == Constants.SessionFile.RoleSystem).Select(e => e.content).ToArray();

	// ── TC.FV.05 validation and limits ─────────────────────────────────────────

	[Fact]
	public async Task Post_InvalidKind_IsAToolErrorNamingKindAndAllowedValues()   // TC.FV.05-1
	{
		var ex = await ToolError(() => Post([Item("urgent", "t")]));

		Assert.Contains("urgent", ex.Message);
		Assert.Contains("critical, ask, todo, alert, decided", ex.Message);
	}

	[Theory]
	[InlineData("   ")]
	[InlineData("\n\n")]
	[InlineData("")]
	public async Task Post_EmptyTitle_IsAToolError(string title)   // TC.FV.05-2
	{
		await ToolError(() => Post([Item("ask", title)]));

		Assert.Empty(Items());
	}

	[Fact]
	public async Task Post_EmptyItemsArray_IsAToolError()   // TC.FV.05-3
	{
		await ToolError(() => Post([]));
		await ToolError(() => Call("post_attention", new JsonObject()));
	}

	[Fact]
	public async Task Post_OneInvalidItem_PostsNothing()   // TC.FV.05-4
	{
		await ToolError(() => Post([Item("ask", "valid"), Item("urgent", "invalid")], notify: true));

		Assert.Empty(Items());
		Assert.Equal(1, new AttentionLedgerStore(_attention.Root).Load(_top.NodeId).NextNumber);
		Assert.Empty(_attention.Files.Appended);
		Assert.Empty(_telegram.Notified);
		Assert.Empty(_toasts.Toasts);
	}

	[Theory]
	[InlineData("CRITICAL", AttentionKind.Critical)]
	[InlineData("Ask", AttentionKind.Ask)]
	public async Task Post_KindIsCaseInsensitive(string kind, AttentionKind expected)   // TC.FV.05-5
	{
		await Post([Item(kind, "t")]);

		Assert.Equal(expected, Items().Single().Kind);
	}

	[Fact]
	public async Task Post_TitleIsFlattenedTrimmedAndCut()   // TC.FV.05-6
	{
		await Post([Item("ask", "Line one\nline two"), Item("ask", "  padded  "), Item("ask", new string('x', 250))]);

		var titles = Items().OrderBy(i => i.Number).Select(i => i.Title).ToArray();
		Assert.Equal("Line one line two", titles[0]);
		Assert.Equal("padded", titles[1]);
		Assert.Equal(200, titles[2].Length);
	}

	[Fact]
	public async Task Post_BodyDefaultsToEmptyAndIsCutTo20000()   // TC.FV.05-7
	{
		await Post([Item("ask", "no body"), Item("ask", "long body", body: new string('b', 25_000))]);

		var items = Items().OrderBy(i => i.Number).ToArray();
		Assert.Equal("", items[0].Body);
		Assert.Equal(20_000, items[1].Body.Length);
	}

	[Fact]
	public async Task Post_OptionsRecommendationAndKeyAreNormalised()   // TC.FV.05-8
	{
		await Post([Item("ask", "t",
			options: ["  a  ", "", "   ", new string('o', 250)],
			recommendation: new string('r', 1500),
			key: "  " + new string('k', 150) + "  ")]);

		var item = Items().Single();
		Assert.Equal(2, item.Options.Count);
		Assert.Equal("a", item.Options[0]);
		Assert.Equal(200, item.Options[1].Length);
		Assert.Equal(1000, item.Recommendation!.Length);
		Assert.Equal(100, item.Key!.Length);
		Assert.Equal(new string('k', 100), item.Key);
	}

	[Fact]
	public async Task Post_30Items_AreAccepted_31AreRejected()   // TC.FV.05-9
	{
		var ok = Enumerable.Range(1, 30).Select(n => Item("ask", $"item {n}")).ToArray();
		await Post(ok);
		Assert.Equal(30, Items().Length);

		var tooMany = Enumerable.Range(1, 31).Select(n => Item("ask", $"other {n}")).ToArray();
		await ToolError(() => Post(tooMany));
		Assert.Equal(30, Items().Length);
	}

	[Fact]
	public async Task Post_MoreThanTenOptions_KeepsTheFirstTen()   // TC.FV.05-10 (orchestrator brief section 6 item 1)
	{
		var options = Enumerable.Range(1, 12).Select(n => $"opt {n}").ToArray();

		await Post([Item("ask", "t", options: options)]);

		Assert.Equal(options.Take(10), Items().Single().Options);
	}

	[Fact]
	public async Task Post_TenOptions_AreAllKept()   // TC.FV.05-10
	{
		var options = Enumerable.Range(1, 10).Select(n => $"opt {n}").ToArray();

		await Post([Item("ask", "t", options: options)]);

		Assert.Equal(10, Items().Single().Options.Count);
	}

	// ── TC.FV.06 results and SYSTEM entries ────────────────────────────────────

	[Fact]
	public async Task Post_NewCriticalItem_ReturnsPostedLineThenNotifiedThenClosingLine()   // TC.FV.06-1
	{
		var text = await PostText(Item("critical", "Prod is down"));

		Assert.Equal(
			"A1 posted (critical).\n" +
			"Notified: Telegram, desktop.\n" +
			"Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.",
			text);
	}

	[Fact]
	public async Task Post_AskWithoutNotify_ReturnsPostedLineAndClosingLineOnly()   // TC.FV.06-1
	{
		var text = await PostText(Item("ask", "Which plan?"));

		Assert.Equal(
			"A1 posted (ask).\nPinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.",
			text);
	}

	[Fact]
	public async Task Post_RepostedOpenItem_ReturnsAlreadyPinnedLine()   // TC.FV.06-2
	{
		await Post([Item("ask", "one", key: "k1")]);
		await Post([Item("ask", "two", key: "k2")]);

		var text = await PostText(Item("ask", "two changed", key: "k2"));

		Assert.StartsWith("A2 already pinned (ask) - updated, not re-posted.\n", text);
	}

	[Fact]
	public async Task Post_TwoNewItems_ReturnTwoLinesInInputOrder()   // TC.FV.06-3
	{
		var text = await PostText(Item("todo", "first"), Item("alert", "second"));

		var lines = text.Split('\n');
		Assert.Equal("A1 posted (todo).", lines[0]);
		Assert.Equal("A2 posted (alert).", lines[1]);
		Assert.Equal(3, lines.Length);
	}

	[Fact]
	public async Task Post_CreatedItem_WritesFullSystemEntry()   // TC.FV.06-4
	{
		for (var n = 1; n <= 11; n++)
			await Post([Item("alert", $"filler {n}")]);
		_attention.Files.Appended.Clear();

		await Post([Item("critical", "Prod is down", body: "Details\nmore details", options: ["o1", "o2"], recommendation: "restart")]);

		var entry = Assert.Single(_attention.Files.Appended);
		Assert.Equal(Constants.SessionFile.RoleSystem, entry.role);
		Assert.Equal(_top.FileName, entry.fileName);
		Assert.Equal(
			"[Attention A12 (critical)] Prod is down\nDetails\nmore details\nOptions: o1 | o2\nRecommendation: restart",
			entry.content);
	}

	[Fact]
	public async Task Post_CreatedItemWithoutExtras_WritesOnlyTheFirstLine()   // TC.FV.06-4
	{
		await Post([Item("ask", "Bare")]);

		Assert.Equal(["[Attention A1 (ask)] Bare"], SystemEntries());
	}

	[Fact]
	public async Task Post_UpdatedItem_WritesUpdatedSystemEntry()   // TC.FV.06-5
	{
		await Post([Item("ask", "Which plan?", key: "plan")]);
		_attention.Files.Appended.Clear();

		await Post([Item("ask", "Which plan, really?", body: "B", key: "plan")]);

		Assert.Equal(["[Attention A1 (ask) updated] Which plan, really?\nB"], SystemEntries());
	}

	[Fact]
	public async Task Post_OneCreatedOneUpdated_WritesTwoEntries()   // TC.FV.06-6
	{
		await Post([Item("ask", "old", key: "k")]);
		_attention.Files.Appended.Clear();

		await Post([Item("ask", "old", key: "k"), Item("todo", "new")]);

		Assert.Equal(2, _attention.Files.Appended.Count);
		Assert.StartsWith("[Attention A1 (ask) updated]", _attention.Files.Appended[0].content);
		Assert.StartsWith("[Attention A2 (todo)]", _attention.Files.Appended[1].content);
	}

	[Fact]
	public async Task Post_AsciiInput_WritesAsciiOnlyEntry()   // TC.FV.06-7
	{
		await Post([Item("ask", "Title", body: "Body", options: ["a", "b"], recommendation: "a")]);

		Assert.All(SystemEntries(), e => Assert.All(e, c => Assert.True(c < 128, $"non-ASCII char U+{(int)c:X4}")));
	}

	[Fact]
	public void ToolsList_DescribesTheThreeAttentionTools()   // TC.FV.06-8
	{
		var tools = ToolsList()["tools"]!.AsArray().ToDictionary(t => t!["name"]!.GetValue<string>(), t => t!);

		Assert.Equal(Constants.Attention.PostToolDescription, tools["post_attention"]["description"]!.GetValue<string>());
		Assert.Equal(Constants.Attention.ResolveToolDescription, tools["resolve_attention"]["description"]!.GetValue<string>());
		Assert.Equal(Constants.Attention.ListToolDescription, tools["list_attention"]["description"]!.GetValue<string>());
		Assert.StartsWith("Pin items in the user's Focus view, next to this session's output,", Constants.Attention.PostToolDescription);
		Assert.EndsWith("a worker's call is refused; report to your supervisor instead.", Constants.Attention.PostToolDescription);
		Assert.Equal(
			"Close a pinned Focus item: status=answered when the user answered it in conversation (note = their answer), status=resolved when it is done or no longer applies (note = why).",
			Constants.Attention.ResolveToolDescription);
		Assert.Equal(
			"List this session's Focus items — open (default) or all — with ids, kinds, statuses and answers.",
			Constants.Attention.ListToolDescription);

		var schema = tools["post_attention"]["inputSchema"]!;
		Assert.Equal(["items", "notify"], schema["properties"]!.AsObject().Select(p => p.Key).OrderBy(k => k).ToArray());
		Assert.Equal(1, schema["properties"]!["items"]!["minItems"]!.GetValue<int>());
		Assert.Equal(30, schema["properties"]!["items"]!["maxItems"]!.GetValue<int>());
		Assert.Equal(
			["body", "key", "kind", "options", "recommendation", "refs", "title"],
			schema["properties"]!["items"]!["items"]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(k => k).ToArray());
		Assert.Equal("items", schema["required"]![0]!.GetValue<string>());
	}

	// ── TC.FV.07 notifications ─────────────────────────────────────────────────

	[Fact]
	public async Task Notify_CriticalAlwaysNotifies()   // TC.FV.07-1
	{
		var text = await PostText(Item("critical", "Down"));

		Assert.Single(_telegram.Notified);
		Assert.Single(_toasts.Toasts);
		Assert.Contains("Notified: Telegram, desktop.", text);
	}

	[Fact]
	public async Task Notify_AskWithoutNotifyFlag_SendsNothingAndSaysNothing()   // TC.FV.07-2
	{
		var text = await PostText(Item("ask", "Which?"));

		Assert.Empty(_telegram.Notified);
		Assert.Empty(_toasts.Toasts);
		Assert.DoesNotContain("Notified:", text);
		Assert.DoesNotContain("Notification not sent", text);
	}

	[Fact]
	public async Task Notify_FlagTrue_NotifiesForAnAlert()   // TC.FV.07-3
	{
		await Post([Item("alert", "FYI")], notify: true);

		Assert.Single(_telegram.Notified);
		Assert.Single(_toasts.Toasts);
	}

	[Fact]
	public async Task Notify_UpdatesNeverNotify()   // TC.FV.07-4
	{
		await Post([Item("critical", "Down", key: "k")]);
		_telegram.Notified.Clear();
		_toasts.Toasts.Clear();

		var text = await Post([Item("critical", "Down again", key: "k")], notify: true);

		Assert.Empty(_telegram.Notified);
		Assert.Empty(_toasts.Toasts);
		Assert.DoesNotContain("Notified:", TextOf(text));
	}

	[Fact]
	public async Task Notify_OnePerCall_EvenForSeveralItems()   // TC.FV.07-5
	{
		await Post([Item("ask", "a"), Item("critical", "b"), Item("todo", "c")]);

		Assert.Single(_telegram.Notified);
		Assert.Single(_toasts.Toasts);
	}

	[Fact]
	public async Task Notify_SingleItem_MessageIsHeadLineBlankLineBody()   // TC.FV.07-6
	{
		for (var n = 1; n <= 11; n++)
			await Post([Item("alert", $"filler {n}")]);

		await Post([Item("critical", "Prod is down", body: "Details here")]);

		Assert.Equal("A12 (critical): Prod is down\n\nDetails here", _telegram.Notified.Single().message);
		Assert.Equal(_telegram.Notified.Single().message, _toasts.Toasts.Single().message);
	}

	[Fact]
	public async Task Notify_SingleItemWithoutBody_MessageIsTheHeadLineOnly()   // TC.FV.07-6
	{
		await Post([Item("critical", "Prod is down")]);

		Assert.Equal("A1 (critical): Prod is down", _telegram.Notified.Single().message);
	}

	[Fact]
	public async Task Notify_SeveralItems_ListsOnlyTheCreatedOnes()   // TC.FV.07-7
	{
		for (var n = 1; n <= 11; n++)
			await Post([Item("alert", $"filler {n}")]);
		await Post([Item("ask", "existing", key: "keep")]);   // A12
		_telegram.Notified.Clear();

		await Post([Item("critical", "t1"), Item("ask", "existing", key: "keep"), Item("ask", "t2"), Item("todo", "t3")]);

		Assert.Equal(
			"3 new items in the Focus view:\n- A13 (critical): t1\n- A14 (ask): t2\n- A15 (todo): t3",
			_telegram.Notified.Single().message);
	}

	[Fact]
	public async Task Notify_ChannelGating_ReportsOnlyTheChannelsThatDelivered()   // TC.FV.07-8
	{
		_telegram.NotifyResult = false;
		Assert.Contains("Notified: desktop.", await PostText(Item("critical", "a")));

		_telegram.NotifyResult = true;
		_toasts.ToastResult = false;
		Assert.Contains("Notified: Telegram.", await PostText(Item("critical", "b")));

		_telegram.NotifyResult = false;
		Assert.Contains(
			"Notification not sent - neither Telegram nor desktop notifications are enabled.",
			await PostText(Item("critical", "c")));

		Assert.Equal(3, Items().Length);   // pinned in every case
	}

	[Fact]
	public async Task Notify_PostAttentionAndNotifyUser_ShareTheFanOut()   // TC.FV.07-9
	{
		var other = AttentionTestEnv.Node("other");
		await Post([Item("critical", "via post")], caller: other);
		await Call("notify_user", new JsonObject { ["message"] = "via notify" }, other);

		Assert.Equal(2, _telegram.Notified.Count);
		Assert.Equal(2, _toasts.Toasts.Count);
		Assert.All(_telegram.Notified, n => Assert.Equal("other", n.nodeId));
		Assert.All(_toasts.Toasts, t =>
		{
			Assert.Equal("other", t.nodeId);
			Assert.Equal(@"C:\Proj\other / Session-other", t.title);
		});
	}

	[Fact]
	public async Task Notify_ChannelFailureDoesNotStopTheOtherNorThePin()   // TC.FV.07-8
	{
		_telegram.ThrowOnNotify = true;

		var text = await PostText(Item("critical", "Down"));

		Assert.Contains("Notified: desktop.", text);
		Assert.Single(Items());
	}

	// ── TC.FV.08 worker refusal ────────────────────────────────────────────────

	private const string WorkerRefusal =
		"Not posted - worker sessions can't post to the user's Focus view. Report to your supervisor instead.";

	[Fact]
	public async Task Worker_PostAttention_IsRefusedWithNormalResultAndNothingHappens()   // TC.FV.08-1
	{
		var worker = AttentionTestEnv.Node("worker", depth: 1);

		var result = await Post([Item("critical", "Down")], notify: true, caller: worker);

		Assert.Equal(WorkerRefusal, TextOf(result));
		Assert.Empty(_attention.Service.GetItems("worker"));
		Assert.Empty(_attention.Files.Appended);
		Assert.Empty(_telegram.Notified);
		Assert.Empty(_toasts.Toasts);
	}

	[Fact]
	public async Task Worker_ResolveAttention_IsRefusedAndLedgerUnchanged()   // TC.FV.08-2
	{
		var worker = AttentionTestEnv.Node("worker", depth: 1);
		_attention.Service.Post(worker, [AttentionTestEnv.Req("pre-existing")]);

		var result = await Resolve("A1", "resolved", "x", caller: worker);

		Assert.Equal(WorkerRefusal, TextOf(result));
		Assert.Equal(AttentionStatus.Open, _attention.Service.GetItems("worker").Single().Status);
	}

	[Fact]
	public async Task Worker_ListAttention_ReturnsNoItemsEvenWhenTheNodeHasSome()   // TC.FV.08-3
	{
		var worker = AttentionTestEnv.Node("worker", depth: 1);
		_attention.Service.Post(worker, [AttentionTestEnv.Req("pre-existing")]);

		Assert.Equal("No items.", await ListText(caller: worker));
		Assert.Equal("No items.", await ListText("all", worker));
	}

	// ── TC.FV.09 resolve and list ──────────────────────────────────────────────

	private async Task PostMany(int count)
	{
		for (var n = 1; n <= count; n++)
			await Post([Item("ask", $"item {n}")]);
		_attention.Files.Appended.Clear();
	}

	[Fact]
	public async Task Resolve_Answered_ReturnsTextAndWritesSystemEntry()   // TC.FV.09-1
	{
		await PostMany(12);

		var result = await Resolve("a12", "answered", "Go with B");

		Assert.Equal("A12 marked answered.", TextOf(result));
		Assert.Equal(["[Attention A12 answered] Go with B"], SystemEntries());
	}

	[Fact]
	public async Task Resolve_Resolved_ReturnsTextAndWritesSystemEntry()   // TC.FV.09-2
	{
		await PostMany(12);

		var result = await Resolve("A12", "resolved", "no longer needed");

		Assert.Equal("A12 resolved.", TextOf(result));
		Assert.Equal(["[Attention A12 resolved] no longer needed"], SystemEntries());
	}

	[Fact]
	public async Task Resolve_EmptyNote_WritesEntryWithoutTrailingSpace()   // TC.FV.09-3
	{
		await PostMany(12);

		await Resolve("A12", "resolved");

		Assert.Equal(["[Attention A12 resolved]"], SystemEntries());
	}

	[Fact]
	public async Task Resolve_AlreadyClosed_SaysSoWithoutEntryOrChange()   // TC.FV.09-4
	{
		await PostMany(12);
		await Resolve("A12", "answered", "first");
		_attention.Files.Appended.Clear();

		var result = await Resolve("A12", "resolved", "second");

		Assert.Equal("A12 is already answered.", TextOf(result));
		Assert.Empty(_attention.Files.Appended);
		Assert.Equal("first", Items().Single(i => i.Id == "A12").Answer);
	}

	[Fact]
	public async Task Resolve_UnknownId_IsAToolErrorWithTheExactMessage()   // TC.FV.09-5
	{
		var ex = await ToolError(() => Resolve("a99", "resolved"));

		Assert.Equal("Unknown attention item 'A99'.", ex.Message);
	}

	[Fact]
	public async Task Resolve_BadStatus_IsAToolError()   // TC.FV.09-6
	{
		await PostMany(1);

		var ex = await ToolError(() => Resolve("A1", "done"));

		Assert.Contains("done", ex.Message);
	}

	[Fact]
	public async Task List_Open_OrdersBySeverityAndFormatsLines()   // TC.FV.09-7
	{
		await Post([Item("alert", "minor"), Item("critical", "major"), Item("ask", "question")]);

		Assert.Equal(
			"A2 [critical, open] major\nA3 [ask, open] question\nA1 [alert, open] minor",
			await ListText());
		Assert.Equal(await ListText(), await ListText("open"));
	}

	[Fact]
	public async Task List_All_ShowsClosedLinesAfterOpenOnesNewestClosedFirst()   // TC.FV.09-8
	{
		await Post([Item("ask", "q1"), Item("todo", "t2"), Item("alert", "a3"), Item("ask", "still open")]);
		await Resolve("A1", "answered", "Yes");
		_attention.Clock.Advance(TimeSpan.FromMinutes(1));
		_attention.Service.ResolveBySession(_top, "A2", AttentionStatus.Resolved, "Rotated");
		_attention.Clock.Advance(TimeSpan.FromMinutes(1));
		_attention.Service.CloseByUser(_top, "A3");

		var text = await ListText("all");

		Assert.Equal(
			"A4 [ask, open] still open\n" +
			"A3 [alert, dismissed] a3\n" +
			"A2 [todo, resolved] t2 - Rotated\n" +
			"A1 [ask, answered] q1 - answer: Yes",
			text);
	}

	[Fact]
	public async Task List_Empty_SaysSo()   // TC.FV.09-9
	{
		Assert.Equal("No open items.", await ListText());
		Assert.Equal("No open items.", await ListText("open"));
		Assert.Equal("No items.", await ListText("all"));
	}

	[Fact]
	public async Task List_BadStatus_IsAToolError()   // brief section 6 item 2
	{
		var ex = await ToolError(() => Call("list_attention", new JsonObject { ["status"] = "closed" }));

		Assert.Contains("closed", ex.Message);
	}

	// ── TC.FV.10 notify_user capture ───────────────────────────────────────────

	private Task<JsonNode> NotifyUser(string message, SessionNodeModel? caller = null) =>
		Call("notify_user", new JsonObject { ["message"] = message }, caller);

	[Fact]
	public async Task NotifyUser_PinsTheMessageAsAnAsk()   // TC.FV.10-1
	{
		var result = await NotifyUser("\n  Need a decision\nSecond line");

		var item = Items().Single();
		Assert.Equal(AttentionKind.Ask, item.Kind);
		Assert.Equal(AttentionSource.NotifyUser, item.Source);
		Assert.Equal("Need a decision", item.Title);
		Assert.Equal("\n  Need a decision\nSecond line", item.Body);
		Assert.EndsWith(" Pinned as A1.", TextOf(result));
	}

	[Fact]
	public async Task NotifyUser_LongFirstLine_IsCutTo200()   // TC.FV.10-2
	{
		await NotifyUser(new string('x', 300) + "\nrest");

		Assert.Equal(200, Items().Single().Title.Length);
	}

	[Fact]
	public async Task NotifyUser_StillSendsExactlyOneFanOutWithTheOriginalMessage()   // TC.FV.10-3
	{
		var result = await NotifyUser("pick one");

		Assert.Equal(("top", "pick one"), Assert.Single(_telegram.Notified));
		Assert.Equal("pick one", Assert.Single(_toasts.Toasts).message);
		Assert.StartsWith("Notification sent (Telegram, desktop).", TextOf(result));
	}

	[Fact]
	public async Task NotifyUser_WritesTheCreatedSystemEntryWithBody()   // TC.FV.10-4
	{
		await NotifyUser("Need a decision\nSecond line");

		Assert.Equal(["[Attention A1 (ask)] Need a decision\nNeed a decision\nSecond line"], SystemEntries());
	}

	[Fact]
	public async Task NotifyUser_SameMessageAgain_UpdatesInsteadOfDuplicating()   // TC.FV.10-5
	{
		await NotifyUser("Need a decision");
		_attention.Files.Appended.Clear();

		var result = await NotifyUser("Need a decision");

		Assert.Single(Items());
		Assert.EndsWith(" Already pinned as A1.", TextOf(result));
		Assert.StartsWith("[Attention A1 (ask) updated] Need a decision", Assert.Single(SystemEntries()));
	}

	[Fact]
	public async Task NotifyUser_Worker_IsDroppedAndPinsNothing()   // TC.FV.10-6
	{
		var worker = AttentionTestEnv.Node("worker", depth: 1);

		var result = await NotifyUser("hello", worker);

		Assert.Contains("not sent", TextOf(result));
		Assert.Empty(_attention.Service.GetItems("worker"));
		Assert.Empty(_attention.Files.Appended);
		Assert.Empty(_telegram.Notified);
	}
}
