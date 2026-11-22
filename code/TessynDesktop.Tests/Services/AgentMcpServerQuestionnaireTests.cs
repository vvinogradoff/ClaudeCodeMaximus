using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>FR.23.34 and FR.23.38: <c>post_attention</c> with a questionnaire, its validation, result line, notification and <c>list_attention</c> line.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerQuestionnaireTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AttentionTestEnv _attention = new();
	private readonly FakeNotificationService _toasts = new();
	private readonly FakeTelegramBridge _telegram = new();
	private readonly AgentMcpServer _server;
	private readonly SessionNodeModel _top = AttentionTestEnv.Node("top");

	public AgentMcpServerQuestionnaireTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"cmx-questionnaire-tests-{Guid.NewGuid():N}");
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

	private Task<JsonNode> Call(string tool, JsonObject arguments) =>
		_server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, _top);

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private static JsonObject Question(string type, string question, string[]? choices = null, string? recommended = null, string[]? recommendedChoices = null)
	{
		var q = new JsonObject { ["type"] = type, ["question"] = question };
		if (choices != null)
			q["choices"] = new JsonArray(choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
		if (recommended != null)
			q["recommended"] = recommended;
		if (recommendedChoices != null)
			q["recommended_choices"] = new JsonArray(recommendedChoices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
		return q;
	}

	private static JsonObject Item(string kind, string title, JsonObject[] questions, string? body = null, string? key = null)
	{
		var item = new JsonObject
		{
			["kind"] = kind,
			["title"] = title,
			["questions"] = new JsonArray(questions.Select(q => (JsonNode?)q).ToArray()),
		};
		if (body != null)
			item["body"] = body;
		if (key != null)
			item["key"] = key;
		return item;
	}

	private Task<JsonNode> Post(params JsonObject[] items) =>
		Call("post_attention", new JsonObject { ["items"] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray()) });

	private static async Task<Exception> ToolError(Func<Task<JsonNode>> action)
	{
		var ex = await Assert.ThrowsAnyAsync<Exception>(action);
		Assert.EndsWith("McpToolException", ex.GetType().Name);   // a file-local type, so the name is mangled
		return ex;
	}

	private AttentionItemModel[] Items() => _attention.Service.GetItems(_top.NodeId).ToArray();

	private static JsonObject[] SevenQuestions() =>
	[
		Question("yes_no", "Q one?", recommended: "YES"),
		Question("single", "Q two?", ["Alpha", "Beta"], recommended: " beta "),
		Question("multi", "Q three?", ["EU", "US", "Asia"], recommendedChoices: ["asia", "eu"]),
		Question("text", "Q four?", recommended: "Phoenix"),
		Question("yes_no", "Q five?"),
		Question("single", "Q six?", ["x", "y", "z", "w"]),
		Question("multi", "Q seven?", ["a", "b"]),
	];

	// ── Posting ────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Post_AQuestionnaire_StoresItNormalised_AndTheResultLineEndsWithTheCount()
	{
		var text = TextOf(await Post(Item("ask", "Plan questions", SevenQuestions(), body: "Context here")));

		Assert.Equal(
			"A1 posted (ask) with 7 questions.\n" +
			"Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.",
			text);

		var item = Items().Single();
		Assert.Equal("Context here", item.Body);
		Assert.Equal(7, item.Questions.Count);
		Assert.Equal([1, 2, 3, 4, 5, 6, 7], item.Questions.Select(q => q.Number));
		Assert.Equal(
			[AttentionQuestionType.YesNo, AttentionQuestionType.Single, AttentionQuestionType.Multi, AttentionQuestionType.Text, AttentionQuestionType.YesNo, AttentionQuestionType.Single, AttentionQuestionType.Multi],
			item.Questions.Select(q => q.Type));
		Assert.Equal("yes", item.Questions[0].Recommended);            // stored lower case
		Assert.Equal("Beta", item.Questions[1].Recommended);           // the choice's own spelling
		Assert.Equal(["EU", "Asia"], item.Questions[2].RecommendedChoices);   // choice spelling and order
		Assert.Null(item.Questions[2].Recommended);
		Assert.Equal("Phoenix", item.Questions[3].Recommended);
		Assert.Null(item.Questions[4].Recommended);
	}

	[Fact]
	public async Task Post_ACriticalItemMayCarryAQuestionnaire()
	{
		var text = TextOf(await Post(Item("critical", "Decide now", [Question("yes_no", "Roll back?")])));

		Assert.StartsWith("A1 posted (critical) with 1 questions.", text);
	}

	[Fact]
	public async Task Post_QuestionsAreTrimmed_AndChoicesMatchedCaseInsensitively()
	{
		await Post(Item("ask", "t", [Question("single", "  Which?  ", ["  Alpha ", "Beta"], recommended: "ALPHA")]));

		var question = Items().Single().Questions.Single();
		Assert.Equal("Which?", question.Text);
		Assert.Equal(["Alpha", "Beta"], question.Choices);
		Assert.Equal("Alpha", question.Recommended);
	}

	[Fact]
	public async Task Post_AMultiLineQuestionKeepsItsLines()
	{
		await Post(Item("ask", "t", [Question("text", "Line one\nline two")]));

		Assert.Equal("Line one\nline two", Items().Single().Questions.Single().Text);
	}

	[Fact]
	public async Task Post_RePostingAnOpenItem_ReplacesTheQuestionnaire_AndSaysAlreadyPinnedWithTheCount()
	{
		await Post(Item("ask", "t", SevenQuestions(), key: "k"));

		var text = TextOf(await Post(Item("ask", "t", [Question("text", "Only one"), Question("yes_no", "And two")], key: "k")));

		Assert.StartsWith("A1 already pinned (ask) with 2 questions - updated, not re-posted.", text);
		Assert.Equal(["Only one", "And two"], Items().Single().Questions.Select(q => q.Text));
	}

	[Fact]
	public async Task Post_AnItemWithoutQuestions_KeepsTheOldResultLine()
	{
		var text = TextOf(await Post(new JsonObject { ["kind"] = "ask", ["title"] = "plain" }));

		Assert.StartsWith("A1 posted (ask).\n", text);
	}

	[Fact]
	public async Task Post_TwentyQuestionsAreAllowed()
	{
		var twenty = Enumerable.Range(1, 20).Select(n => Question("text", $"Question {n}")).ToArray();

		var text = TextOf(await Post(Item("ask", "many", twenty)));

		Assert.StartsWith("A1 posted (ask) with 20 questions.", text);
	}

	// ── Validation ─────────────────────────────────────────────────────────────

	public static TheoryData<string, JsonObject> BadQuestions() => new()
	{
		{ "'type' is required", new JsonObject { ["question"] = "q" } },
		{ "invalid type 'radio'", Question("radio", "q") },
		{ "'question' is required", new JsonObject { ["type"] = "text" } },
		{ "'question' is required", Question("text", "   ") },
		{ "at most 500 characters", Question("text", new string('x', 501)) },
		{ "'choices' is required for single and multi", Question("single", "q") },
		{ "'choices' is required for single and multi", Question("multi", "q") },
		{ "2 to 4 labels (got 1)", Question("single", "q", ["only"]) },
		{ "2 to 4 labels (got 5)", Question("multi", "q", ["a", "b", "c", "d", "e"]) },
		{ "choices must be unique", Question("single", "q", ["Same", "same"]) },
		{ "a choice must be at most 100 characters", Question("single", "q", [new string('c', 101), "b"]) },
		{ "a choice must not be empty", Question("single", "q", ["  ", "b"]) },
		{ "'choices' is only allowed for single and multi", Question("yes_no", "q", ["a", "b"]) },
		{ "'choices' is only allowed for single and multi", Question("text", "q", ["a", "b"]) },
		{ "must be \"yes\" or \"no\"", Question("yes_no", "q", recommended: "maybe") },
		{ "'recommended' must be one of the choices", Question("single", "q", ["a", "b"], recommended: "c") },
		{ "'recommended' is not allowed for a multi question", Question("multi", "q", ["a", "b"], recommended: "a") },
		{ "recommended choice 'c' is not one of the choices", Question("multi", "q", ["a", "b"], recommendedChoices: ["a", "c"]) },
		{ "'recommended_choices' is only allowed for multi", Question("single", "q", ["a", "b"], recommendedChoices: ["a"]) },
		{ "'recommended' must be at most 1000 characters", Question("text", "q", recommended: new string('r', 1001)) },
	};

	[Theory]
	[MemberData(nameof(BadQuestions))]
	public async Task Post_AnInvalidQuestion_IsAToolErrorNamingTheItemAndTheQuestion_AndNothingIsPosted(string expected, JsonObject bad)
	{
		// the bad one is the 4th question of the 1st item, so the message must say items[0].questions[3]
		var questions = new[] { Question("text", "ok 1"), Question("text", "ok 2"), Question("text", "ok 3"), bad };

		var ex = await ToolError(() => Post(Item("ask", "t", questions)));

		Assert.Contains("items[0].questions[3]:", ex.Message);
		Assert.Contains(expected, ex.Message);
		Assert.Empty(Items());
		Assert.Empty(_attention.Files.Appended);
	}

	[Fact]
	public async Task Post_AnInvalidQuestionInASecondItem_PostsNeitherItem()
	{
		var ex = await ToolError(() => Post(
			new JsonObject { ["kind"] = "todo", ["title"] = "fine" },
			Item("ask", "bad", [Question("single", "q", ["only"])])));

		Assert.Contains("items[1].questions[0]:", ex.Message);
		Assert.Empty(Items());
	}

	[Fact]
	public async Task Post_MoreThanTwentyQuestions_IsAToolError()
	{
		var tooMany = Enumerable.Range(1, 21).Select(n => Question("text", $"Question {n}")).ToArray();

		var ex = await ToolError(() => Post(Item("ask", "t", tooMany)));

		Assert.Contains("items[0]:", ex.Message);
		Assert.Contains("1 to 20 questions (got 21)", ex.Message);
		Assert.Empty(Items());
	}

	[Fact]
	public async Task Post_AnEmptyQuestionsArray_IsAToolError()
	{
		var ex = await ToolError(() => Post(Item("ask", "t", [])));

		Assert.Contains("1 to 20 questions (got 0)", ex.Message);
	}

	[Theory]
	[InlineData("todo")]
	[InlineData("alert")]
	[InlineData("decided")]
	public async Task Post_AQuestionnaireOnAnyOtherKind_IsRejected(string kind)
	{
		var ex = await ToolError(() => Post(Item(kind, "t", [Question("yes_no", "q")])));

		Assert.Contains("items[0]:", ex.Message);
		Assert.Contains("only allowed on ask and critical items", ex.Message);
		Assert.Empty(Items());
	}

	[Fact]
	public async Task Post_AQuestionnaireTogetherWithOptionsOrRecommendation_IsRejected()
	{
		var withOptions = Item("ask", "t", [Question("yes_no", "q")]);
		withOptions["options"] = new JsonArray("a", "b");
		var withRecommendation = Item("ask", "t2", [Question("yes_no", "q")]);
		withRecommendation["recommendation"] = "a";

		var first  = await ToolError(() => Post(withOptions));
		var second = await ToolError(() => Post(withRecommendation));

		Assert.Contains("must not set 'options' or 'recommendation'", first.Message);
		Assert.Contains("must not set 'options' or 'recommendation'", second.Message);
		Assert.Empty(Items());
	}

	// ── Notification, list_attention, descriptions ─────────────────────────────

	[Fact]
	public async Task Notify_ASingleCreatedQuestionnaireItem_AddsTheCountAndTheFirstThreeTexts()
	{
		var item = Item("critical", "Plan questions", SevenQuestions(), body: "The body");

		await Post(item);

		var message = Assert.Single(_telegram.Notified).message;
		Assert.Equal("A1 (critical): Plan questions\n\nThe body\n\n7 questions: Q one?; Q two?; Q three?...", message);
	}

	[Fact]
	public async Task ListAttention_AnOpenQuestionnaireItem_EndsWithTheCount_AndAnAnsweredOneShowsItsAnswer()
	{
		await Post(Item("ask", "Plan questions", SevenQuestions()));
		await Post(new JsonObject { ["kind"] = "ask", ["title"] = "plain" });

		var open = TextOf(await Call("list_attention", new JsonObject()));

		Assert.Equal("A1 [ask, open] Plan questions - 7 questions\nA2 [ask, open] plain", open.ReplaceLineEndings("\n"));

		_attention.Service.MarkAnsweredByUser(_top.NodeId, [new AttentionAnswerModel { ItemId = "A1", Title = "Plan questions", Answer = "Q1 x: Yes" }]);

		var all = TextOf(await Call("list_attention", new JsonObject { ["status"] = "all" }));
		Assert.Contains("A1 [ask, answered] Plan questions - answer: Q1 x: Yes", all);
		Assert.DoesNotContain("7 questions", all);
	}

	[Fact]
	public void TheToolDescription_GainsTheQuestionnaireSentence_AndTheSchemaTheQuestionsField()
	{
		var list  = (JsonNode)typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
		var post  = list["tools"]!.AsArray().Single(t => (string?)t!["name"] == "post_attention")!;
		var description = (string)post["description"]!;

		Assert.Contains(
			"An ask can carry a questionnaire: up to 20 questions, each yes_no, single (2-4 choices), multi (2-4 choices) or text, with your recommended answer filled in; the user keeps or overrides each one and answers them all in one reply. Use it whenever you need several decisions at once — put the context in body and the questions in questions, not in body.",
			description);
		Assert.EndsWith("Top-level sessions only — a worker's call is refused; report to your supervisor instead.", description);

		var questions = post["inputSchema"]!["properties"]!["items"]!["items"]!["properties"]!["questions"]!;
		Assert.Equal(20, (int)questions["maxItems"]!);
		var props = questions["items"]!["properties"]!;
		Assert.Equal(["yes_no", "single", "multi", "text"], props["type"]!["enum"]!.AsArray().Select(v => (string)v!));
		Assert.NotNull(props["choices"]);
		Assert.NotNull(props["recommended"]);
		Assert.NotNull(props["recommended_choices"]);
		Assert.Equal(["type", "question"], questions["items"]!["required"]!.AsArray().Select(v => (string)v!));
	}

	[Fact]
	public void TheGuidance_GainsTheQuestionnaireSentence()
	{
		const string sentence = "When you need several decisions at once, post one ask with a questionnaire (questions, each with your recommended answer) instead of listing the questions in the body.";

		Assert.Contains(sentence, Constants.Instructions.AttentionGuidance);

		var block = InstructionBlockBuilder.Build(new InstructionOptionsModel(
			IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false, IsAgentToolsEnabled: true, IsNewSession: true, IsStreamingMode: false));
		Assert.Contains(sentence, block);
	}
}
