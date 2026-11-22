using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// FR.23.33 (model, ledger, dedupe), FR.23.36 (shared answer formatter, attention.reply), FR.23.37 (drafts), FR.23.38 (Pinned list, session file, DTO)
/// and FR.23.39 (constants) of the Focus questionnaires, with the exact strings of the spec.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionQuestionnaireTests : IDisposable
{
	private readonly RemoteSessionEnv _env = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly RecordingSendService _send = new();
	private readonly AttentionRpcModule _module;
	private readonly string _draftDir = Path.Combine(Path.GetTempPath(), "qn-drafts-" + Guid.NewGuid().ToString("N"));
	private readonly DraftService _drafts;
	private readonly DraftRpcModule _draftModule;

	public AttentionQuestionnaireTests()
	{
		_env.Models.Models = [new ClaudeModelInfo("claude-opus-5-5", "opus", "Opus 5.5")];
		_module      = new AttentionRpcModule(_env.Registry, _attention.Service, new AttentionReplyService(_attention.Service, _send), _env.Options, new SessionModelSettings(_env.Settings, _env.Models));
		_drafts      = new DraftService(_draftDir);
		_draftModule = new DraftRpcModule(_env.Registry, _drafts, _attention.Service);
	}

	public void Dispose()
	{
		_env.Dispose();
		_attention.Dispose();
		if (Directory.Exists(_draftDir))
			Directory.Delete(_draftDir, recursive: true);
	}

	private SessionNodeModel Node => _env.Session;

	// ── Fixtures ────────────────────────────────────────────────────────────────

	private static AttentionQuestionModel YesNo(int n, string text, string? recommended = null) =>
		new() { Number = n, Type = AttentionQuestionType.YesNo, Text = text, Recommended = recommended };

	private static AttentionQuestionModel Single(int n, string text, string[] choices, string? recommended = null) =>
		new() { Number = n, Type = AttentionQuestionType.Single, Text = text, Choices = [.. choices], Recommended = recommended };

	private static AttentionQuestionModel Multi(int n, string text, string[] choices, params string[] recommended) =>
		new() { Number = n, Type = AttentionQuestionType.Multi, Text = text, Choices = [.. choices], RecommendedChoices = [.. recommended] };

	private static AttentionQuestionModel Text(int n, string text, string? recommended = null) =>
		new() { Number = n, Type = AttentionQuestionType.Text, Text = text, Recommended = recommended };

	/// <summary>The six questions of the FR.23.36 example.</summary>
	private static List<AttentionQuestionModel> SpecQuestions() =>
	[
		YesNo(1, "Ship it?", "yes"),
		Single(2, "Which db?", ["Alpha", "Beta", "Gamma"], "Alpha"),
		Multi(3, "Which regions?", ["EU", "US", "Asia"], "EU", "Asia"),
		Multi(4, "Notify?", ["Email", "SMS"], "SMS"),
		Text(5, "Release name?", "Phoenix"),
		Text(6, "Anything else?"),
	];

	private AttentionItemModel PostQuestionnaire(string title = "Plan questions", AttentionKind kind = AttentionKind.Ask, string body = "", IReadOnlyList<AttentionQuestionModel>? questions = null, string? key = null) =>
		_attention.Service.Post(Node, [new AttentionPostRequestModel { Kind = kind, Title = title, Body = body, Key = key, Questions = questions ?? SpecQuestions() }]).Single().Item;

	private AttentionItemModel Item(string id) => _attention.Service.GetItems(Node.NodeId).Single(i => i.Id == id);

	private Task<JsonNode?> Call(string method, string extra = "") =>
		RemoteTestHelpers.CallAsync(_module, method, $"{{\"nodeId\":\"{Node.NodeId}\"{(extra.Length == 0 ? string.Empty : "," + extra)}}}");

	// The draft methods are loopback only, which RemoteTestHelpers.CallAsync refuses, so the raw result is serialised here.
	private async Task<JsonNode?> CallDraft(string method, string extra = "")
	{
		var result = await RemoteTestHelpers.CallRawAsync(_draftModule, method, $"{{\"nodeId\":\"{Node.NodeId}\"{(extra.Length == 0 ? string.Empty : "," + extra)}}}");
		return result == null ? null : JsonSerializer.SerializeToNode(result, result.GetType(), TdcpJsonOptions.Default);
	}

	private static IReadOnlyList<AttentionQuestionAnswerParams> SpecAnswers() =>
	[
		new(1, "yes"),
		new(2, "beta"),                       // case-insensitive
		new(3, null, ["EU", "asia"]),
		new(4, null, []),
		new(5, "Odin"),
		// 6 is not mentioned: no answer
	];

	// ── FR.23.39 constants ──────────────────────────────────────────────────────

	[Fact]
	public void TheLimitsAndTypeNames_AreConstants_AsTheSpecSays()
	{
		Assert.Equal(20, Constants.Attention.MaxQuestionsPerItem);
		Assert.Equal(2, Constants.Attention.MinQuestionChoices);
		Assert.Equal(4, Constants.Attention.MaxQuestionChoices);
		Assert.Equal(500, Constants.Attention.MaxQuestionTextLength);
		Assert.Equal(100, Constants.Attention.MaxQuestionChoiceLength);
		Assert.Equal(1_000, Constants.Attention.MaxQuestionRecommendationLength);
		Assert.Equal(["yes_no", "single", "multi", "text"],
			[Constants.Attention.QuestionTypeYesNo, Constants.Attention.QuestionTypeSingle, Constants.Attention.QuestionTypeMulti, Constants.Attention.QuestionTypeText]);
	}

	// ── FR.23.33 ledger ─────────────────────────────────────────────────────────

	[Fact]
	public void TheQuestionnaire_IsStoredOnTheItem_AndSurvivesARestart()
	{
		var posted = PostQuestionnaire();

		Assert.Equal(6, posted.Questions.Count);

		var restarted = _attention.NewServiceOverSameRoot().GetItems(Node.NodeId).Single();
		Assert.Equal(6, restarted.Questions.Count);
		var multi = restarted.Questions[2];
		Assert.Equal(AttentionQuestionType.Multi, multi.Type);
		Assert.Equal(3, multi.Number);
		Assert.Equal(["EU", "US", "Asia"], multi.Choices);
		Assert.Equal(["EU", "Asia"], multi.RecommendedChoices);
		Assert.Equal("Alpha", restarted.Questions[1].Recommended);
		Assert.Null(restarted.Questions[5].Recommended);
	}

	[Fact]
	public void ALedgerFileWithoutQuestions_ReadsAsNone()
	{
		_attention.Service.Post(Node, [AttentionTestEnv.Req("plain")]);

		var item = _attention.NewServiceOverSameRoot().GetItems(Node.NodeId).Single();

		Assert.Empty(item.Questions);
	}

	[Fact]
	public void RePostingAnOpenItem_ReplacesTheWholeQuestionnaire()
	{
		var first = PostQuestionnaire(key: "plan");

		var second = _attention.Service.Post(Node,
		[
			new AttentionPostRequestModel { Kind = AttentionKind.Ask, Title = "Other title", Key = "plan", Questions = [YesNo(1, "Only one now?", "no")] },
		]).Single();

		Assert.False(second.IsCreated);
		Assert.Equal(first.Id, second.Item.Id);
		var stored = Item(first.Id);
		var only = Assert.Single(stored.Questions);
		Assert.Equal("Only one now?", only.Text);
		Assert.Equal("no", only.Recommended);
	}

	[Fact]
	public void TheServiceHandsOutCopies_OfTheQuestionnaire()
	{
		var posted = PostQuestionnaire();

		posted.Questions[0].Text = "changed by the caller";
		posted.Questions.Clear();

		Assert.Equal(6, Item(posted.Id).Questions.Count);
		Assert.Equal("Ship it?", Item(posted.Id).Questions[0].Text);
	}

	// ── FR.23.38 session file ───────────────────────────────────────────────────

	[Fact]
	public void TheSessionFileEntry_ListsTheQuestionsAfterTheBody_OnePerLine()
	{
		PostQuestionnaire(title: "Plan questions", body: "Context of the plan",
			questions:
			[
				YesNo(1, "Ship it?", "yes"),
				Single(2, "Which db?", ["Alpha", "Beta"], "Alpha"),
				Multi(3, "Which regions?", ["EU", "US", "Asia"], "EU", "Asia"),
				Text(4, "Release name?\nsecond line"),
			]);

		var entry = Assert.Single(_attention.Files.Appended).content;

		Assert.Equal(
			"[Attention A1 (ask)] Plan questions\n" +
			"Context of the plan\n" +
			"Q1 [yes/no] Ship it? (recommended: yes)\n" +
			"Q2 [single: A Alpha | B Beta] Which db? (recommended: A)\n" +
			"Q3 [multi: A EU | B US | C Asia] Which regions? (recommended: A, C)\n" +
			"Q4 [text] Release name? / second line",
			entry);
	}

	[Fact]
	public void ATextQuestionWithARecommendation_ShowsItInTheSessionFile()
	{
		PostQuestionnaire(questions: [Text(1, "Name?", "Phoenix")]);

		Assert.EndsWith("\nQ1 [text] Name? (recommended: Phoenix)", Assert.Single(_attention.Files.Appended).content);
	}

	// ── FR.23.36 formatter ──────────────────────────────────────────────────────

	[Fact]
	public void TheFormatter_ProducesTheSpecLines_ForTheSixCases()
	{
		var item = PostQuestionnaire();

		var answer = AttentionAnswerFormatter.Compose(item, "", SpecAnswers());

		Assert.Equal(
			"Q1 Ship it?: Yes (as recommended)\n" +
			"Q2 Which db?: B Beta (changed; recommended: A Alpha)\n" +
			"Q3 Which regions?: A EU, C Asia (as recommended)\n" +
			"Q4 Notify?: none selected (changed; recommended: B SMS)\n" +
			"Q5 Release name?: Odin (changed)\n" +
			"Q6 Anything else?: no answer",
			answer.Answer);
		Assert.Equal(item.Id, answer.ItemId);
		Assert.Equal("Plan questions", answer.Title);
	}

	[Fact]
	public void TheFormatter_MarksNoAnswerAgainstARecommendation_AsChanged()
	{
		var item = PostQuestionnaire();

		var answer = AttentionAnswerFormatter.Compose(item, "", []);

		Assert.Equal(
			"Q1 Ship it?: no answer (changed; recommended: Yes)\n" +
			"Q2 Which db?: no answer (changed; recommended: A Alpha)\n" +
			"Q3 Which regions?: none selected (changed; recommended: A EU, C Asia)\n" +
			"Q4 Notify?: none selected (changed; recommended: B SMS)\n" +
			"Q5 Release name?: no answer (changed)\n" +
			"Q6 Anything else?: no answer",
			answer.Answer);
	}

	[Fact]
	public void TheFormatter_GivesNoMark_WhenThereWasNoRecommendation()
	{
		var item = PostQuestionnaire(questions:
		[
			YesNo(1, "A?"), Single(2, "B?", ["x", "y"]), Multi(3, "C?", ["x", "y"]), Text(4, "D?"),
		]);

		var answer = AttentionAnswerFormatter.Compose(item, "", [new(1, "no"), new(2, "y"), new(3, null, ["x"]), new(4, "free")]);

		Assert.Equal("Q1 A?: No\nQ2 B?: B y\nQ3 C?: A x\nQ4 D?: free", answer.Answer);
	}

	[Fact]
	public void TheFormatter_JoinsMultiLineQuestionAndAnswerTextOntoOneLine()
	{
		var item = PostQuestionnaire(questions: [Text(1, "First line\nsecond line", "one\ntwo")]);

		var answer = AttentionAnswerFormatter.Compose(item, "", [new(1, "one\r\ntwo")]);

		Assert.Equal("Q1 First line / second line: one / two (as recommended)", answer.Answer);
	}

	[Fact]
	public void TheFormatter_PutsTheNoteFirst()
	{
		var item = PostQuestionnaire(questions: [YesNo(1, "Ship it?", "yes")]);

		var answer = AttentionAnswerFormatter.Compose(item, "  please hurry  ", [new(1, "no")]);

		Assert.Equal("please hurry\nQ1 Ship it?: No (changed; recommended: Yes)", answer.Answer);
		Assert.False(answer.StartsWithQuestions);
	}

	[Fact]
	public void ThePrompt_StartsTheQuestionsOnANewLine_WhenThereIsNoNote_AndIndentsThem()
	{
		var item = PostQuestionnaire(title: "Plan questions", questions: [YesNo(1, "Ship it?", "yes"), Text(2, "Why?")]);

		var prompt = AttentionPromptBuilder.BuildAnswersPrompt([AttentionAnswerFormatter.Compose(item, "", [new(1, "yes")])]);

		Assert.Equal(
			"Answers from the Focus view:\n" +
			"- A1 (Plan questions): \n" +
			"  Q1 Ship it?: Yes (as recommended)\n" +
			"  Q2 Why?: no answer",
			prompt);
	}

	[Fact]
	public void ThePrompt_PutsTheNoteAfterTheColon()
	{
		var item = PostQuestionnaire(title: "Plan questions", questions: [YesNo(1, "Ship it?", "yes")]);

		var prompt = AttentionPromptBuilder.BuildAnswersPrompt([AttentionAnswerFormatter.Compose(item, "my note", [new(1, "yes")])]);

		Assert.Equal("Answers from the Focus view:\n- A1 (Plan questions): my note\n  Q1 Ship it?: Yes (as recommended)", prompt);
	}

	// ── FR.23.36 attention.reply ────────────────────────────────────────────────

	private const string SpecAnswersJson =
		"{\"number\":1,\"value\":\"yes\"},{\"number\":2,\"value\":\"Beta\"},{\"number\":3,\"choices\":[\"EU\",\"Asia\"]}," +
		"{\"number\":4,\"choices\":[]},{\"number\":5,\"value\":\"Odin\"}";

	[Fact]
	public async Task Reply_FormatsTheQuestionnaire_SendsThePrompt_AndStoresTheAnswerWithoutThePrefix()
	{
		var item = PostQuestionnaire(title: "Plan questions");

		var json = (await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"\",\"questions\":[{SpecAnswersJson}]}}]"))!;

		Assert.Equal([item.Id], json["answeredIds"]!.AsArray().Select(i => (string)i!));
		Assert.True((bool?)json["turnStarted"]);
		var expectedLines =
			"Q1 Ship it?: Yes (as recommended)\n" +
			"Q2 Which db?: B Beta (changed; recommended: A Alpha)\n" +
			"Q3 Which regions?: A EU, C Asia (as recommended)\n" +
			"Q4 Notify?: none selected (changed; recommended: B SMS)\n" +
			"Q5 Release name?: Odin (changed)\n" +
			"Q6 Anything else?: no answer";
		Assert.Equal("Answers from the Focus view:\n- A1 (Plan questions): \n  " + expectedLines.Replace("\n", "\n  "), _send.Sends.Single().Text);

		var stored = Item(item.Id);
		Assert.Equal(AttentionStatus.Answered, stored.Status);
		Assert.Equal(expectedLines, stored.Answer);
	}

	[Fact]
	public async Task Reply_WithANote_PutsItBeforeTheQuestions()
	{
		var item = PostQuestionnaire(title: "Plan questions", questions: [YesNo(1, "Ship it?", "yes")]);

		await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\" careful \",\"questions\":[{{\"number\":1,\"value\":\"no\"}}]}}]");

		Assert.Equal("Answers from the Focus view:\n- A1 (Plan questions): careful\n  Q1 Ship it?: No (changed; recommended: Yes)", _send.Sends.Single().Text);
		Assert.Equal("careful\nQ1 Ship it?: No (changed; recommended: Yes)", Item(item.Id).Answer);
	}

	[Fact]
	public async Task Reply_ATextOnlyAnswerToAQuestionnaireItem_SendsEveryQuestionAsNoAnswer()
	{
		var item = PostQuestionnaire(title: "Plan questions", questions: [YesNo(1, "Ship it?"), Multi(2, "Where?", ["a", "b"])]);

		var json = (await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"just text\"}}]"))!;

		Assert.True((bool?)json["turnStarted"]);
		Assert.Equal("Answers from the Focus view:\n- A1 (Plan questions): just text\n  Q1 Ship it?: no answer\n  Q2 Where?: none selected", _send.Sends.Single().Text);
	}

	[Fact]
	public async Task Reply_AQuestionnaireItemWithAnEmptyNote_IsNotSkipped()
	{
		var item = PostQuestionnaire(questions: [YesNo(1, "Ship it?", "yes")]);

		var json = (await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"\",\"questions\":[{{\"number\":1,\"value\":\"yes\"}}]}}]"))!;

		Assert.Equal([item.Id], json["answeredIds"]!.AsArray().Select(i => (string)i!));
		Assert.Empty(json["skippedIds"]!.AsArray());
	}

	[Theory]
	[InlineData("{\"number\":0,\"value\":\"yes\"}")]                       // number out of range
	[InlineData("{\"number\":7,\"value\":\"yes\"}")]                       // number out of range
	[InlineData("{\"number\":1,\"value\":\"maybe\"}")]                     // not yes/no
	[InlineData("{\"number\":1,\"choices\":[\"x\"]}")]                     // wrong shape: choices on yes/no
	[InlineData("{\"number\":2,\"value\":\"Delta\"}")]                     // unknown choice
	[InlineData("{\"number\":2,\"choices\":[\"Alpha\"]}")]                 // wrong shape: choices on single
	[InlineData("{\"number\":3,\"value\":\"EU\"}")]                        // wrong shape: value on multi
	[InlineData("{\"number\":3,\"choices\":[\"EU\",\"Mars\"]}")]           // unknown choice in multi
	[InlineData("{\"number\":5,\"choices\":[\"x\"]}")]                     // wrong shape: choices on text
	public async Task Reply_AValueThatDoesNotFitItsQuestion_IsRejectedAsInvalidParams_AndNothingIsSent(string question)
	{
		var item = PostQuestionnaire();

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() =>
			Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"\",\"questions\":[{question}]}}]"));

		Assert.Empty(_send.Sends);
		Assert.Equal(AttentionStatus.Open, Item(item.Id).Status);
	}

	[Fact]
	public async Task Reply_ARepeatedQuestionNumber_IsInvalidParams()
	{
		var item = PostQuestionnaire();

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() =>
			Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"\",\"questions\":[{{\"number\":1,\"value\":\"yes\"}},{{\"number\":1,\"value\":\"no\"}}]}}]"));
	}

	[Fact]
	public async Task Reply_QuestionsOnAnItemWithoutAQuestionnaire_AreInvalidParams()
	{
		var plain = _attention.Service.Post(Node, [AttentionTestEnv.Req("plain")]).Single().Item;

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() =>
			Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{plain.Id}\",\"text\":\"x\",\"questions\":[{{\"number\":1,\"value\":\"yes\"}}]}}]"));

		Assert.Empty(_send.Sends);
	}

	[Theory]
	[InlineData("\"questions\":\"nope\"")]
	[InlineData("\"questions\":[1]")]
	[InlineData("\"questions\":[{\"value\":\"yes\"}]")]
	[InlineData("\"questions\":[{\"number\":\"one\"}]")]
	[InlineData("\"questions\":[{\"number\":1,\"value\":5}]")]
	[InlineData("\"questions\":[{\"number\":1,\"choices\":\"EU\"}]")]
	public async Task Reply_AMalformedQuestionsParam_IsInvalidParams(string questions)
	{
		var item = PostQuestionnaire();

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() =>
			Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{item.Id}\",\"text\":\"\",{questions}}}]"));
	}

	[Fact]
	public async Task Reply_OldCallersThatSendTextOnly_AreUnchanged()
	{
		var plain = _attention.Service.Post(Node, [AttentionTestEnv.Req("plain")]).Single().Item;

		await Call("attention.reply", $"\"answers\":[{{\"itemId\":\"{plain.Id}\",\"text\":\"yes\"}}]");

		Assert.Equal("Answers from the Focus view:\n- A1 (plain): yes", _send.Sends.Single().Text);
		Assert.Equal("yes", Item(plain.Id).Answer);
	}

	[Fact]
	public async Task Reply_ABadAnswerRejectsTheWholeCall_EvenWhenAnotherAnswerIsFine()
	{
		var plain = _attention.Service.Post(Node, [AttentionTestEnv.Req("plain")]).Single().Item;
		var qn    = PostQuestionnaire(title: "Q");

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() =>
			Call("attention.reply",
				$"\"answers\":[{{\"itemId\":\"{plain.Id}\",\"text\":\"ok\"}},{{\"itemId\":\"{qn.Id}\",\"text\":\"\",\"questions\":[{{\"number\":2,\"value\":\"Delta\"}}]}}]"));

		Assert.Empty(_send.Sends);
		Assert.Equal(AttentionStatus.Open, Item(plain.Id).Status);
	}

	// ── FR.23.38 wire ───────────────────────────────────────────────────────────

	[Fact]
	public async Task AttentionList_CarriesTheQuestionsOnTheWire_WithCamelCaseStringEnums()
	{
		var item = PostQuestionnaire();

		var json = (await Call("attention.list"))!;

		var wire = json["items"]![0]!;
		Assert.Equal(item.Id, (string?)wire["id"]);
		var questions = wire["questions"]!.AsArray();
		Assert.Equal(6, questions.Count);
		Assert.Equal(["yesNo", "single", "multi", "multi", "text", "text"], questions.Select(q => (string)q!["type"]!));
		Assert.Equal([1, 2, 3, 4, 5, 6], questions.Select(q => (int)q!["number"]!));

		Assert.Equal("yes", (string?)questions[0]!["recommended"]);
		Assert.Equal(["Alpha", "Beta", "Gamma"], questions[1]!["choices"]!.AsArray().Select(c => (string)c!));
		Assert.Equal("Alpha", (string?)questions[1]!["recommended"]);
		Assert.Equal(["EU", "Asia"], questions[2]!["recommendedChoices"]!.AsArray().Select(c => (string)c!));
		Assert.Null(questions[2]!["recommended"]);
		Assert.Null(questions[5]!["recommended"]);
		Assert.Equal("Which db?", (string?)questions[1]!["text"]);
	}

	[Fact]
	public async Task AttentionList_OmitsQuestions_ForAnItemWithoutOnes()
	{
		_attention.Service.Post(Node, [AttentionTestEnv.Req("plain")]);

		var json = (await Call("attention.list"))!;

		Assert.False(json["items"]![0]!.AsObject().ContainsKey("questions"));
	}

	[Fact]
	public void TheDtoRoundTripsThroughTheWireOptions()
	{
		var dto = PostQuestionnaire().ToDto();

		var json = JsonSerializer.Serialize(dto, TdcpJsonOptions.Default);
		var back = JsonSerializer.Deserialize<AttentionItemDto>(json, TdcpJsonOptions.Default)!;

		Assert.Equal(6, back.Questions!.Count);
		Assert.Equal(AttentionQuestionType.Multi, back.Questions[2].Type);
		Assert.Equal(["EU", "Asia"], back.Questions[2].RecommendedChoices);
	}

	[Fact]
	public void TheAnswerParams_RoundTripThroughTheWireOptions()
	{
		var answers = new[] { new AttentionAnswerParams("A1", "note", [new(1, "yes"), new(3, null, ["EU"])]), new AttentionAnswerParams("A2", "plain") };

		var json = JsonSerializer.Serialize(answers, TdcpJsonOptions.Default);
		var back = JsonSerializer.Deserialize<AttentionAnswerParams[]>(json, TdcpJsonOptions.Default)!;

		Assert.Equal("{\"itemId\":\"A1\",\"text\":\"note\",\"questions\":[{\"number\":1,\"value\":\"yes\"},{\"number\":3,\"choices\":[\"EU\"]}]}", JsonSerializer.Serialize(answers[0], TdcpJsonOptions.Default));
		Assert.Equal(3, back[0].Questions![1].Number);
		Assert.Null(back[1].Questions);
	}

	// ── FR.23.38 Pinned list ────────────────────────────────────────────────────

	[Fact]
	public void ThePinnedList_EndsTheLineOfAQuestionnaireItem_WithTheQuestionCount()
	{
		var item = PostQuestionnaire(title: "Plan questions");
		var plain = new AttentionItemModel { Id = "A2", Number = 2, Kind = AttentionKind.Todo, Title = "Do it", Status = AttentionStatus.Open };
		var options = new InstructionOptionsModel(
			IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false, IsAgentToolsEnabled: true, IsNewSession: false, IsStreamingMode: false,
			IsWorkerSession: false, PinnedAttentionItems: [item, plain], UserClosedAttentionItems: null);

		var lines = InstructionBlockBuilder.Build(options).ReplaceLineEndings("\n").Split('\n');

		Assert.Contains("  - A1 ask: Plan questions (questionnaire, 6 questions)", lines);
		Assert.Contains("  - A2 todo: Do it", lines);
	}

	// ── FR.23.37 drafts ─────────────────────────────────────────────────────────

	private static string Q(params (int Number, string? Value)[] values) =>
		"[" + string.Join(",", values.Select(v => v.Value == null ? $"{{\"number\":{v.Number}}}" : $"{{\"number\":{v.Number},\"value\":\"{v.Value}\"}}")) + "]";

	[Fact]
	public void TheItemCodec_KeepsPlainTextAsPlainText()
	{
		Assert.Equal("just a note", ItemDraftCodec.Encode("just a note", null));
		Assert.Equal("just a note", ItemDraftCodec.Encode("just a note", []));
		Assert.Equal(string.Empty, ItemDraftCodec.Encode(string.Empty, null));
		var decoded = ItemDraftCodec.Decode("just a note");
		Assert.Equal("just a note", decoded.Text);
		Assert.Null(decoded.Questions);
	}

	[Fact]
	public void TheItemCodec_RoundTripsNoteAndQuestions_AndANoteThatLooksLikeTheEnvelope()
	{
		var encoded = ItemDraftCodec.Encode("note\nsecond", [new(1, "yes"), new(2, null, ["a", "b"])]);
		var decoded = ItemDraftCodec.Decode(encoded);

		Assert.Equal("note\nsecond", decoded.Text);
		Assert.Equal(2, decoded.Questions!.Count);
		Assert.Equal(["a", "b"], decoded.Questions[1].Choices);

		var tricky = Constants.Composer.ItemDraftEnvelopePrefix + "\nnot json";
		Assert.Equal(tricky, ItemDraftCodec.Decode(ItemDraftCodec.Encode(tricky, null)).Text);
	}

	[Fact]
	public void AnItemDraftWithQuestions_RoundTripsInTheStore_AndLoadItemDraftStaysTheNoteOnly()
	{
		_drafts.SaveItemDraftFull("s.txt", "A1", "my note", [new(1, "yes"), new(3, null, ["EU"])]);

		var restarted = new DraftService(_draftDir);
		var full = restarted.LoadItemDraftFull("s.txt", "A1")!;
		Assert.Equal("my note", full.Text);
		Assert.Equal([1, 3], full.Questions!.Select(q => q.Number));
		Assert.Equal("my note", restarted.LoadItemDraft("s.txt", "A1"));
		Assert.Equal(["A1"], restarted.ListItemDraftIds("s.txt"));
	}

	[Fact]
	public void ADraftWithQuestionsOnly_IsAStoredDraft_WithAnEmptyNote()
	{
		_drafts.SaveItemDraftFull("s.txt", "A1", "", [new(1, "no")]);

		var full = _drafts.LoadItemDraftFull("s.txt", "A1")!;
		Assert.Equal(string.Empty, full.Text);
		Assert.Single(full.Questions!);
		Assert.Null(_drafts.LoadItemDraft("s.txt", "A1"));
		Assert.Equal(["A1"], _drafts.ListItemDraftIds("s.txt"));
	}

	[Fact]
	public void AnItemDraftWithoutQuestions_ReadsAsBefore()
	{
		_drafts.SaveItemDraft("s.txt", "A1", "old style");

		var full = _drafts.LoadItemDraftFull("s.txt", "A1")!;

		Assert.Equal("old style", full.Text);
		Assert.Null(full.Questions);
	}

	[Fact]
	public async Task DraftSet_WithQuestions_StoresThemWithTheNote_AndDraftGetReturnsThem()
	{
		var item = PostQuestionnaire();

		var set = (await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"my note\",\"questions\":{Q((1, "no"), (2, "Beta"))}"))!;
		var get = (await CallDraft("draft.get", $"\"itemId\":\"{item.Id}\""))!;

		Assert.Equal("my note", (string?)get["text"]);
		Assert.Equal((string?)set["revision"], (string?)get["revision"]);
		var questions = get["questions"]!.AsArray();
		Assert.Equal([1, 2], questions.Select(q => (int)q!["number"]!));
		Assert.Equal(["no", "Beta"], questions.Select(q => (string)q!["value"]!));
	}

	[Fact]
	public async Task DraftGet_OmitsQuestions_WhenThereAreNone()
	{
		var item = PostQuestionnaire();
		await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"only a note\"");

		var get = (await CallDraft("draft.get", $"\"itemId\":\"{item.Id}\""))!;

		Assert.Equal("only a note", (string?)get["text"]);
		Assert.False(get.AsObject().ContainsKey("questions"));
	}

	[Fact]
	public async Task TheRevision_CoversTheQuestions_SoAQuestionChangeIsAChange()
	{
		var item = PostQuestionnaire();

		var first  = (string?)(await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"n\",\"questions\":{Q((1, "no"))}"))!["revision"];
		var second = (string?)(await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"n\",\"questions\":{Q((1, "yes"))},\"baseRevision\":\"{first}\""))!["revision"];
		var third  = (string?)(await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"n\",\"baseRevision\":\"{second}\""))!["revision"];

		Assert.NotEqual(first, second);
		Assert.NotEqual(second, third);
		Assert.Equal(new DraftRevisions().Of("n"), third);   // without question values the revision is the text's, as before
	}

	[Fact]
	public async Task DraftSet_WithAStaleBaseRevision_IsRefused_ForADraftWithQuestions()
	{
		var item = PostQuestionnaire();
		await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"n\",\"questions\":{Q((1, "no"))}");

		var ex = await Assert.ThrowsAsync<TdcpException>(() =>
			CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"n\",\"questions\":{Q((1, "yes"))},\"baseRevision\":\"{RemoteProtocol.Composer.EmptyDraftRevision}\""));

		Assert.Equal(Constants.Composer.DraftChangedMessage, ex.Message);
	}

	[Fact]
	public async Task DraftSet_AnEmptyNoteWithoutQuestions_Deletes_ButAnEmptyNoteWithQuestionsDoesNot()
	{
		var item = PostQuestionnaire();
		await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"\",\"questions\":{Q((1, "no"))}");
		Assert.Equal(["A1"], _drafts.ListItemDraftIds(Node.FileName));

		var set = (await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"\""))!;

		Assert.Equal(RemoteProtocol.Composer.EmptyDraftRevision, (string?)set["revision"]);
		Assert.Empty(_drafts.ListItemDraftIds(Node.FileName));
	}

	[Fact]
	public async Task DraftDelete_RemovesADraftWithQuestions_AndSaysItExisted()
	{
		var item = PostQuestionnaire();
		await CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"\",\"questions\":{Q((1, "no"))}");

		var result = (await CallDraft("draft.delete", $"\"itemId\":\"{item.Id}\""))!;

		Assert.True((bool?)result["deleted"]);
		Assert.Empty(_drafts.ListItemDraftIds(Node.FileName));
	}

	[Theory]
	[InlineData("\"questions\":\"x\"")]
	[InlineData("\"questions\":[{\"number\":0}]")]
	[InlineData("\"questions\":[{\"number\":1},{\"number\":1}]")]
	[InlineData("\"questions\":[{\"value\":\"yes\"}]")]
	public async Task DraftSet_AMalformedQuestionsParam_IsInvalidParams(string questions)
	{
		var item = PostQuestionnaire();

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => CallDraft("draft.set", $"\"itemId\":\"{item.Id}\",\"text\":\"n\",{questions}"));
	}

	[Fact]
	public async Task DraftSet_QuestionsWithoutAnItemId_AreInvalidParams()
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => CallDraft("draft.set", $"\"text\":\"n\",\"questions\":{Q((1, "no"))}"));
	}

	[Fact]
	public void TheLifecycle_DeletesADraftWithQuestions_WhenTheItemCloses()
	{
		var item = PostQuestionnaire();
		_drafts.SaveItemDraftFull(Node.FileName, item.Id, "", [new(1, "no")]);
		using var lifecycle = new FocusDraftLifecycle(_drafts, _attention.Service, _env.Files, _env.Registry);
		lifecycle.Start();

		_attention.Service.CloseByUser(Node, item.Id);

		Assert.Empty(_drafts.ListItemDraftIds(Node.FileName));
	}
}
