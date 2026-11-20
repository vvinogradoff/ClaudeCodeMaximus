using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.25 / TC.DS.48 to TC.DS.56: after the user switches sessions and the Focus items of the new session loaded, the Session view comes forward when the badge count is 0 and
/// Focus is the selected tab beside it in the session area. Never for a failed load, a stale session, later item changes, answers, or another place of Focus.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusAutoSwitchTests : IDisposable
{
	private readonly FakeTessynClient _client = new();
	private readonly FakeShellLayout _layout = new() { FocusSelectedWithSession = true };
	private readonly CapturingLogger<SessionPaneViewModel> _log = new();
	private readonly SessionPaneViewModel _pane;

	public FocusAutoSwitchTests()
	{
		var ui = new ImmediateUiDispatcher();
		_pane = new SessionPaneViewModel(
			_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui, new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance),
			_log, Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(_client, ui, NullLogger<FocusViewModel>.Instance), null, new MediaPreviewPolicy())
		{
			ShellLayout = _layout,
		};
	}

	public void Dispose() => _pane.Dispose();

	private static AttentionItemDto Item(int number, AttentionKind kind, AttentionStatus status = AttentionStatus.Open) =>
		new() { Id = "A" + number, Number = number, Kind = kind, Status = status, Title = "Item " + number, CreatedUtc = DateTimeOffset.UnixEpoch };

	private void Seed(string id, params AttentionItemDto[] items)
	{
		_client.Sessions[id] = FakeSession.WithEntries(2, "Session " + id);
		_client.Attention[id] = [.. items];
	}

	private async Task Open(string id)
	{
		await _pane.OpenAsync(id);
		await _pane.Focus.WhenIdleAsync();
	}

	private int Switches => _layout.BroughtForward.Count(p => p == Constants.DockedShell.SessionPanelId);

	private int ReasonLines => _log.Messages.Count(m => m.Contains("no open Focus items, back to the Session view"));

	[Fact]
	public async Task NoOpenItems_FocusSelectedWithTheSessionView_BringsTheSessionViewForward_AndLogsOnce()
	{
		Seed("a");
		await Open("a");

		Assert.Equal(1, Switches);
		Assert.Equal(1, ReasonLines);
	}

	[Fact]
	public async Task OpenItems_FocusStays()
	{
		Seed("a", Item(1, AttentionKind.Ask));
		await Open("a");

		Assert.Empty(_layout.BroughtForward);
		Assert.Equal(0, ReasonLines);
	}

	[Fact]
	public async Task FocusNotSelectedWithTheSessionView_SideFloatingAutoHidden_NothingHappens()
	{
		_layout.FocusSelectedWithSession = false;
		Seed("a");
		await Open("a");

		Assert.Empty(_layout.BroughtForward);
		Assert.Equal(0, ReasonLines);
	}

	[Fact]
	public async Task DecidedOnlyAndClosedOnly_CountAsNone_AnOpenAskDoesNot()
	{
		Seed("decided", Item(1, AttentionKind.Decided), Item(2, AttentionKind.Decided));
		Seed("closed", Item(1, AttentionKind.Ask, AttentionStatus.Answered), Item(2, AttentionKind.Todo, AttentionStatus.Dismissed));
		Seed("ask", Item(1, AttentionKind.Ask));

		await Open("decided");
		Assert.Equal(1, Switches);
		await Open("closed");
		Assert.Equal(2, Switches);
		await Open("ask");
		Assert.Equal(2, Switches);
	}

	[Fact]
	public async Task WhileTheItemsLoad_NothingHappens_ThenTheSwitchHappensOnce()
	{
		Seed("a");
		var gate = new TaskCompletionSource();
		_client.AttentionListGates["a"] = gate;
		await _pane.OpenAsync("a");
		Assert.Empty(_layout.BroughtForward);

		gate.SetResult();
		await _pane.Focus.WhenIdleAsync();
		Assert.Equal(1, Switches);
		Assert.Equal(1, ReasonLines);
	}

	[Fact]
	public async Task AFailedLoad_NeverSwitches_AndALaterRefreshIsNotAFirstLoad()
	{
		Seed("a");
		_client.AttentionListException = new InvalidOperationException("boom");
		await Open("a");
		Assert.Empty(_layout.BroughtForward);

		_client.AttentionListException = null;
		await _pane.Focus.RefreshAsync();
		Assert.Empty(_layout.BroughtForward);
	}

	[Fact]
	public async Task SwitchingAgainBeforeTheLoadFinished_NoSwitchForTheStaleSession()
	{
		Seed("a");
		Seed("b", Item(1, AttentionKind.Ask));
		var gate = new TaskCompletionSource();
		_client.AttentionListGates["a"] = gate;
		await _pane.OpenAsync("a");
		await _pane.OpenAsync("b");
		gate.SetResult();
		await _pane.Focus.WhenIdleAsync();

		Assert.Empty(_layout.BroughtForward);
	}

	[Fact]
	public async Task AnsweringTheLastItemWhileReading_AndLaterChanges_DoNotSwitch()
	{
		Seed("a", Item(1, AttentionKind.Ask));
		await Open("a");

		_client.Attention["a"][0] = _client.Attention["a"][0] with { Status = AttentionStatus.Answered };
		await _pane.Focus.RefreshAsync();
		await _pane.Focus.WhenIdleAsync();
		Assert.Equal(0, _pane.FocusBadge);
		Assert.Empty(_layout.BroughtForward);

		_client.RaiseNotification(RemoteProtocol.Events.AttentionChanged, new AttentionChangedPayload("a", new AttentionSummaryDto()));
		await _pane.Focus.WhenIdleAsync();
		Assert.Empty(_layout.BroughtForward);
	}

	[Fact]
	public async Task ReopeningTheSameSession_IsNotASwitch()
	{
		Seed("a", Item(1, AttentionKind.Ask));
		await Open("a");
		_client.Attention["a"].Clear();
		await Open("a");

		Assert.Empty(_layout.BroughtForward);
	}

	[Fact]
	public async Task SwitchingBackToASessionWithoutItems_ChecksAgain_AndNothingReselectsFocus()
	{
		Seed("a");
		Seed("b", Item(1, AttentionKind.Ask));
		await Open("a");
		await Open("b");
		await Open("a");

		Assert.Equal(2, Switches);
		Assert.DoesNotContain(Constants.DockedShell.FocusPanelId, _layout.BroughtForward);
	}
}
