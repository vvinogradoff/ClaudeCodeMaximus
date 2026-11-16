using Microsoft.Extensions.Logging;
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
/// The review fixes of W8 / W7 (report 470): the delayed send keeps the picks (H1, A14) and refuses the one-shot toggles (M3), the time parser cannot overflow (M1)
/// and reads DST edges sensibly (L3), the import host survives a failing refresh (M2), Select all recounts once (L2).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleReviewFixesTests
{
	private const string Node = "a";
	private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
	private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();

	public ScheduleReviewFixesTests()
	{
		_client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, [RemoteProtocol.Schedules.Capability], 1, 0);
		_client.SchedulesListHandler = _ => new SchedulesListResult([]);
		_client.Catalog = new CatalogResult(
			[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus", ModelProvider.Anthropic)],
			["default", "high"],
			[new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);
	}

	private async Task<SessionPaneViewModel> OpenedAsync()
	{
		_client.Sessions[Node] = FakeSession.WithEntries(2);
		var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			Dialogs = new DialogService(_presenter, new ImmediateUiDispatcher()),
			ScheduleClock = new FixedClock(Now),
			ScheduleZone = Utc,
		};
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		return pane;
	}

	private void Answer(string text) => _presenter.Answer = dialog =>
	{
		dialog.InputText = text;
		dialog.AcceptCommand.Execute(null);
		return Task.CompletedTask;
	};

	// ---- H1: the picks travel with the delayed send ----

	[Fact]
	public async Task SendLater_CarriesTheProfileModelAndEffortThatWerePicked()
	{
		using var pane = await OpenedAsync();
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "work");
		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "high");
		pane.InputText = "later";
		Answer("1h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal(("work", "opus", "high"), Assert.Single(_client.SchedulesAddPicks));
	}

	[Fact]
	public async Task SendLater_SendsOnlyWhatWasPicked()
	{
		using var pane = await OpenedAsync();
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "work");
		pane.InputText = "later";
		Answer("1h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal(("work", null, null), Assert.Single(_client.SchedulesAddPicks));
	}

	[Fact]
	public async Task SendLater_WithNothingPicked_SendsNoPicks()
	{
		using var pane = await OpenedAsync();
		pane.InputText = "later";
		Answer("1h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal((null, null, null), Assert.Single(_client.SchedulesAddPicks));
	}

	// ---- M3: what a schedule cannot carry is refused, not dropped ----

	[Fact]
	public async Task SendLater_WithNewBranchOn_IsRefusedWithANotice_AndKeepsTheText()
	{
		using var pane = await OpenedAsync();
		pane.Instruction.ToggleNewBranchCommand.Execute(null);
		pane.InputText = "later";
		Answer("1h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal(Constants.Schedules.OneShotsNotScheduled, pane.Notice);
		Assert.Equal("later", pane.InputText);
		Assert.Empty(_client.SchedulesAddPicks);
		Assert.Empty(_presenter.Shown);   // refused before the question
		Assert.True(pane.Instruction.NewBranch);   // the flag is still there for a send now
	}

	[Fact]
	public async Task SendLater_WithAttachedFilePathsInTheText_ScheduledTheTextAsItIs()
	{
		using var pane = await OpenedAsync();
		var text = "look at this\n" + Constants.Texts.AttachedFilePrefix + @"C:\data\report.pdf";
		pane.InputText = text;
		string? scheduled = null;
		_client.SchedulesAddHandler = (nodeId, prompt, kind, at, _, _, note, _) =>
		{
			scheduled = prompt;
			return new ScheduleDto("s1", nodeId, kind, prompt, note ?? string.Empty, at, null, MissedFireDto.FireOnce, 0, 0, false);
		};
		Answer("1h");

		await pane.SendLaterCommand.ExecuteAsync(null);

		Assert.Equal(text, scheduled);
	}

	// ---- M1: no input can make the parser throw ----

	[Theory]
	[InlineData("9999999999d")]
	[InlineData("99999999999999999999d")]
	[InlineData("99999999999999999999h")]
	[InlineData("99999999999999999999")]
	[InlineData("99999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999m")]
	[InlineData("5000d")]
	[InlineData("3000d3000d")]
	[InlineData("3000d3000d3000d3000d3000d3000d3000d3000d3000d3000d3000d3000d")]
	[InlineData("3649d23h59m")]   // one minute under ten years is fine below; this is the same as the limit plus a bit
	public void AnAnswerFarBeyondTheLimit_IsRefusedAsTooFar_AndNeverThrows(string answer)
	{
		var time = ScheduleTimeParser.TryParse(answer, Now, Utc, out var error);

		if (answer == "3649d23h59m")
		{
			Assert.NotNull(time);   // 3649 d 23 h 59 min is inside ten years (3650 d)
			return;
		}

		Assert.Null(time);
		Assert.Contains("too far", error, StringComparison.Ordinal);
	}

	[Fact]
	public void TheLimitItself_IsAccepted_AndOneMinuteMoreIsNot()
	{
		Assert.NotNull(ScheduleTimeParser.TryParse("3650d", Now, Utc, out _));
		Assert.Null(ScheduleTimeParser.TryParse("3650d1m", Now, Utc, out var error));
		Assert.Contains("too far", error, StringComparison.Ordinal);
	}

	// ---- L3: DST edges of an absolute time ----

	private static TimeZoneInfo? Berlin()
	{
		foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time", "Central European Standard Time" })
		{
			try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
			catch (TimeZoneNotFoundException) { }
		}

		return null;
	}

	[Fact]
	public void ATimeInTheSpringForwardGap_IsReadAnHourLater()
	{
		var zone = Berlin();
		if (zone == null)
			return;   // no tz database on this machine

		var now = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

		var time = ScheduleTimeParser.TryParse("2026-03-29 02:30", now, zone, out _);

		// 02:30 does not exist on 29 March 2026 in Berlin (02:00 -> 03:00); it is read as 03:30 CEST = 01:30 UTC.
		Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), time!.FireAtUtc);
	}

	[Fact]
	public void AnAmbiguousAutumnTime_IsReadAsItsFirstOccurrence()
	{
		var zone = Berlin();
		if (zone == null)
			return;

		var now = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

		var time = ScheduleTimeParser.TryParse("2026-10-25 02:30", now, zone, out _);

		// 02:30 happens twice on 25 October 2026; the first is still CEST (+02:00) = 00:30 UTC.
		Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), time!.FireAtUtc);
	}

	// ---- M2: a failing refresh after an import does not escape ----

	[Fact]
	public async Task ShowImported_WhenTheDaemonDropsAfterTheImport_DoesNotThrow()
	{
		// The tree already contains a failing snapshot itself; the host's own catch is the second line of defence for the async-void handler it runs in.
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		_client.TreeSnapshotException = new InvalidOperationException("the daemon dropped");
		var logger = new RecordingLogger();

		var exception = await Record.ExceptionAsync(() => ImportPickerHost.ShowImportedAsync(tree, ["n1"], logger));

		Assert.Null(exception);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private sealed class RecordingLogger : ILogger
	{
		public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
	}
}
