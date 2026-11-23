using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using static TessynDesktop.WPF.Tests.ElevatedRunFixture;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.ER.51 to TC.ER.55 (view-model part): the facts, the approve path and the launcher, the states and the closed card of an elevated run (FR.23.32, FR.32.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunCardViewModelTests
{
	private readonly ElevatedRunFixture _f = new();

	/// <summary>Every kind of text the card must show unchanged: markup, a link, backticks, a path, trailing and double spaces, mixed line endings, a very long line.</summary>
	public static string AwkwardCommand => "<b>x</b> *bold* [link](http://x) `code` C:\\Windows\\notepad.exe  \r\ndouble  space and trailing   \n" + new string('y', 400);

	// ── TC.ER.51 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task TC_ER_51_TheFactsAreShownUnchanged_AndCopyCommandPutsTheExactTextOnTheClipboard()
	{
		var (focus, card) = await _f.ShowAsync(Run(command: AwkwardCommand));
		using var _ = focus;

		Assert.Equal(AwkwardCommand, card.Command);                    // no markup, no ellipsis, no line-ending change, spaces kept
		Assert.Equal("Run the suite as the test user", card.Reason);
		Assert.Equal(@"C:\Projects\Personal\Tessyn-wt-elevruns", card.WorkingDirectory);
		Assert.Equal("Time limit: 60 minutes", card.TimeLimitText);
		Assert.Equal("Requested by Lead (lead-nod)", card.RequestedByText);

		card.CopyCommand.Execute(null);

		Assert.Equal([AwkwardCommand], _f.Clipboard.Texts);
		Assert.False(focus.Items.Single().HasBody);                    // the body only serves clients that do not know the action
	}

	[Fact]
	public async Task TC_ER_51_AnItemThatCarriesTheAction_HasNoMarkdownBody_ButOneWithoutIsAPlainAsk()
	{
		var plain = Item(Run(), number: 2) with { Action = null };
		_f.Seed(Run());
		_f.Client.Attention[Lead].Add(plain);
		using var focus = _f.CreateFocus();

		await focus.SetSessionAsync(Lead);

		var elevated = focus.Items.Single(i => i.Id == "A1");
		var ask = focus.Items.Single(i => i.Id == "A2");
		Assert.True(elevated.IsElevatedRun);
		Assert.False(elevated.HasBody);
		Assert.False(ask.IsElevatedRun);
		Assert.True(ask.HasBody);              // an older item or one without the action stays an ordinary card
	}

	[Fact]
	public async Task TC_ER_51_AWorkersRequestOnTheSupervisorsCard_NamesTheWorker()
	{
		var run = Run(requester: Worker, name: "Wrk");
		var (focus, card) = await _f.ShowAsync(run, Lead);
		using var _ = focus;

		Assert.Equal("Requested by worker Wrk (wrk-node) of this session", card.RequestedByText);

		var (own, ownCard) = await new ElevatedRunFixture().ShowAsync(Run(requester: Worker, name: "Wrk"), Worker);
		using var __ = own;
		Assert.Equal("Requested by Wrk (wrk-node)", ownCard.RequestedByText);
	}

	// ── TC.ER.53 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task TC_ER_53_OneClickApprovesOnce_StartsExactlyTheReturnedDescription_AndReportsLaunched()
	{
		var launch = new ElevatedLaunchDto("er-1", "powershell.exe", "-EncodedCommand QUJD", @"C:\Work", @"C:\Data\er-1", "runas", 60);
		_f.Client.ElevatedLaunch = launch;
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Equal(["er-1"], _f.Client.ElevatedApproved);
		Assert.Equal([launch], _f.Launcher.Launches);
		Assert.Equal(["er-1"], _f.Client.ElevatedLaunched);
		Assert.Empty(_f.Client.ElevatedLaunchFailures);
	}

	[Fact]
	public async Task TC_ER_53_ADoubleClick_AndAClickWhileLaunching_DoNotLaunchAgain()
	{
		_f.Launcher.Gate();                                   // the Windows prompt stays open
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		var first = card.ApproveCommand.ExecuteAsync(null);
		Assert.False(card.ApproveCommand.CanExecute(null));
		await card.ApproveCommand.ExecuteAsync(null);         // the second click of a double click
		card.DeclineCommand.Execute(null);                    // Decline is unavailable too: the run is launching
		Assert.True(card.IsLaunching);
		Assert.False(card.ApproveCommand.CanExecute(null));

		_f.Launcher.Release();
		await first;

		Assert.Single(_f.Client.ElevatedApproved);
		Assert.Single(_f.Launcher.Launches);
		Assert.Single(_f.Client.ElevatedLaunched);
		Assert.Empty(_f.Client.ElevatedDeclines);
	}

	[Fact]
	public async Task TC_ER_53_ADeclinedWindowsPrompt_ReportsLaunchFailedDeclined()
	{
		_f.Launcher.Result = ElevatedLaunchResult.Declined;
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Equal([("er-1", true, (string?)null)], _f.Client.ElevatedLaunchFailures);
		Assert.Empty(_f.Client.ElevatedLaunched);
		Assert.Equal("Declined at the Windows prompt", card.OutcomeText);
	}

	[Fact]
	public async Task TC_ER_53_AFailedStart_ReportsLaunchFailedWithTheReason()
	{
		_f.Launcher.Result = ElevatedLaunchResult.Failed("the file was not found");
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Equal([("er-1", false, (string?)"the file was not found")], _f.Client.ElevatedLaunchFailures);
		Assert.Equal("Failed to start: the file was not found", card.OutcomeText);
	}

	[Fact]
	public async Task TC_ER_53_ARefusedApprove_ShowsTheDaemonsText_AndStartsNothing()
	{
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;
		_f.Client.PutElevatedRun(Run(ElevatedRunStatus.Declined));             // another client was first; this card has not heard yet
		_f.Client.ElevatedApproveException = new TessynRpcException(RemoteProtocol.ErrorCodes.DaemonError, "This request is no longer pending (declined).");

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Equal("This request is no longer pending (declined).", card.Error);
		Assert.Empty(_f.Launcher.Launches);
		Assert.Empty(_f.Client.ElevatedLaunched);
		Assert.Empty(_f.Client.ElevatedLaunchFailures);
	}

	[Fact]
	public async Task TC_ER_53_AnExpiredRequest_ShowsTheExpiredText()
	{
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;
		_f.Client.ElevatedApproveException = new TessynRpcException(RemoteProtocol.ErrorCodes.DaemonError, RemoteProtocol.ElevatedRuns.ExpiredText);

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Equal("This request expired.", card.Error);
		Assert.Empty(_f.Launcher.Launches);
	}

	[Fact]
	public async Task TC_ER_53_AFastCommandThatAlreadyFinished_CountsLaunchedAsSuccess()
	{
		_f.Client.ElevatedLaunchedException = new TessynRpcException(RemoteProtocol.ErrorCodes.DaemonError, "This request is not launching (running).");
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Null(card.Error);
		Assert.Single(_f.Launcher.Launches);
		Assert.Empty(_f.Client.ElevatedLaunchFailures);
	}

	[Fact]
	public async Task TC_ER_53_WithoutALauncher_NothingIsApproved()
	{
		_f.Seed(Run());
		using var focus = new FocusViewModel(_f.Client, new ImmediateUiDispatcher(), Microsoft.Extensions.Logging.Abstractions.NullLogger<FocusViewModel>.Instance);
		await focus.SetSessionAsync(Lead);
		var card = focus.Items.Single().Elevated!;

		await card.ApproveCommand.ExecuteAsync(null);

		Assert.Empty(_f.Client.ElevatedApproved);
		Assert.Equal("This window cannot start administrator runs.", card.Error);
	}

	[Fact]
	public async Task TC_ER_53_DeclineAsksTheDaemonWithTheNote_AndStartsNothing()
	{
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;
		card.NoteText = "  not now  ";

		await card.DeclineCommand.ExecuteAsync(null);

		Assert.Equal([("er-1", (string?)"not now")], _f.Client.ElevatedDeclines);
		Assert.Empty(_f.Client.ElevatedApproved);
		Assert.Empty(_f.Launcher.Launches);
		Assert.Equal("Declined", card.OutcomeText);
		Assert.Empty(focus.Items);                                  // the item closed
		Assert.Equal("Declined", focus.ClosedItems.Single().OutcomeText);
	}

	// ── TC.ER.54 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task TC_ER_54_PendingShowsTheButtonsAndTheExpiry()
	{
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		Assert.True(card.IsPending);
		Assert.True(card.ApproveCommand.CanExecute(null));
		Assert.True(card.DeclineCommand.CanExecute(null));
		Assert.False(card.StopCommand.CanExecute(null));
		Assert.Equal("Expires " + T0.AddMinutes(30).ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture), card.ExpiresText);
	}

	[Fact]
	public async Task TC_ER_54_Launching_ReplacesTheButtons_WaitingWhileThePromptIsOpen_ThenStarting()
	{
		_f.Launcher.Gate();
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;

		var click = card.ApproveCommand.ExecuteAsync(null);

		Assert.True(card.IsLaunching);
		Assert.True(card.IsPromptOpen);
		Assert.Equal("Waiting for the Windows administrator prompt...", card.LaunchingText);
		Assert.False(card.ApproveCommand.CanExecute(null));
		Assert.False(card.DeclineCommand.CanExecute(null));

		_f.Launcher.Release();
		await click;

		Assert.False(card.IsPromptOpen);
		Assert.Equal("Starting...", card.LaunchingText);
	}

	[Fact]
	public async Task TC_ER_54_ASecondViewModel_SeesLaunching_AndNoButtons()
	{
		_f.Launcher.Gate();
		var (focus, card) = await _f.ShowAsync(Run());
		using var _ = focus;
		var other = _f.CreateFocus();                               // another Focus view over the same daemon (another window)
		using var __ = other;
		await other.SetSessionAsync(Lead);
		var otherCard = other.Items.Single().Elevated!;

		var click = card.ApproveCommand.ExecuteAsync(null);         // the approve call returns; the prompt stays open

		Assert.True(otherCard.IsLaunching);
		Assert.Equal("Starting...", otherCard.LaunchingText);       // not its prompt
		Assert.False(otherCard.ApproveCommand.CanExecute(null));
		Assert.False(otherCard.DeclineCommand.CanExecute(null));
		Assert.False(otherCard.IsPromptOpen);

		_f.Launcher.Release();
		await click;
	}

	[Fact]
	public async Task TC_ER_54_Running_ShowsElapsedAndLimit_AndStopAsksWithoutConfirmation()
	{
		var started = T0.AddMinutes(1);
		var running = Run(ElevatedRunStatus.Running) with { StartedUtc = started };
		var (focus, card) = await _f.ShowAsync(running);
		using var _ = focus;

		Assert.True(card.IsRunning);
		Assert.Equal($"Running as administrator since {started.ToLocalTime():HH:mm} (0s, limit 60 min)", card.RunningText);
		_f.Time.Advance(TimeSpan.FromSeconds(75));
		Assert.Equal($"Running as administrator since {started.ToLocalTime():HH:mm} (1m 15s, limit 60 min)", card.RunningText);
		Assert.True(card.StopCommand.CanExecute(null));
		Assert.False(card.ApproveCommand.CanExecute(null));
		Assert.False(card.DeclineCommand.CanExecute(null));

		await card.StopCommand.ExecuteAsync(null);

		Assert.Equal(["er-1"], _f.Client.ElevatedStopped);
	}

	[Fact]
	public async Task TC_ER_54_TheOutputExpander_ShowsTheLast20Lines_AndRefreshesEvery2SecondsWhileOpen()
	{
		_f.Client.ElevatedOutputTail = "line 1\nline 2";
		var (focus, card) = await _f.ShowAsync(Run(ElevatedRunStatus.Running) with { StartedUtc = T0 });
		using var _ = focus;
		var reads = () => _f.Client.Calls.Count(c => c == "elevated.get:er-1:20");

		Assert.Equal(0, reads());                                   // collapsed: nothing is read
		_f.Time.Advance(TimeSpan.FromSeconds(6));
		Assert.Equal(0, reads());

		card.IsOutputExpanded = true;                               // opening reads at once
		Assert.Equal(1, reads());
		Assert.Equal("line 1\nline 2", card.OutputText);

		_f.Client.ElevatedOutputTail = "line 1\nline 2\nline 3";
		_f.Time.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(1, reads());
		_f.Time.Advance(TimeSpan.FromSeconds(1));                   // 2 s after the first tick pair
		Assert.True(reads() >= 2);
		Assert.Equal("line 1\nline 2\nline 3", card.OutputText);

		var before = reads();
		card.IsOutputExpanded = false;
		_f.Time.Advance(TimeSpan.FromSeconds(10));
		Assert.Equal(before, reads());                              // closed again: no more reads
	}

	[Fact]
	public async Task TC_ER_54_TheClockStops_WhenTheRunEnds()
	{
		var (focus, card) = await _f.ShowAsync(Run(ElevatedRunStatus.Running) with { StartedUtc = T0 });
		using var _ = focus;
		Assert.Equal(1, _f.Time.ActiveTimers);

		_f.Client.SetElevatedStatus("er-1", ElevatedRunStatus.Completed, r => r with { ExitCode = 0, EndedUtc = T0.AddMinutes(3) });

		Assert.True(card.IsFinal);
		Assert.Equal(0, _f.Time.ActiveTimers);
	}

	// ── TC.ER.55 ──────────────────────────────────────────────────────────────

	public static TheoryData<ElevatedRunStatus, int?, string?, ElevatedRunCancelledBy?, string> OutcomeLines => new()
	{
		{ ElevatedRunStatus.Completed, 0, null, null, "Completed: exit code 0, 3m 12s" },
		{ ElevatedRunStatus.Declined, null, null, null, "Declined" },
		{ ElevatedRunStatus.DeclinedAtPrompt, null, null, null, "Declined at the Windows prompt" },
		{ ElevatedRunStatus.FailedToStart, null, "The client did not report the start.", null, "Failed to start: The client did not report the start." },
		{ ElevatedRunStatus.TimedOut, null, null, null, "Timed out after 60 min" },
		{ ElevatedRunStatus.Cancelled, null, null, ElevatedRunCancelledBy.User, "Cancelled (stopped by the user)" },
		{ ElevatedRunStatus.Cancelled, null, null, ElevatedRunCancelledBy.Agent, "Cancelled (withdrawn by the agent)" },
		{ ElevatedRunStatus.Expired, null, null, null, "Expired" },
	};

	[Theory]
	[MemberData(nameof(OutcomeLines))]
	public async Task TC_ER_55_AClosedCard_ShowsTheOutcomeLine_TheCommandAndTheLogPath_AndNoButtons(
		ElevatedRunStatus status, int? exitCode, string? failure, ElevatedRunCancelledBy? cancelledBy, string expected)
	{
		var run = Run(status, command: AwkwardCommand) with
		{
			StartedUtc = T0, EndedUtc = T0.AddSeconds(192), ExitCode = exitCode, Failure = failure, CancelledBy = cancelledBy,
		};
		_f.Seed(run, Lead, 1, AttentionStatus.Resolved);
		using var focus = _f.CreateFocus();
		await focus.SetSessionAsync(Lead);

		var closed = focus.ClosedItems.Single();
		Assert.Empty(focus.OpenItems);
		Assert.Equal(expected, closed.OutcomeText);
		Assert.True(closed.IsElevatedRun);
		Assert.False(closed.IsBodyExpanded);

		closed.ToggleBodyCommand.Execute(null);                     // expanding shows the facts: the command block and the log path

		Assert.True(closed.IsBodyExpanded);
		Assert.Equal(AwkwardCommand, closed.Elevated!.Command);
		Assert.Equal(@"Log: C:\Data\elevated-runs\er-1\output.log", closed.Elevated.LogPathText);
		Assert.False(closed.Elevated.ApproveCommand.CanExecute(null));
		Assert.False(closed.Elevated.DeclineCommand.CanExecute(null));
		Assert.False(closed.Elevated.StopCommand.CanExecute(null));
	}

	[Fact]
	public async Task TC_ER_55_ARunThatEndsWhileTheCardIsOpen_MovesToClosed()
	{
		var (focus, card) = await _f.ShowAsync(Run(ElevatedRunStatus.Running) with { StartedUtc = T0 });
		using var _ = focus;

		_f.Client.SetElevatedStatus("er-1", ElevatedRunStatus.Completed, r => r with { ExitCode = 3, EndedUtc = T0.AddSeconds(5) });

		Assert.Empty(focus.OpenItems);
		Assert.Equal("Completed: exit code 3, 5s", focus.ClosedItems.Single().OutcomeText);
	}
}
