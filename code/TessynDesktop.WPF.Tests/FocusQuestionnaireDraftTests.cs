using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.23.37 in the WPF Focus view: the note and the question values are one draft per item, saved on change, restored when the card shows, dropped when it no longer fits, deleted on send.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusQuestionnaireDraftTests
{
	private const string Node = "n";

	private readonly FakeTessynClient _client = new();

	private FocusViewModel Create()
	{
		var focus = new FocusViewModel(_client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
		focus.ReplySync.Delay = TimeSpan.Zero;
		return focus;
	}

	private void Seed(params AttentionQuestionDto[] questions) => _client.Attention[Node] =
	[
		new AttentionItemDto
		{
			Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Plan questions", CreatedUtc = DateTimeOffset.UnixEpoch,
			Questions = questions.Length == 0 ? FocusQuestionnaireTests.Questions() : questions,
		},
	];

	private static async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "Not reached in time: " + what);
	}

	[Fact]
	public async Task ChangingAnAnswer_SavesTheValuesAsTheItemsDraft()
	{
		Seed();
		using var focus = Create();
		await focus.SetSessionAsync(Node);

		focus.Items.Single().Questions[0].Choices[1].IsSelected = true;   // No

		await WaitForAsync(() => _client.ReplyDraftQuestions.ContainsKey((Node, "A1")), "saved");
		var saved = _client.ReplyDraftQuestions[(Node, "A1")];
		Assert.Equal(5, saved.Count);
		Assert.Equal("no", saved.Single(q => q.Number == 1).Value);
	}

	[Fact]
	public async Task AnUntouchedQuestionnaire_WritesNoDraft()
	{
		Seed();
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		await Task.Delay(100);

		Assert.Empty(_client.ReplyDraftQuestions);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("draft.set:", StringComparison.Ordinal));
	}

	[Fact]
	public async Task TheValuesComeBack_WhenTheCardShowsAgainInTheNextRun()
	{
		Seed();
		using (var first = Create())
		{
			await first.SetSessionAsync(Node);
			var card = first.Items.Single();
			card.Questions[1].Choices[0].IsSelected = true;   // Redis
			card.Questions[2].Choices[1].IsSelected = true;   // WPF, Web, Phone
			card.Questions[3].AnswerText = "mine";
			card.ReplyText = "a note";
			await WaitForAsync(() => _client.ReplyDraftTexts.GetValueOrDefault((Node, "A1")) == "a note"
				&& _client.ReplyDraftQuestions.GetValueOrDefault((Node, "A1"))?.Single(q => q.Number == 4).Value == "mine", "saved");
		}

		using var second = Create();
		await second.SetSessionAsync(Node);

		var restored = second.Items.Single();
		await WaitForAsync(() => restored.ReplyText == "a note" && restored.Questions[3].AnswerText == "mine", "restored");
		Assert.Equal("Redis", restored.Questions[1].Choices.Single(c => c.IsSelected).Value);
		Assert.Equal(["WPF", "Web", "Phone"], restored.Questions[2].Choices.Where(c => c.IsSelected).Select(c => c.Value));
		Assert.True(restored.Questions[1].ShowReset);
	}

	[Fact]
	public async Task AValuesOnlyDraft_WithAnEmptyNote_IsWritten()
	{
		Seed();
		using var focus = Create();
		await focus.SetSessionAsync(Node);

		focus.Items.Single().Questions[3].AnswerText = "x";

		await WaitForAsync(() => _client.ReplyDraftQuestions.ContainsKey((Node, "A1")), "saved without a note");
		Assert.False(_client.ReplyDraftTexts.ContainsKey((Node, "A1")));
	}

	[Fact]
	public async Task ADraftThatNoLongerFitsTheQuestions_IsDropped_AndTheCardStartsFromTheRecommendations()
	{
		_client.ReplyDraftQuestions[(Node, "A1")] = [new AttentionQuestionAnswerParams(1, "Old B")];
		Seed(new AttentionQuestionDto { Number = 1, Type = AttentionQuestionType.Single, Text = "Which?", Choices = ["New A", "New B"], Recommended = "New A" });
		using var focus = Create();

		await focus.SetSessionAsync(Node);
		await Task.Delay(100);

		var card = focus.Items.Single();
		Assert.Equal("New A", card.Questions[0].Choices.Single(c => c.IsSelected).Value);
		Assert.False(card.HasChangedQuestions);
	}

	[Fact]
	public async Task ASentQuestionnaire_LeavesNoDraftToWrite()
	{
		Seed();
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		var card = focus.Items.Single();
		card.Questions[0].Choices[1].IsSelected = true;
		await WaitForAsync(() => _client.ReplyDraftQuestions.ContainsKey((Node, "A1")), "saved");

		await card.ReplyCommand.ExecuteAsync(null);
		_client.ReplyDraftQuestions.Clear();   // the daemon deletes the draft when the item closes
		await Task.Delay(100);

		Assert.Empty(_client.ReplyDraftQuestions);
		Assert.Equal("no", _client.ReplyAnswers.Single().Questions!.Single(q => q.Number == 1).Value);
	}
}
