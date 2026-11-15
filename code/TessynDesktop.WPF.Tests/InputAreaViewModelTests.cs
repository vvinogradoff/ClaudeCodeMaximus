using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.12.29 and FR.23.12 on the session pane view model (TC.IC.01 to 10): the Session/Focus selection and the collapsible input area, with its default and reset rules.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputAreaViewModelTests : IDisposable
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private readonly FakeTessynClient _client = new();
	private readonly MemoryState _state = new();
	private readonly SessionPaneViewModel _pane;

	public InputAreaViewModelTests()
	{
		foreach (var id in new[] { "a", "b", "c" })
		{
			var session = FakeSession.WithEntries(5, "Session " + id);
			session.Entries = [.. session.Entries.Select(e => e with { EntryId = $"f{id}:{e.Index}" })];
			_client.Sessions[id] = session;
		}

		_pane = Create(_state);
	}

	public void Dispose() => _pane.Dispose();

	private SessionPaneViewModel Create(IClientStateStore? state) =>
		new(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			stateStore: state);

	[Fact]
	public async Task ASessionOpensOnTheSessionView_WithTheInputAreaExpanded()
	{
		await _pane.OpenAsync("a");

		Assert.True(_pane.IsSessionSelected);
		Assert.False(_pane.IsFocusSelected);
		Assert.False(_pane.IsInputCollapsed);
		Assert.True(_pane.IsInputExpanded);
	}

	[Fact]
	public async Task TheFocusView_AlwaysStartsCollapsed_AndTheSessionViewExpanded()
	{
		await _pane.OpenAsync("a");

		_pane.SelectFocusViewCommand.Execute(null);
		Assert.True(_pane.IsInputCollapsed);

		_pane.SelectSessionViewCommand.Execute(null);
		Assert.False(_pane.IsInputCollapsed);
	}

	[Fact]
	public async Task SwitchingTheViewOfTheSameSession_AppliesTheDefaultOfTheViewEntered_EvenAfterAManualChange()
	{
		await _pane.OpenAsync("a");
		_pane.SelectFocusViewCommand.Execute(null);
		_pane.ToggleInputAreaCommand.Execute(null);   // the user expanded it in the Focus view
		Assert.False(_pane.IsInputCollapsed);

		_pane.SelectSessionViewCommand.Execute(null);
		_pane.ToggleInputAreaCommand.Execute(null);   // and collapsed it in the Session view
		Assert.True(_pane.IsInputCollapsed);
		_pane.SelectFocusViewCommand.Execute(null);

		Assert.True(_pane.IsInputCollapsed);          // collapsed because the Focus default is collapsed
		_pane.SelectSessionViewCommand.Execute(null);
		Assert.False(_pane.IsInputCollapsed);
	}

	[Fact]
	public async Task OpeningAnotherSession_ResetsToTheDefaultOfTheViewItLandsIn()
	{
		await _pane.OpenAsync("a");
		_pane.ToggleInputAreaCommand.Execute(null);   // Session view, collapsed by hand
		Assert.True(_pane.IsInputCollapsed);

		await _pane.OpenAsync("b");

		Assert.True(_pane.IsSessionSelected);
		Assert.False(_pane.IsInputCollapsed);         // b lands on its Session view: expanded
	}

	[Fact]
	public async Task TheExactScenarioOfTheRequirement_FocusOfAExpandedByHand_BThenAAgain()
	{
		await _pane.OpenAsync("a");
		_pane.SelectFocusViewCommand.Execute(null);
		_pane.ToggleInputAreaCommand.Execute(null);   // Focus view of A with the input area expanded
		Assert.False(_pane.IsInputCollapsed);

		await _pane.OpenAsync("b");                   // B has never been on Focus: Session view, expanded
		Assert.True(_pane.IsSessionSelected);
		Assert.False(_pane.IsInputCollapsed);
		_pane.SelectFocusViewCommand.Execute(null);   // landing on the Focus view of B: collapsed
		Assert.True(_pane.IsInputCollapsed);

		await _pane.OpenAsync("a");                   // back to A: its Focus view is kept, and the area is back to the default of Focus
		Assert.True(_pane.IsFocusSelected);
		Assert.True(_pane.IsInputCollapsed);
	}

	[Fact]
	public async Task TheSelectedView_IsKeptPerSession_InMemoryOnly()
	{
		await _pane.OpenAsync("a");
		_pane.SelectFocusViewCommand.Execute(null);
		await _pane.OpenAsync("b");
		Assert.True(_pane.IsSessionSelected);

		await _pane.OpenAsync("a");
		Assert.True(_pane.IsFocusSelected);

		using var restarted = Create(new MemoryState());
		await restarted.OpenAsync("a");
		Assert.True(restarted.IsSessionSelected);     // a new run starts every session on Session: nothing persisted
		Assert.False(restarted.IsInputCollapsed);
	}

	[Fact]
	public async Task ReopeningTheSameSession_DoesNotResetWhatTheUserDid()
	{
		await _pane.OpenAsync("a");
		_pane.ToggleInputAreaCommand.Execute(null);

		await _pane.OpenAsync("a");

		Assert.True(_pane.IsInputCollapsed);
	}

	[Fact]
	public async Task TheCollapsedState_IsNotPersisted()
	{
		await _pane.OpenAsync("a");
		_pane.ToggleInputAreaCommand.Execute(null);

		Assert.True(_pane.IsInputCollapsed);
		Assert.True(_state.State.CommandBarVisible);   // the only input-area state in the client state is the command bar's own switch
		using var restarted = Create(_state);
		await restarted.OpenAsync("a");
		Assert.False(restarted.IsInputCollapsed);
	}

	[Fact]
	public async Task CollapsingLeavesTheDraft_TheCommandBar_AndTheRunningTurnAlone()
	{
		await _pane.OpenAsync("a");
		_pane.InputText = "unsent words";
		_pane.IsCommandBarVisible = false;

		_pane.ToggleInputAreaCommand.Execute(null);
		_pane.ToggleInputAreaCommand.Execute(null);

		Assert.Equal("unsent words", _pane.InputText);
		Assert.False(_pane.IsCommandBarVisible);
	}

	[Fact]
	public async Task Collapsed_ShowsTheCommandBarOnly_WhenItIsVisible_OtherwiseOneLineOfText()
	{
		await _pane.OpenAsync("a");
		_pane.ToggleInputAreaCommand.Execute(null);

		_pane.IsCommandBarVisible = true;
		Assert.False(_pane.ShowInputBox);
		Assert.True(_pane.IsCommandBarVisible);

		_pane.IsCommandBarVisible = false;
		Assert.True(_pane.ShowInputBox);
	}

	[Fact]
	public async Task Expanded_AlwaysShowsTheInputBox_AndTheExtras()
	{
		await _pane.OpenAsync("a");

		foreach (var bar in new[] { true, false })
		{
			_pane.IsCommandBarVisible = bar;
			Assert.True(_pane.ShowInputBox);
			Assert.True(_pane.ShowExpandedExtras);
		}
	}

	[Fact]
	public async Task TheCollapsedSideColumn_HasExactlyOneButton_SendByDefault_StopWhileATurnRuns()
	{
		await _pane.OpenAsync("a");
		_pane.ToggleInputAreaCommand.Execute(null);

		Assert.True(_pane.ShowSendButton);
		Assert.False(_pane.ShowStopButton);
		Assert.False(_pane.ShowExpandedExtras);

		_pane.IsRunning = true;
		Assert.False(_pane.ShowSendButton);
		Assert.True(_pane.ShowStopButton);

		_pane.IsRunning = false;
		Assert.True(_pane.ShowSendButton);
		Assert.False(_pane.ShowStopButton);
	}

	[Fact]
	public async Task TheExpandedSideColumn_ShowsSend_AndStopWhileATurnRuns()
	{
		await _pane.OpenAsync("a");

		Assert.True(_pane.ShowSendButton);
		Assert.False(_pane.ShowStopButton);
		_pane.IsRunning = true;
		Assert.True(_pane.ShowSendButton);
		Assert.True(_pane.ShowStopButton);
	}

	[Fact]
	public void TheCommandBarSwitch_IsRememberedAcrossRuns()
	{
		Assert.True(_pane.IsCommandBarVisible);   // a client without a saved choice shows the bar, as before

		_pane.ToggleCommandBarCommand.Execute(null);

		Assert.False(_state.State.CommandBarVisible);
		using var restarted = Create(_state);
		Assert.False(restarted.IsCommandBarVisible);
	}

	[Fact]
	public async Task TheFocusBadge_ShowsOnlyOnTheSessionView_AndOnlyWithOpenItems()
	{
		_client.Attention["a"] =
		[
			new TessynDesktop.Models.Remote.AttentionItemDto
			{
				Id = "A1", Number = 1, Kind = TessynDesktop.Models.AttentionKind.Ask, Status = TessynDesktop.Models.AttentionStatus.Open, Title = "t", CreatedUtc = DateTimeOffset.UnixEpoch,
			},
		];
		await _pane.OpenAsync("a");
		await _pane.Focus.WhenIdleAsync();
		Assert.True(_pane.IsFocusBadgeVisible);

		_pane.SelectFocusViewCommand.Execute(null);
		Assert.False(_pane.IsFocusBadgeVisible);

		await _pane.OpenAsync("b");
		Assert.False(_pane.IsFocusBadgeVisible);   // nothing open there
	}

	[Fact]
	public async Task AJumpToAnEntry_LeavesTheFocusViewForTheTranscript()
	{
		await _pane.OpenAsync("a");
		_pane.SelectFocusViewCommand.Execute(null);
		Assert.True(_pane.IsInputCollapsed);

		await _pane.JumpToEntryAsync(new TessynDesktop.Models.Remote.AttentionRefDto { Kind = "entry", NodeId = "b", EntryId = "fb:2" });

		Assert.Equal("b", _pane.NodeId);
		Assert.True(_pane.IsSessionSelected);
		Assert.False(_pane.IsInputCollapsed);
	}

	[Fact]
	public void TheExpandedHeight_IsRememberedForTheRunOfTheApp()
	{
		Assert.Null(_pane.ExpandedInputHeight);

		_pane.ExpandedInputHeight = 180;

		Assert.Equal(180, _pane.ExpandedInputHeight);
	}
}
