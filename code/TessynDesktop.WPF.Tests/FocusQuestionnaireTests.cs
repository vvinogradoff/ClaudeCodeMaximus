using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.23.33 and FR.23.35 in the WPF Focus view: the questions of a card, their pre-filled answers, the radio and toggle rules, Reset, and the answers sent (FR.23.36).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusQuestionnaireTests
{
	private const string Node = "n";

	private readonly FakeTessynClient _client = new();

	internal static AttentionQuestionDto[] Questions() =>
	[
		new() { Number = 1, Type = AttentionQuestionType.YesNo, Text = "Use the cache?", Recommended = "yes" },
		new() { Number = 2, Type = AttentionQuestionType.Single, Text = "Which store?", Choices = ["Redis", "Memory", "Disk"], Recommended = "Memory" },
		new() { Number = 3, Type = AttentionQuestionType.Multi, Text = "Which clients?", Choices = ["WPF", "Web", "Phone"], RecommendedChoices = ["WPF", "Phone"] },
		new() { Number = 4, Type = AttentionQuestionType.Text, Text = "Name it", Recommended = "cache-v2" },
		new() { Number = 5, Type = AttentionQuestionType.Single, Text = "No recommendation here", Choices = ["One", "Two"] },
	];

	internal void Seed(params AttentionQuestionDto[] questions) => _client.Attention[Node] =
	[
		new AttentionItemDto
		{
			Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Plan questions", CreatedUtc = DateTimeOffset.UnixEpoch,
			Questions = questions.Length == 0 ? Questions() : questions,
		},
	];

	internal async Task<(FocusViewModel Focus, FocusItemViewModel Card)> ShowAsync()
	{
		var focus = new FocusViewModel(_client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
		focus.ReplySync.Delay = TimeSpan.Zero;
		await focus.SetSessionAsync(Node);
		return (focus, focus.Items.Single());
	}

	private static string Selected(FocusQuestionViewModel q) => string.Join(",", q.Choices.Where(c => c.IsSelected).Select(c => c.Value));

	[Fact]
	public async Task EveryControlStartsAtItsRecommendation_AndAQuestionWithoutOneStartsEmpty()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;

		Assert.True(card.HasQuestions);
		Assert.Equal("Note (optional)", card.NoteLabel);
		Assert.Equal(["Q1", "Q2", "Q3", "Q4", "Q5"], card.Questions.Select(q => q.NumberText));
		Assert.Equal("yes", Selected(card.Questions[0]));
		Assert.Equal("Memory", Selected(card.Questions[1]));
		Assert.Equal("WPF,Phone", Selected(card.Questions[2]));
		Assert.Equal("cache-v2", card.Questions[3].AnswerText);
		Assert.Equal(string.Empty, Selected(card.Questions[4]));
		Assert.All(card.Questions, q => Assert.False(q.ShowReset));
	}

	[Fact]
	public async Task TheRecommendedValueCarriesTheMarker_AndChoicesAreLetteredAToD()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;

		Assert.Equal(["Yes", "No"], card.Questions[0].Choices.Select(c => c.DisplayText));
		Assert.Equal(["A  Redis", "B  Memory", "C  Disk"], card.Questions[1].Choices.Select(c => c.DisplayText));
		Assert.Equal([false, true, false], card.Questions[1].Choices.Select(c => c.IsRecommended));
		Assert.Equal([true, false, true], card.Questions[2].Choices.Select(c => c.IsRecommended));
		Assert.Equal("recommended", card.Questions[1].Choices[1].RecommendedMarker);
		Assert.True(card.Questions[3].ShowTextMarker);   // its text equals the recommendation
		Assert.False(card.Questions[4].Choices.Any(c => c.IsRecommended));
	}

	[Fact]
	public async Task YesNoIsARadioGroup_ClickingTheSelectedButtonKeepsIt()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;
		var q = card.Questions[0];

		q.Choices[1].IsSelected = true;   // No
		Assert.Equal("no", Selected(q));
		Assert.True(q.ShowReset);

		q.Choices[1].IsSelected = false;  // a click on the selected one
		Assert.Equal("no", Selected(q));
	}

	[Fact]
	public async Task SingleIsARadioGroup_AndClickingTheSelectedChoiceAgainClearsIt()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;
		var q = card.Questions[1];

		q.Choices[2].IsSelected = true;
		Assert.Equal("Disk", Selected(q));
		Assert.False(q.Choices[1].IsSelected);

		q.Choices[2].IsSelected = false;   // the selected one again
		Assert.Equal(string.Empty, Selected(q));
		Assert.Null(q.ToAnswer().Value);
		Assert.True(q.ShowReset);
	}

	[Fact]
	public async Task MultiTogglesEachChoiceOnItsOwn_IncludingNone()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;
		var q = card.Questions[2];

		q.Choices[1].IsSelected = true;
		Assert.Equal("WPF,Web,Phone", Selected(q));
		q.Choices[0].IsSelected = false;
		q.Choices[1].IsSelected = false;
		q.Choices[2].IsSelected = false;

		Assert.Equal(string.Empty, Selected(q));
		Assert.NotNull(q.ToAnswer().Choices);   // none selected is an empty list, not a missing answer
		Assert.Empty(q.ToAnswer().Choices!);
	}

	[Fact]
	public async Task ResetRestoresTheRecommendation_AndTheLinkShowsOnlyWhileTheAnswerDiffers()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;

		var single = card.Questions[1];
		single.Choices[0].IsSelected = true;
		Assert.True(single.ShowReset);
		single.ResetCommand.Execute(null);
		Assert.Equal("Memory", Selected(single));
		Assert.False(single.ShowReset);

		var text = card.Questions[3];
		text.AnswerText = "other";
		Assert.True(text.ShowReset);
		Assert.False(text.ShowTextMarker);
		text.Reset();
		Assert.Equal("cache-v2", text.AnswerText);
		Assert.True(text.ShowTextMarker);

		var multi = card.Questions[2];
		multi.Choices[2].IsSelected = false;
		Assert.True(multi.ShowReset);
		multi.Reset();
		Assert.Equal("WPF,Phone", Selected(multi));

		var none = card.Questions[4];
		none.Choices[0].IsSelected = true;
		Assert.False(none.ShowReset);   // nothing to restore without a recommendation
		none.Reset();
		Assert.Equal(string.Empty, Selected(none));
	}

	[Fact]
	public async Task TheAnswersSentAreTheCurrentValues_OfEveryQuestion()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;
		card.Questions[0].Choices[1].IsSelected = true;      // No
		card.Questions[3].AnswerText = "  my name  ";

		var answers = card.BuildAnswers()!;

		Assert.Equal(5, answers.Count);
		Assert.Equal(new AttentionQuestionAnswerParams(1, "no"), answers[0]);
		Assert.Equal(new AttentionQuestionAnswerParams(2, "Memory"), answers[1]);
		Assert.Equal(["WPF", "Phone"], answers[2].Choices);
		Assert.Equal(new AttentionQuestionAnswerParams(4, "my name"), answers[3]);
		Assert.Equal(new AttentionQuestionAnswerParams(5), answers[4]);
	}

	[Fact]
	public async Task ReplyAndSendAnswers_AreEnabledWithAnEmptyNote_AndSendTheQuestions()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;
		card.Questions[1].Choices[2].IsSelected = true;   // Disk

		Assert.True(card.HasReply);
		Assert.True(card.ReplyCommand.CanExecute(null));
		Assert.Equal(1, focus.AnswerCount);
		Assert.True(focus.SendAnswersCommand.CanExecute(null));

		await focus.SendAnswersCommand.ExecuteAsync(null);

		var sent = Assert.Single(_client.ReplyAnswers);
		Assert.Equal("A1", sent.ItemId);
		Assert.Equal(string.Empty, sent.Text);
		Assert.Equal("Disk", sent.Questions!.Single(q => q.Number == 2).Value);
		Assert.Equal(5, sent.Questions!.Count);
	}

	[Fact]
	public async Task ACardWithoutQuestions_BehavesAsBefore()
	{
		_client.Attention[Node] =
		[
			new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Plain", CreatedUtc = DateTimeOffset.UnixEpoch },
		];
		var (focus, card) = await ShowAsync();
		using var _ = focus;

		Assert.False(card.HasQuestions);
		Assert.Null(card.NoteLabel);
		Assert.False(card.HasReply);
		Assert.Null(card.BuildAnswers());
		card.ReplyText = "go";
		await focus.SendAnswersCommand.ExecuteAsync(null);
		Assert.Null(_client.ReplyAnswers.Single().Questions);
	}

	[Fact]
	public async Task ARepostedQuestionnaire_ReplacesTheQuestionsAndTheirAnswers()
	{
		Seed();
		var (focus, card) = await ShowAsync();
		using var _ = focus;
		card.Questions[0].Choices[1].IsSelected = true;

		Seed(new AttentionQuestionDto { Number = 1, Type = AttentionQuestionType.YesNo, Text = "Something else?", Recommended = "no" });
		await focus.RefreshAsync();

		Assert.Single(card.Questions);
		Assert.Equal("no", Selected(card.Questions[0]));
		Assert.Equal("Something else?", card.Questions[0].Text);
	}
}
