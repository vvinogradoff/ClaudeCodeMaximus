using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.ViewModels;
using static TessynDesktop.WPF.Tests.ElevatedRunFixture;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.23.42, FR.23.43: the Running group of the Focus view: the grouping, the moves between the groups, "Send answers" and the empty state.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusRunningGroupTests
{
	private readonly ElevatedRunFixture _f = new();

	private AttentionItemDto Plain(int number, AttentionKind kind = AttentionKind.Todo) => new()
	{
		Id = "A" + number, Number = number, Kind = kind, Status = AttentionStatus.Open, Title = "Plain " + number,
		CreatedUtc = T0.AddMinutes(number), UpdatedUtc = T0.AddMinutes(number),
	};

	private void SeedPlain(params AttentionItemDto[] items)
	{
		if (!_f.Client.Attention.TryGetValue(Lead, out var list))
			_f.Client.Attention[Lead] = list = [];
		list.AddRange(items);
	}

	[Theory]
	[InlineData(ElevatedRunStatus.Launching)]
	[InlineData(ElevatedRunStatus.Running)]
	public async Task AnItemInProgress_IsInTheRunningGroup_NotAmongTheOpenCards_NorCounted(ElevatedRunStatus status)
	{
		SeedPlain(Plain(2));
		_f.Seed(Run(status), Lead, number: 1);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		var running = Assert.Single(focus.RunningItems);
		Assert.Equal("A1", running.Id);
		Assert.True(running.IsInProgress);
		Assert.Equal(AttentionKind.Ask, running.Kind);            // the kind stays Ask
		Assert.Equal(["A2"], focus.OpenItems.Select(i => i.Id));
		Assert.Equal(1, focus.OpenCount);
		Assert.Equal("todo", focus.TopOpenKind);                  // the top kind skips it
		Assert.True(focus.HasRunning);
		Assert.Equal("Running (1)", focus.RunningHeader);
	}

	[Fact]
	public async Task ThePendingItem_StaysAnOpenAskCard_AndTheGroupIsHidden()
	{
		_f.Seed(Run(), Lead, number: 1);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		Assert.Empty(focus.RunningItems);
		Assert.False(focus.HasRunning);
		Assert.Equal(["A1"], focus.OpenItems.Select(i => i.Id));
		Assert.Equal(1, focus.OpenCount);
		Assert.Equal("ask", focus.TopOpenKind);
	}

	[Fact]
	public async Task TheChip_ReadsRunningInANeutralKey_NotTheAskChip()
	{
		_f.Seed(Run(ElevatedRunStatus.Running), Lead, number: 1);
		_f.Seed(Run(id: "er-2"), Lead, number: 2);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		var running = focus.RunningItems.Single();
		Assert.Equal(("running", "Running"), (running.ChipKey, running.ChipText));
		var pending = focus.OpenItems.Single();
		Assert.Equal(("ask", "Ask"), (pending.ChipKey, pending.ChipText));
	}

	[Fact]
	public async Task TheCard_HasTheStopButton_WhileRunning_AndTheStartingLine_WhileLaunching()
	{
		_f.Seed(Run(ElevatedRunStatus.Launching, id: "er-1"), Lead, number: 1);
		_f.Seed(Run(ElevatedRunStatus.Running, id: "er-2") with { StartedUtc = T0 }, Lead, number: 2);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		var launching = focus.RunningItems.Single(i => i.Id == "A1").Elevated!;
		var running   = focus.RunningItems.Single(i => i.Id == "A2").Elevated!;
		Assert.Equal("Starting...", launching.LaunchingText);
		Assert.False(launching.StopCommand.CanExecute(null));
		Assert.True(running.StopCommand.CanExecute(null));
		Assert.StartsWith("Running as administrator since", running.RunningText);
	}

	[Fact]
	public async Task TheGroup_ListsTheOldestFirst()
	{
		_f.Seed(Run(ElevatedRunStatus.Running, id: "er-new") with { CreatedUtc = T0.AddMinutes(30) }, Lead, number: 1);
		_f.Seed(Run(ElevatedRunStatus.Running, id: "er-old") with { CreatedUtc = T0 }, Lead, number: 2);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		Assert.Equal(["A2", "A1"], focus.RunningItems.Select(i => i.Id));
		Assert.Equal("Running (2)", focus.RunningHeader);
	}

	[Fact]
	public async Task Approve_MovesTheCardToTheRunningGroup_AtLaunching_WithoutTheListBeingRead()
	{
		SeedPlain(Plain(2));
		_f.Seed(Run(), Lead, number: 1);
		using var focus = _f.CreateFocus();
		await focus.SetSessionAsync(Lead);
		var card = focus.OpenItems.Single(i => i.Id == "A1");
		Assert.Equal(2, focus.OpenCount);

		await card.Elevated!.ApproveCommand.ExecuteAsync(null);

		Assert.Same(card, focus.RunningItems.Single());           // the same card, moved
		Assert.Equal(["A2"], focus.OpenItems.Select(i => i.Id));
		Assert.Equal(1, focus.OpenCount);
		Assert.True(focus.HasRunning);
		Assert.Contains(focus.Items, i => ReferenceEquals(i, card));
	}

	[Fact]
	public async Task TheRunEnding_MovesTheCardToClosed_AndTheGroupDisappears()
	{
		_f.Seed(Run(ElevatedRunStatus.Running), Lead, number: 1);
		using var focus = _f.CreateFocus();
		await focus.SetSessionAsync(Lead);

		_f.Client.SetElevatedStatus("er-1", ElevatedRunStatus.Completed, r => r with { ExitCode = 0, EndedUtc = T0.AddMinutes(2) });
		await focus.WhenIdleAsync();

		Assert.Empty(focus.RunningItems);
		Assert.False(focus.HasRunning);
		Assert.Empty(focus.Items);
		Assert.Single(focus.ClosedItems);
		Assert.Equal(0, focus.OpenCount);
	}

	[Fact]
	public async Task SendAnswers_DoesNotCountTheItemInProgress()
	{
		SeedPlain(Plain(2));
		_f.Seed(Run(ElevatedRunStatus.Running), Lead, number: 1);
		using var focus = _f.CreateFocus();
		await focus.SetSessionAsync(Lead);

		Assert.Equal(0, focus.AnswerCount);
		Assert.False(focus.SendAnswersCommand.CanExecute(null));

		focus.OpenItems.Single().ReplyText = "go ahead";

		Assert.Equal(1, focus.AnswerCount);
		Assert.Equal("Send answers (1)", focus.SendAnswersText);
	}

	[Fact]
	public async Task EmptyState_ShowsWhenOnlyItemsInProgressAndDecidedAreOpen_WithTheGroupAbove()
	{
		SeedPlain(Plain(2, AttentionKind.Decided));
		_f.Seed(Run(ElevatedRunStatus.Running), Lead, number: 1);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		Assert.True(focus.IsEmpty);
		Assert.True(focus.HasRunning);
		Assert.Equal(0, focus.OpenCount);
		Assert.False(focus.IsBadgeVisible);
		Assert.Equal(string.Empty, focus.TopOpenKind);
		Assert.Equal(["A2"], focus.DecidedItems.Select(i => i.Id));
	}

	[Fact]
	public async Task TheSupervisorsMirroredCard_IsInTheGroupToo()
	{
		_f.Seed(Run(ElevatedRunStatus.Running, requester: Worker, name: "Wrk"), Lead, number: 1);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		var card = focus.RunningItems.Single();
		Assert.True(card.Elevated!.IsWorkerRequest);
		Assert.Equal(0, focus.OpenCount);
	}
}
