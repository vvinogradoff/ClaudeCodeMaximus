using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Focus view (FR.23): open items newest first, answering, dismissing, live updates, and references handed to the owner.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusViewModelTests
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

	private readonly FakeTessynClient _client = new();

	private FocusViewModel Create() => new(_client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);

	private static AttentionItemDto Item(int number, AttentionKind kind = AttentionKind.Ask, AttentionStatus status = AttentionStatus.Open, string[]? options = null,
		string? recommendation = null, string? body = null, IReadOnlyList<AttentionRefDto>? refs = null) => new()
	{
		Id = "A" + number,
		Number = number,
		Kind = kind,
		Status = status,
		Title = "Title " + number,
		Body = body,
		Options = options ?? [],
		Recommendation = recommendation,
		CreatedUtc = T0.AddMinutes(number),
		UpdatedUtc = T0.AddMinutes(number),
		Refs = refs,
	};

	private void Seed(string nodeId, params AttentionItemDto[] items) => _client.Attention[nodeId] = [.. items];

	[Fact]
	public async Task ShowsOnlyOpenItems_NewestFirst_WithTheirIdsAndKinds()
	{
		Seed("n", Item(1, AttentionKind.Critical), Item(2, AttentionKind.Todo), Item(3, AttentionKind.Alert, AttentionStatus.Dismissed), Item(4, AttentionKind.Decided), Item(5, AttentionKind.Ask, AttentionStatus.Answered));
		using var focus = Create();

		await focus.SetSessionAsync("n");

		Assert.Equal(["A4", "A2", "A1"], focus.Items.Select(i => i.Id));
		Assert.Equal(["decided", "todo", "critical"], focus.Items.Select(i => i.KindKey));
		Assert.Equal(2, focus.OpenCount);          // Decided is not counted in the badge (FR.23.13)
		Assert.True(focus.HasItems);
		Assert.False(focus.IsEmpty);
	}

	[Fact]
	public async Task NoOpenItems_IsEmpty()
	{
		Seed("n", Item(1, status: AttentionStatus.Resolved));
		using var focus = Create();

		await focus.SetSessionAsync("n");

		Assert.True(focus.IsEmpty);
		Assert.False(focus.HasItems);
		Assert.Equal(0, focus.OpenCount);
	}

	[Fact]
	public async Task AskItems_ShowOptionsAndRecommendation_AndClicksFillTheReplyBox()
	{
		Seed("n", Item(1, options: ["Yes", "No"], recommendation: "Yes", body: "Pick **one**"));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var item = focus.Items.Single();

		Assert.Equal("Pick **one**", item.Body);
		Assert.Equal(["Yes", "No"], item.Options.Select(o => o.Text));
		Assert.True(item.HasOptions);
		Assert.True(item.HasRecommendation);

		item.Options[1].PickCommand.Execute(null);
		Assert.Equal("No", item.ReplyText);

		item.AcceptRecommendationCommand.Execute(null);
		Assert.Equal("Accepted your recommendation: Yes", item.ReplyText);
	}

	[Fact]
	public async Task SendAnswers_SendsEveryNonEmptyReplyBoxInOneCall_AndClearsThem()
	{
		Seed("n", Item(1), Item(2), Item(3));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		Assert.False(focus.SendAnswersCommand.CanExecute(null));

		focus.Items.Single(i => i.Id == "A1").ReplyText = "first answer";
		focus.Items.Single(i => i.Id == "A3").ReplyText = "  third\nline two  ";
		focus.Items.Single(i => i.Id == "A2").ReplyText = "   ";

		Assert.Equal(2, focus.AnswerCount);
		Assert.Equal("Send answers (2)", focus.SendAnswersText);
		Assert.True(focus.SendAnswersCommand.CanExecute(null));

		await focus.SendAnswersCommand.ExecuteAsync(null);

		Assert.Single(_client.Calls, c => c.StartsWith("attention.reply:", StringComparison.Ordinal));
		Assert.Contains("attention.reply:n:A3=third\nline two|A1=first answer", _client.Calls);   // ordered as the cards are (newest first)
		Assert.Equal(["A2"], focus.Items.Select(i => i.Id));                                      // answered items are no longer open
		Assert.Equal(0, focus.AnswerCount);
		Assert.Equal("Send answers", focus.SendAnswersText);
	}

	[Fact]
	public async Task Reply_SendsOnlyThatCard()
	{
		Seed("n", Item(1), Item(2));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var a1 = focus.Items.Single(i => i.Id == "A1");
		var a2 = focus.Items.Single(i => i.Id == "A2");
		a1.ReplyText = "one";
		a2.ReplyText = "two";

		await a1.ReplyCommand.ExecuteAsync(null);

		Assert.Contains("attention.reply:n:A1=one", _client.Calls);
		Assert.DoesNotContain(_client.Calls, c => c.Contains("A2=", StringComparison.Ordinal));
		Assert.Equal("two", a2.ReplyText);                       // other cards keep their unsent text
		Assert.Equal(["A2"], focus.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task ReplyIsDisabledForAnEmptyBox()
	{
		Seed("n", Item(1));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var item = focus.Items.Single();

		Assert.False(item.ReplyCommand.CanExecute(null));
		item.ReplyText = "x";
		Assert.True(item.ReplyCommand.CanExecute(null));
	}

	[Fact]
	public async Task SkippedAnswers_AreReportedAndKeptInTheBox()
	{
		Seed("n", Item(1));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		focus.Items.Single().ReplyText = "late answer";
		_client.Attention["n"][0] = _client.Attention["n"][0] with { Status = AttentionStatus.Resolved }; // the session closed it meanwhile

		await focus.SendAnswersCommand.ExecuteAsync(null);

		Assert.Contains("A1", focus.Error);
	}

	[Theory]
	[InlineData(AttentionKind.Ask, "Dismiss")]
	[InlineData(AttentionKind.Todo, "Dismiss")]
	[InlineData(AttentionKind.Critical, "Got it")]
	[InlineData(AttentionKind.Alert, "Got it")]
	[InlineData(AttentionKind.Decided, "Got it")]
	public async Task TheCloseButton_ReadsByKind_AndDismissClosesTheItem(AttentionKind kind, string text)
	{
		Seed("n", Item(7, kind));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var item = focus.Items.Single();
		Assert.Equal(text, item.CloseText);

		await item.DismissCommand.ExecuteAsync(null);

		Assert.Contains("attention.dismiss:n:A7", _client.Calls);
		Assert.Empty(focus.Items);
		Assert.True(focus.IsEmpty);
	}

	[Fact]
	public async Task AttentionChangedEvents_UpdateTheViewLive_ForItsOwnSessionOnly()
	{
		Seed("n", Item(1));
		Seed("other", Item(9));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var calls = _client.Calls.Count;

		_client.Attention["n"].Add(Item(2, AttentionKind.Critical));
		_client.RaiseNotification(RemoteProtocol.Events.AttentionChanged, new AttentionChangedPayload("other", new AttentionSummaryDto()));
		await focus.WhenIdleAsync();
		Assert.Equal(calls, _client.Calls.Count);                    // another session: nothing reloaded
		Assert.Single(focus.Items);

		_client.RaiseNotification(RemoteProtocol.Events.AttentionChanged, new AttentionChangedPayload("n", new AttentionSummaryDto()));
		await focus.WhenIdleAsync();

		Assert.Equal(["A2", "A1"], focus.Items.Select(i => i.Id));
		Assert.Equal(["critical", "ask"], focus.Items.Select(i => i.KindKey));
	}

	[Fact]
	public async Task TextTypedIntoAReplyBox_SurvivesRefreshes_AndSessionSwitches()
	{
		Seed("n", Item(1));
		Seed("m", Item(1));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var item = focus.Items.Single();
		item.ReplyText = "half-written answer";

		_client.Attention["n"].Add(Item(2));
		await focus.RefreshAsync();
		Assert.Same(item, focus.Items.Single(i => i.Id == "A1"));
		Assert.Equal("half-written answer", item.ReplyText);

		await focus.SetSessionAsync("m");        // e.g. a jump to another session's entry
		Assert.Empty(focus.Items.Where(i => i.ReplyText.Length > 0));
		await focus.SetSessionAsync("n");

		Assert.Equal("half-written answer", focus.Items.Single(i => i.Id == "A1").ReplyText);
	}

	[Fact]
	public async Task ItemsAreUpdatedInPlace_SoTheCardBeingTypedIntoKeepsItsInstance()
	{
		Seed("n", Item(1), Item(2));
		using var focus = Create();
		await focus.SetSessionAsync("n");
		var before = focus.Items.ToList();

		_client.Attention["n"][0] = _client.Attention["n"][0] with { Title = "Renamed" };
		await focus.RefreshAsync();

		Assert.Equal(before, focus.Items);
		Assert.Equal("Renamed", focus.Items.Single(i => i.Id == "A1").Title);
	}

	[Fact]
	public async Task References_AreListedWithLabels_AndActivatingOneRaisesTheEvent()
	{
		var entry = new AttentionRefDto { Kind = "entry", NodeId = "n", EntryId = "f:3", Offset = 4, Length = 6, Label = "the failing step" };
		var file = new AttentionRefDto { Kind = "file", RefId = "abc123", Name = "plan.md", Path = "docs/plan.md" };
		var url = new AttentionRefDto { Kind = "url", Url = "https://example.com/pr/1" };
		var odd = new AttentionRefDto { Kind = "hologram" };
		Seed("n", Item(1, refs: [entry, file, url, odd]));
		using var focus = Create();
		var raised = new List<AttentionRefDto>();
		focus.RefActivated += (_, r) => raised.Add(r);
		await focus.SetSessionAsync("n");
		var refs = focus.Items.Single().Refs;

		Assert.Equal(["the failing step", "plan.md", "example.com", "hologram"], refs.Select(r => r.Label));
		Assert.True(focus.Items.Single().HasRefs);
		Assert.True(refs[0].OpenCommand.CanExecute(null));
		Assert.False(refs[3].OpenCommand.CanExecute(null));          // an unknown kind is listed but inert

		refs[0].OpenCommand.Execute(null);
		refs[1].OpenCommand.Execute(null);
		refs[2].OpenCommand.Execute(null);

		Assert.Equal([entry, file, url], raised);
	}

	[Fact]
	public async Task ALoadFailure_IsShown_AndClearedByTheNextSuccess()
	{
		Seed("n", Item(1));
		_client.AttentionListException = new InvalidOperationException("boom");
		using var focus = Create();

		await focus.SetSessionAsync("n");
		Assert.Contains("boom", focus.Error);

		_client.AttentionListException = null;
		await focus.RefreshAsync();
		Assert.Null(focus.Error);
		Assert.Single(focus.Items);
	}

	[Fact]
	public async Task ClearEmptiesTheView_AndAResyncReloads()
	{
		Seed("n", Item(1));
		using var focus = Create();
		await focus.SetSessionAsync("n");

		_client.Attention["n"].Add(Item(2));
		_client.RaiseResync();
		await focus.WhenIdleAsync();
		Assert.Equal(2, focus.Items.Count);

		focus.Clear();
		Assert.Empty(focus.Items);
		Assert.Null(focus.NodeId);
		Assert.True(focus.IsEmpty);
	}
}
