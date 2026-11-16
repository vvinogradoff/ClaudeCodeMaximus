using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>W8 (FR.14): the schedule list of the open session, the delayed send ("Send later…"), the answer parser and the tree row flag.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleViewModelTests
{
	private const string Node = "a";
	private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
	private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly List<ScheduleDto> _stored = [];

	public ScheduleViewModelTests()
	{
		_client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, [RemoteProtocol.Schedules.Capability], 1, 0);
		_client.SchedulesListHandler = nodeId => new SchedulesListResult([.. _stored.Where(s => nodeId == null || s.NodeId == nodeId)]);
		_client.SchedulesAddHandler = (nodeId, prompt, kind, at, cron, missed, note, max) =>
		{
			var added = new ScheduleDto("s" + (_stored.Count + 1), nodeId, kind, prompt, note ?? string.Empty, at, cron, missed ?? MissedFireDto.FireOnce, 0, max ?? 0, false);
			_stored.Add(added);
			return added;
		};
		_client.SchedulesRemoveHandler = id => new SchedulesRemoveResult(_stored.RemoveAll(s => s.Id == id) > 0);
	}

	private static ScheduleDto Schedule(string id, string prompt = "wake up", DateTimeOffset? at = null, bool mailbox = false, ScheduleKindDto kind = ScheduleKindDto.At, string? cron = null) =>
		new(id, Node, kind, prompt, mailbox ? string.Empty : "Delayed send", at ?? Now.AddHours(1), cron, MissedFireDto.FireOnce, 0, 0, mailbox);

	private SessionPaneViewModel CreatePane() =>
		new(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			Dialogs = new DialogService(_presenter, new ImmediateUiDispatcher()),
			ScheduleClock = new FixedClock(Now),
			ScheduleZone = Utc,
		};

	private async Task<SessionPaneViewModel> OpenedAsync()
	{
		_client.Sessions[Node] = FakeSession.WithEntries(2);
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		return pane;
	}

	private void Answer(string text) => _presenter.Answer = dialog =>
	{
		dialog.InputText = text;
		dialog.AcceptCommand.Execute(null);
		return Task.CompletedTask;
	};

	// ---- the answer parser ----

	[Theory]
	[InlineData("30", 30)]
	[InlineData("30m", 30)]
	[InlineData("30 min", 30)]
	[InlineData("2h", 120)]
	[InlineData("1h30m", 90)]
	[InlineData("1d", 1440)]
	[InlineData("0.5h", 30)]
	[InlineData("1,5h", 90)]
	public void ADelay_IsRelativeToNow(string answer, int minutes)
	{
		var time = ScheduleTimeParser.TryParse(answer, Now, Utc, out var error);

		Assert.Null(error);
		Assert.Equal(new ScheduleTime(ScheduleKindDto.Delay, Now.AddMinutes(minutes)), time);
	}

	[Fact]
	public void AClockTime_IsTodayOrTomorrowWhenItHasPassed()
	{
		Assert.Equal(new ScheduleTime(ScheduleKindDto.At, new DateTimeOffset(2026, 11, 16, 14, 30, 0, TimeSpan.Zero)), ScheduleTimeParser.TryParse("14:30", Now, Utc, out _));
		Assert.Equal(new ScheduleTime(ScheduleKindDto.At, new DateTimeOffset(2026, 11, 17, 9, 15, 0, TimeSpan.Zero)), ScheduleTimeParser.TryParse("9:15", Now, Utc, out _));
	}

	[Fact]
	public void AnAbsoluteTime_IsReadInTheGivenZone()
	{
		var zone = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");

		var time = ScheduleTimeParser.TryParse("2026-11-20 09:30", Now, zone, out _);

		Assert.Equal(new ScheduleTime(ScheduleKindDto.At, new DateTimeOffset(2026, 11, 20, 7, 30, 0, TimeSpan.Zero)), time);
	}

	[Theory]
	[InlineData("")]
	[InlineData("soon")]
	[InlineData("0")]
	[InlineData("-5m")]
	[InlineData("1h30")]
	[InlineData("5x")]
	[InlineData("2026-11-01 09:00")]
	[InlineData("99999d")]
	public void ABadAnswer_IsRefusedWithAReason(string answer)
	{
		Assert.Null(ScheduleTimeParser.TryParse(answer, Now, Utc, out var error));
		Assert.False(string.IsNullOrEmpty(error));
	}

	// ---- the delayed send ----

	[Fact]
	public async Task SendLater_SchedulesTheMessage_EmptiesTheBoxAndShowsTheIndicator()
	{
		using var pane = await OpenedAsync();
		pane.InputText = "  remind me  ";
		Answer("2h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		var added = Assert.Single(_stored);
		Assert.Equal((Node, "remind me", ScheduleKindDto.Delay, Now.AddHours(2), Constants.Schedules.DelayedSendNote), (added.NodeId, added.Prompt, added.Kind, added.FireAtUtc, added.Note));
		Assert.Equal(string.Empty, pane.InputText);
		Assert.Empty(_client.TurnSends);   // nothing is sent now
		Assert.True(pane.Schedules.HasSchedules);
		Assert.Equal(1, pane.Schedules.Count);
		Assert.Contains("2026-11-16 12:00", pane.Notice);
	}

	[Fact]
	public async Task SendLater_WithAClockTime_AddsAnAtSchedule()
	{
		using var pane = await OpenedAsync();
		pane.InputText = "morning";
		Answer("2026-11-17 08:00");

		await pane.SendLaterCommand.ExecuteAsync(null);

		var added = Assert.Single(_stored);
		Assert.Equal((ScheduleKindDto.At, new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero)), (added.Kind, added.FireAtUtc));
	}

	[Fact]
	public async Task SendLater_CancelledOrWithAnEmptyBox_SchedulesNothingAndKeepsTheText()
	{
		using var pane = await OpenedAsync();
		pane.InputText = "keep me";
		_presenter.Answer = dialog =>
		{
			dialog.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};

		await pane.SendLaterCommand.ExecuteAsync(null);
		Assert.Empty(_stored);
		Assert.Equal("keep me", pane.InputText);

		pane.InputText = "   ";
		await pane.SendLaterCommand.ExecuteAsync(null);
		Assert.Empty(_stored);
		Assert.Equal(Constants.Schedules.NothingToSend, pane.Notice);
		Assert.Single(_presenter.Shown);   // the empty box did not even ask
	}

	[Fact]
	public async Task TheDialogRefusesAnAnswerItDoesNotUnderstand_UntilItIsGood()
	{
		using var pane = await OpenedAsync();
		pane.InputText = "x";
		string? firstError = null;
		_presenter.Answer = dialog =>
		{
			dialog.InputText = "whenever";
			dialog.AcceptCommand.Execute(null);
			firstError = dialog.Error;
			dialog.InputText = "10m";
			dialog.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.False(string.IsNullOrEmpty(firstError));
		Assert.Equal(Now.AddMinutes(10), Assert.Single(_stored).FireAtUtc);
	}

	[Fact]
	public async Task SendLater_WhenTheDaemonRefuses_KeepsTheTextAndShowsTheReason()
	{
		using var pane = await OpenedAsync();
		pane.InputText = "keep me";
		_client.SchedulesAddHandler = (_, _, _, _, _, _, _, _) => throw new InvalidOperationException("'fireAtUtc' is too far");
		Answer("1h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal("keep me", pane.InputText);
		Assert.Contains("'fireAtUtc' is too far", pane.Error);
	}

	[Fact]
	public async Task SendLater_OnADaemonWithoutSchedules_SaysSoAndAsksNothing()
	{
		_client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, [], 1, 0);
		using var pane = await OpenedAsync();
		pane.InputText = "x";

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal(Constants.Schedules.NotSupported, pane.Notice);
		Assert.Empty(_presenter.Shown);
		Assert.Empty(_stored);
	}

	// ---- the list ----

	[Fact]
	public async Task OpeningASession_LoadsItsSchedules_SplitIntoOwnAndMailbox()
	{
		_stored.AddRange([Schedule("own", "later"), Schedule("mail", "worker reply", mailbox: true), new ScheduleDto("other", "zzz", ScheduleKindDto.At, "elsewhere", "", Now, null, MissedFireDto.FireOnce, 0, 0, false)]);

		using var pane = await OpenedAsync();

		Assert.Equal(["own"], pane.Schedules.Own.Select(s => s.Id));
		Assert.Equal(["mail"], pane.Schedules.Mailbox.Select(s => s.Id));
		Assert.Equal(2, pane.Schedules.Count);
		Assert.True(pane.Schedules.Own[0].CanCancel);
		Assert.False(pane.Schedules.Mailbox[0].CanCancel);   // read-only
		Assert.Contains("schedules.list:" + Node, _client.Calls);
	}

	[Fact]
	public async Task Cancel_RemovesTheScheduleAndReloads_AndAMailboxDeliveryCannotBeCancelled()
	{
		_stored.AddRange([Schedule("own"), Schedule("mail", mailbox: true)]);
		using var pane = await OpenedAsync();

		await pane.Schedules.CancelCommand.ExecuteAsync(pane.Schedules.Mailbox[0]);
		Assert.DoesNotContain("schedules.remove:mail", _client.Calls);

		await pane.Schedules.CancelCommand.ExecuteAsync(pane.Schedules.Own[0]);

		Assert.Contains("schedules.remove:own", _client.Calls);
		Assert.Empty(pane.Schedules.Own);
		Assert.Equal(1, pane.Schedules.Count);
	}

	[Fact]
	public async Task ScheduleChanged_ForThisSessionOrUnspecified_ReloadsTheList_ForAnotherSession_DoesNot()
	{
		using var pane = await OpenedAsync();
		Assert.False(pane.Schedules.HasSchedules);

		_stored.Add(Schedule("new"));
		_client.RaiseNotification(RemoteProtocol.Schedules.ChangedEvent, new ScheduleChangedPayload("another"));
		Assert.False(pane.Schedules.HasSchedules);

		_client.RaiseNotification(RemoteProtocol.Schedules.ChangedEvent, new ScheduleChangedPayload(Node));
		Assert.Equal(["new"], pane.Schedules.Own.Select(s => s.Id));

		_stored.Clear();
		_client.RaiseNotification(RemoteProtocol.Schedules.ChangedEvent, new ScheduleChangedPayload(null));
		Assert.False(pane.Schedules.HasSchedules);
	}

	[Fact]
	public async Task AFailedList_ShowsTheReasonAndKeepsNoStaleRows()
	{
		_stored.Add(Schedule("own"));
		using var pane = await OpenedAsync();
		_client.SchedulesListHandler = _ => throw new InvalidOperationException("boom");

		await pane.Schedules.RefreshAsync();

		Assert.Contains("boom", pane.Schedules.Status);
	}

	[Fact]
	public async Task AnUnsupportedDaemon_LoadsNothing()
	{
		_client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, [], 1, 0);
		_stored.Add(Schedule("own"));

		using var pane = await OpenedAsync();

		Assert.False(pane.Schedules.IsSupported);
		Assert.False(pane.Schedules.HasSchedules);
		Assert.DoesNotContain("schedules.list:" + Node, _client.Calls);
	}

	[Fact]
	public void TheRows_ShowWhenAndWhatAndTheKind()
	{
		var own = new ScheduleItemViewModel(Schedule("x", "first line\nsecond", Now.AddHours(1)), Utc);
		var cron = new ScheduleItemViewModel(Schedule("c", "tick", Now, kind: ScheduleKindDto.Cron, cron: "*/5 * * * *") with { Note = "" }, Utc);
		var mail = new ScheduleItemViewModel(Schedule("m", "reply", mailbox: true), Utc);

		Assert.Equal(("first line", "2026-11-16 11:00", Constants.Schedules.DelayedLabel), (own.PromptPreview, own.WhenText, own.KindText));
		Assert.Equal(("*/5 * * * * · next 2026-11-16 10:00", Constants.Schedules.CronLabel), (cron.WhenText, cron.KindText));
		Assert.Equal(Constants.Schedules.MailboxLabel, mail.KindText);
	}

	// ---- the tree row ----

	[Fact]
	public void ATreeRow_ShowsTheIndicatorOnlyForASessionWithSchedules()
	{
		var session = new TreeNodeViewModel("s", TreeNodeKind.Session, "S", null);
		var directory = new TreeNodeViewModel("d", TreeNodeKind.Directory, "D", null);
		var raised = new List<string?>();
		session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
		Assert.False(session.HasSchedules);

		session.State = new SessionStateDto("s", false, false, null, false, null, null, false, null, HasSchedules: true);
		directory.State = session.State;

		Assert.True(session.HasSchedules);
		Assert.Contains(nameof(TreeNodeViewModel.HasSchedules), raised);
		Assert.False(directory.HasSchedules);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
