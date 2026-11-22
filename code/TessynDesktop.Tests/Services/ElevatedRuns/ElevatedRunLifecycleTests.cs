using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog.Events;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>TC.ER.17 to TC.ER.31: withdrawing and listing, the transition table, the first approve, expiry, the launch reports, the timeouts, the wrapper's process, the restart, the log and the record (FR.15.17, FR.15.18, FR.15.19).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunLifecycleTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private ElevatedRunModel Reload(string id) => _env.Service.Find(id)!;

	private static string Message(Action action) => Assert.ThrowsAny<Exception>(action).Message;

	// ── TC.ER.17 to 19: cancel_elevated_run ───────────────────────────────────

	[Fact]
	public async Task Cancel_OwnPendingRequest_WithdrawsItAndDeliversNothing_TC_ER_17()
	{
		var run = await _env.RequestAsync();

		var text = _env.Service.Cancel(_env.Lead, run.Id);

		var after = Reload(run.Id);
		Assert.Equal($"Elevated run {run.Id} withdrawn.", text);
		Assert.Equal(ElevatedRunStatus.Cancelled, after.Status);
		Assert.Equal(ElevatedRunCancelledBy.Agent, after.CancelledBy);
		Assert.All(_env.Attention.GetItems(_env.Lead.NodeId), item => Assert.Equal(AttentionStatus.Resolved, item.Status));
		Assert.Empty(_env.Scheduler.ElevatedDeliveries);
	}

	[Fact]
	public async Task Cancel_UnknownOrForeignId_GivesTheSameTextAndChangesNothing_TC_ER_18()
	{
		var run = await _env.RequestAsync();

		var unknown = Message(() => _env.Service.Cancel(_env.Lead, "deadbeef"));
		var foreign = Message(() => _env.Service.Cancel(_env.Other, run.Id));
		var tooShort = Message(() => _env.Service.Cancel(_env.Lead, run.Id[..3]));
		var prefix = _env.Service.Cancel(_env.Lead, run.Id[..4]);

		Assert.Equal("Unknown elevated run 'deadbeef'.", unknown);
		Assert.Equal($"Unknown elevated run '{run.Id}'.", foreign);
		Assert.Contains("too short", tooShort);
		Assert.Equal($"Elevated run {run.Id} withdrawn.", prefix);
	}

	[Fact]
	public async Task Cancel_ForeignId_IsNotChangedByTheForeignCaller_TC_ER_18()
	{
		var run = await _env.RequestAsync();

		Message(() => _env.Service.Cancel(_env.Other, run.Id));

		Assert.Equal(ElevatedRunStatus.Pending, Reload(run.Id).Status);
	}

	[Fact]
	public async Task Cancel_LaunchingRunningAndFinal_AreRefusedWithTheirStatus_TC_ER_19()
	{
		var launching = await _env.RequestAsync(command: "echo 1");
		var running   = await _env.RequestAsync(command: "echo 2");
		var declined  = await _env.RequestAsync(command: "echo 3");
		_env.Service.Approve(launching.Id);
		_env.MakeRunning(running.Id, pid: 100);
		_env.Service.Decline(declined.Id, null);

		Assert.Equal($"Elevated run {launching.Id} is already launching - the user can stop it from the card.", _env.Service.Cancel(_env.Lead, launching.Id));
		Assert.Equal($"Elevated run {running.Id} is already running - the user can stop it from the card.", _env.Service.Cancel(_env.Lead, running.Id));
		Assert.Equal($"Elevated run {declined.Id} is already declined.", _env.Service.Cancel(_env.Lead, declined.Id));
		Assert.Equal(ElevatedRunStatus.Launching, Reload(launching.Id).Status);
		Assert.Equal(ElevatedRunStatus.Running, Reload(running.Id).Status);
		Assert.Equal(ElevatedRunStatus.Declined, Reload(declined.Id).Status);
	}

	// ── TC.ER.20: list_elevated_runs ──────────────────────────────────────────

	[Fact]
	public async Task List_ShowsOnlyTheCallersOwnRuns_ActiveByDefault_NewestFirst_TC_ER_20()
	{
		var first = await _env.RequestAsync(command: "echo 1");
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		var second = await _env.RequestAsync(command: new string('c', 100), reason: new string('r', 120));
		var done = await _env.RequestAsync(command: "echo 3");
		_env.Service.Decline(done.Id, null);
		await _env.RequestAsync(_env.Other, workingDir: _env.Beta);
		await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);

		var active = _env.Service.ListText(_env.Lead, null).Split('\n');
		var all    = _env.Service.ListText(_env.Lead, "all").Split('\n');

		Assert.Equal([$"{second.Id} [pending] {new string('c', 80)} - {new string('r', 80)}", $"{first.Id} [pending] echo 1 - because"], active);
		Assert.Equal(3, all.Length);
		Assert.Equal($"{second.Id} [pending] {new string('c', 80)} - {new string('r', 80)}", all[0]);
		Assert.Single(_env.Service.ListText(_env.Wrk, null).Split('\n'));
	}

	[Fact]
	public async Task List_FinishedLines_CarryTheExitCodeAndDurationOrTheFailure_TC_ER_20()
	{
		var exited = await _env.RequestAsync(command: "echo 1");
		var failed = await _env.RequestAsync(command: "echo 2");
		_env.MakeRunning(exited.Id, pid: 11);
		_env.Clock.Advance(TimeSpan.FromSeconds(125));
		_env.WriteResult(exited.Id, exitCode: 3);
		_env.Service.Poll();
		_env.Service.Approve(failed.Id);
		_env.Service.ReportLaunchFailed(failed.Id, declined: false, "boom");

		var lines = _env.Service.ListText(_env.Lead, "all").Split('\n');

		Assert.Contains($"{exited.Id} [completed] echo 1 - because - exit 3, 2m 05s", lines);
		Assert.Contains($"{failed.Id} [failedToStart] echo 2 - because - boom", lines);
	}

	[Fact]
	public void List_WithNoRuns_SaysSo_TC_ER_20() =>
		Assert.Equal("No elevated runs.", _env.Service.ListText(_env.Lead, null));

	// ── TC.ER.21: the transition table ────────────────────────────────────────

	[Fact]
	public async Task Transitions_EveryAllowedEdgeWorks_TC_ER_21()
	{
		// pending -> launching -> running -> completed
		var a = await _env.RequestAsync(command: "a");
		_env.MakeRunning(a.Id, 1);
		_env.WriteResult(a.Id);
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Completed, Reload(a.Id).Status);

		// pending -> declined; pending -> cancelled
		var b = await _env.RequestAsync(command: "b");
		_env.Service.Decline(b.Id, null);
		var c = await _env.RequestAsync(command: "c");
		_env.Service.Cancel(_env.Lead, c.Id);
		Assert.Equal(ElevatedRunStatus.Declined, Reload(b.Id).Status);
		Assert.Equal(ElevatedRunStatus.Cancelled, Reload(c.Id).Status);

		// pending -> expired
		var d = await _env.RequestAsync(command: "d");
		_env.Clock.Advance(TimeSpan.FromHours(24));
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Expired, Reload(d.Id).Status);

		// launching -> declinedAtPrompt; launching -> failedToStart
		var e = await _env.RequestAsync(command: "e");
		_env.Service.Approve(e.Id);
		_env.Service.ReportLaunchFailed(e.Id, declined: true, null);
		var f = await _env.RequestAsync(command: "f");
		_env.Service.Approve(f.Id);
		_env.Service.ReportLaunchFailed(f.Id, declined: false, "x");
		Assert.Equal(ElevatedRunStatus.DeclinedAtPrompt, Reload(e.Id).Status);
		Assert.Equal(ElevatedRunStatus.FailedToStart, Reload(f.Id).Status);

		// running -> timedOut; running -> cancelled; running -> failedToStart
		var g = await _env.RequestAsync(command: "g");
		_env.MakeRunning(g.Id, 2);
		_env.WriteResult(g.Id, ended: "timedOut");
		var h = await _env.RequestAsync(command: "h");
		_env.MakeRunning(h.Id, 3);
		_env.WriteResult(h.Id, ended: "stopped");
		var i = await _env.RequestAsync(command: "i");
		_env.MakeRunning(i.Id, 4);
		_env.AlivePids.Remove(4);
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.TimedOut, Reload(g.Id).Status);
		Assert.Equal(ElevatedRunStatus.Cancelled, Reload(h.Id).Status);
		Assert.Equal(ElevatedRunStatus.FailedToStart, Reload(i.Id).Status);
	}

	[Fact]
	public async Task Transitions_EveryOtherEdgeIsRefused_AndAFinalStatusNeverChanges_TC_ER_21()
	{
		var pending = await _env.RequestAsync(command: "p");

		// pending -> running is not an edge: a started.json on a pending request changes nothing.
		_env.WriteStarted(pending.Id, 9);
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Pending, Reload(pending.Id).Status);
		Assert.Contains("not launching", Message(() => _env.Service.ReportLaunched(pending.Id)));
		Assert.Contains("not running", Message(() => _env.Service.Stop(pending.Id)));
		Assert.Contains("not launching", Message(() => _env.Service.ReportLaunchFailed(pending.Id, true, null)));

		var declined = await _env.RequestAsync(command: "d");
		_env.Service.Decline(declined.Id, "no");
		Assert.Equal("This request is no longer pending (declined).", Message(() => _env.Service.Approve(declined.Id)));
		Assert.Equal("This request is no longer pending (declined).", Message(() => _env.Service.Decline(declined.Id, null)));

		var done = await _env.RequestAsync(command: "r");
		_env.MakeRunning(done.Id, 7);
		_env.WriteResult(done.Id, exitCode: 4);
		_env.Service.Poll();
		_env.Service.Poll();
		_env.Clock.Advance(TimeSpan.FromDays(2));
		_env.Service.Poll();
		Assert.Throws<ElevatedRunException>(() => _env.Service.Stop(done.Id));
		Assert.Throws<ElevatedRunException>(() => _env.Service.ReportLaunchFailed(done.Id, false, "late"));
		Assert.Equal(ElevatedRunStatus.Completed, Reload(done.Id).Status);
		Assert.Equal(4, Reload(done.Id).ExitCode);
		Assert.Equal(ElevatedRunStatus.Declined, Reload(declined.Id).Status);
	}

	// ── TC.ER.22, 23: the first approve wins; expiry on approve ───────────────

	[Fact]
	public async Task Approve_OnlyTheFirstOfTwoConcurrentCallsWins_TC_ER_22()
	{
		var run = await _env.RequestAsync();
		using var gate = new System.Threading.ManualResetEventSlim();

		var calls = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
		{
			gate.Wait();
			try { return (Launch: (ElevatedRunLaunchModel?)_env.Service.Approve(run.Id), Error: (string?)null); }
			catch (ElevatedRunException ex) { return (null, ex.Message); }
		})).ToArray();
		gate.Set();
		var results = await Task.WhenAll(calls);

		Assert.Equal(1, results.Count(r => r.Launch != null));
		Assert.Equal("This request is no longer pending (launching).", results.Single(r => r.Error != null).Error);
		Assert.Equal("This request is no longer pending (launching).", Message(() => _env.Service.Approve(run.Id)));
	}

	[Fact]
	public async Task Approve_AfterTheExpiry_IsRefusedAsExpired_BeforeItWorks_TC_ER_23()
	{
		var late  = await _env.RequestAsync(command: "late");
		var early = await _env.RequestAsync(command: "early");

		_env.Clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59));
		var launch = _env.Service.Approve(early.Id);
		_env.Clock.Advance(TimeSpan.FromSeconds(2));

		Assert.Equal(early.Id, launch.RequestId);
		Assert.Equal("This request expired.", Message(() => _env.Service.Approve(late.Id)));
		Assert.Equal(ElevatedRunStatus.Expired, Reload(late.Id).Status);
	}

	// ── TC.ER.24: the sweep ───────────────────────────────────────────────────

	[Fact]
	public async Task Sweep_ExpiresAPendingRequest_ClosesItsItemsAndDeliversOnce_TC_ER_24()
	{
		var pending = await _env.RequestAsync(command: "p");
		var running = await _env.RequestAsync(command: "r");
		_env.MakeRunning(running.Id, 5);

		_env.Clock.Advance(TimeSpan.FromHours(24));
		_env.Service.Poll();
		_env.Service.Poll();

		Assert.Equal(ElevatedRunStatus.Expired, Reload(pending.Id).Status);
		Assert.Equal(ElevatedRunStatus.Running, Reload(running.Id).Status);
		var delivery = Assert.Single(_env.Scheduler.ElevatedDeliveries);
		Assert.Equal((_env.Lead.NodeId, pending.Id), (delivery.target, delivery.runId));
		Assert.Contains("expired (not approved within 24 hours)", delivery.text);
		Assert.Single(_env.Attention.GetItems(_env.Lead.NodeId), i => i.Status == AttentionStatus.Open);
	}

	[Fact]
	public async Task Sweep_AlsoRunsAtDaemonStart_TC_ER_24()
	{
		var pending = await _env.RequestAsync();
		_env.Clock.Advance(TimeSpan.FromHours(25));

		_env.NewService().Reconcile();

		Assert.Equal(ElevatedRunStatus.Expired, Reload(pending.Id).Status);
		Assert.Single(_env.Scheduler.ElevatedDeliveries);
	}

	// ── TC.ER.25: decline ─────────────────────────────────────────────────────

	[Fact]
	public async Task Decline_StoresTheNoteCutTo500_AndItReachesTheOutcome_TC_ER_25()
	{
		var run = await _env.RequestAsync();

		_env.Service.Decline(run.Id, new string('n', 700));

		var after = Reload(run.Id);
		Assert.Equal(ElevatedRunStatus.Declined, after.Status);
		Assert.Equal(500, after.Note!.Length);
		Assert.Contains("Note: " + new string('n', 500), _env.Scheduler.ElevatedDeliveries.Single().text);
	}

	[Fact]
	public async Task Decline_OnAnyOtherStatus_AnswersNoLongerPending_TC_ER_25()
	{
		var launching = await _env.RequestAsync(command: "l");
		var running   = await _env.RequestAsync(command: "r");
		var declined  = await _env.RequestAsync(command: "d");
		_env.Service.Approve(launching.Id);
		_env.MakeRunning(running.Id, 6);
		_env.Service.Decline(declined.Id, null);

		Assert.Equal("This request is no longer pending (launching).", Message(() => _env.Service.Decline(launching.Id, "x")));
		Assert.Equal("This request is no longer pending (running).", Message(() => _env.Service.Decline(running.Id, "x")));
		Assert.Equal("This request is no longer pending (declined).", Message(() => _env.Service.Decline(declined.Id, "x")));
		Assert.Throws<ElevatedRunException>(() => _env.Service.Decline("ffffffff", null));
	}

	// ── TC.ER.26: the launch reports ──────────────────────────────────────────

	[Fact]
	public async Task Launch_LaunchedThenStartedJson_MakesItRunning_TC_ER_26()
	{
		var run = await _env.RequestAsync();

		_env.Launch(run.Id);
		Assert.Equal(ElevatedRunStatus.Launching, Reload(run.Id).Status);
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Launching, Reload(run.Id).Status);
		_env.WriteStarted(run.Id, 77);
		_env.Service.Poll();

		var after = Reload(run.Id);
		Assert.Equal(ElevatedRunStatus.Running, after.Status);
		Assert.Equal(77, after.WrapperPid);
		Assert.NotNull(after.StartedUtc);
	}

	[Fact]
	public async Task Launch_Failed_DeclinedAtPromptOrFailureWithReasonCutTo500_TC_ER_26()
	{
		var prompt = await _env.RequestAsync(command: "p");
		var broken = await _env.RequestAsync(command: "b");
		_env.Service.Approve(prompt.Id);
		_env.Service.Approve(broken.Id);

		_env.Service.ReportLaunchFailed(prompt.Id, declined: true, "ignored");
		_env.Service.ReportLaunchFailed(broken.Id, declined: false, new string('f', 600));

		Assert.Equal(ElevatedRunStatus.DeclinedAtPrompt, Reload(prompt.Id).Status);
		Assert.Null(Reload(prompt.Id).Failure);
		Assert.Equal(ElevatedRunStatus.FailedToStart, Reload(broken.Id).Status);
		Assert.Equal(new string('f', 500), Reload(broken.Id).Failure);
	}

	[Fact]
	public async Task Launch_Reports_AreOnlyValidForALaunchingRequest_TC_ER_26()
	{
		var run = await _env.RequestAsync();

		Assert.Equal("This request is not launching (pending).", Message(() => _env.Service.ReportLaunched(run.Id)));
		Assert.Equal("This request is not launching (pending).", Message(() => _env.Service.ReportLaunchFailed(run.Id, false, "x")));
		_env.MakeRunning(run.Id, 3);
		Assert.Equal("This request is not launching (running).", Message(() => _env.Service.ReportLaunched(run.Id)));
	}

	// ── TC.ER.27: the start timeouts ──────────────────────────────────────────

	[Fact]
	public async Task Timeout_NoStartedJson60SecondsAfterLaunched_FailsToStart_TC_ER_27()
	{
		var run = await _env.RequestAsync();
		_env.Launch(run.Id);

		_env.Clock.Advance(TimeSpan.FromSeconds(59));
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Launching, Reload(run.Id).Status);
		_env.Clock.Advance(TimeSpan.FromSeconds(1));
		_env.Service.Poll();

		var after = Reload(run.Id);
		Assert.Equal(ElevatedRunStatus.FailedToStart, after.Status);
		Assert.Equal("The elevated process did not start within 60 seconds.", after.Failure);
	}

	[Fact]
	public async Task Timeout_NoReportWithin5MinutesOfTheApprove_FailsToStart_AnOpenPromptIsNotLimitedBefore_TC_ER_27()
	{
		var run = await _env.RequestAsync();
		_env.Service.Approve(run.Id);

		_env.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Launching, Reload(run.Id).Status);
		_env.Clock.Advance(TimeSpan.FromSeconds(1));
		_env.Service.Poll();

		Assert.Equal("The client did not report the start.", Reload(run.Id).Failure);
		Assert.Equal(ElevatedRunStatus.FailedToStart, Reload(run.Id).Status);
	}

	// ── TC.ER.28, 29 ──────────────────────────────────────────────────────────

	[Fact]
	public async Task Running_WithTheWrapperGoneAndNoResult_FailsToStart_TC_ER_28()
	{
		var run = await _env.RequestAsync();
		_env.MakeRunning(run.Id, 31);

		_env.AlivePids.Remove(31);
		_env.Service.Poll();

		Assert.Equal(ElevatedRunStatus.FailedToStart, Reload(run.Id).Status);
		Assert.Equal("The elevated process ended without a result.", Reload(run.Id).Failure);
	}

	[Fact]
	public async Task Running_WrapperGoneButResultWritten_IsTheOutcomeNotAFailure_TC_ER_28()
	{
		var run = await _env.RequestAsync();
		_env.MakeRunning(run.Id, 32);
		_env.WriteResult(run.Id, exitCode: 0);

		_env.AlivePids.Remove(32);
		_env.Service.Poll();

		Assert.Equal(ElevatedRunStatus.Completed, Reload(run.Id).Status);
	}

	[Fact]
	public async Task Restart_ReconcilesEveryOpenRequest_TC_ER_29()
	{
		var pending     = await _env.RequestAsync(command: "pending");
		var launching   = await _env.RequestAsync(command: "launching");
		var finished    = await _env.RequestAsync(command: "finished");
		_env.MakeRunning(finished.Id, 41);                       // a running request does not count against the limit of three open ones
		var alive       = await _env.RequestAsync(command: "alive");
		_env.Service.Approve(launching.Id);
		_env.WriteResult(finished.Id, exitCode: 9);
		_env.MakeRunning(alive.Id, 42);
		var expires = Reload(pending.Id).ExpiresUtc;

		var restarted = _env.NewService();
		restarted.Reconcile();

		Assert.Equal(ElevatedRunStatus.Pending, restarted.Find(pending.Id)!.Status);
		Assert.Equal(expires, restarted.Find(pending.Id)!.ExpiresUtc);
		Assert.Equal(ElevatedRunStatus.FailedToStart, restarted.Find(launching.Id)!.Status);
		Assert.Equal("The daemon restarted before the outcome was known.", restarted.Find(launching.Id)!.Failure);
		Assert.Equal(ElevatedRunStatus.Completed, restarted.Find(finished.Id)!.Status);
		Assert.Equal(9, restarted.Find(finished.Id)!.ExitCode);
		Assert.Equal(ElevatedRunStatus.Running, restarted.Find(alive.Id)!.Status);
		Assert.Contains(_env.Scheduler.ElevatedDeliveries, d => d.runId == finished.Id);
		Assert.Contains(_env.Scheduler.ElevatedDeliveries, d => d.runId == launching.Id);

		_env.WriteResult(alive.Id, exitCode: 0);
		restarted.Poll();
		Assert.Equal(ElevatedRunStatus.Completed, restarted.Find(alive.Id)!.Status);
	}

	[Fact]
	public async Task Restart_DoesNotDeliverAnOutcomeTwice_TC_ER_29()
	{
		var run = await _env.RequestAsync();
		_env.Service.Decline(run.Id, null);
		Assert.Single(_env.Scheduler.ElevatedDeliveries);

		_env.NewService().Reconcile();
		_env.Service.Poll();

		Assert.Single(_env.Scheduler.ElevatedDeliveries);
	}

	// ── TC.ER.30: the log ─────────────────────────────────────────────────────

	[Fact]
	public async Task Log_OneInformationLinePerRequestAndPerStatusChange_TC_ER_30()
	{
		var run = await _env.RequestAsync(command: "echo hi");
		_env.MakeRunning(run.Id, 50);
		_env.WriteResult(run.Id, exitCode: 2);
		_env.Service.Poll();

		var lines = _env.Sink.Lines(LogEventLevel.Information).Where(l => l.StartsWith($"ElevatedRun {run.Id} ", StringComparison.Ordinal)).ToList();

		Assert.Equal(
			[
				$"ElevatedRun {run.Id} pending session={_env.Lead.NodeId} \"Lead\" dir={_env.Alpha} exit=- command=echo hi",
				$"ElevatedRun {run.Id} launching session={_env.Lead.NodeId} \"Lead\" dir={_env.Alpha} exit=- command=echo hi",
				$"ElevatedRun {run.Id} running session={_env.Lead.NodeId} \"Lead\" dir={_env.Alpha} exit=- command=echo hi",
				$"ElevatedRun {run.Id} completed session={_env.Lead.NodeId} \"Lead\" dir={_env.Alpha} exit=2 command=echo hi",
			],
			lines);
	}

	[Fact]
	public async Task Log_ACommandOf1000Characters_IsCutTo300_TC_ER_30()
	{
		var command = "echo " + new string('z', 995);
		var run = await _env.RequestAsync(command: command);

		var line = _env.Sink.Lines(LogEventLevel.Information).Single(l => l.StartsWith($"ElevatedRun {run.Id} ", StringComparison.Ordinal));

		Assert.EndsWith("command=" + command[..300], line);
	}

	// ── TC.ER.31: the record ──────────────────────────────────────────────────

	[Fact]
	public async Task Record_IsWrittenAtomically_HoldsEveryField_SurvivesAReload_AndIsNeverDeleted_TC_ER_31()
	{
		var run = await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);
		_env.MakeRunning(run.Id, 60);
		_env.WriteResult(run.Id, exitCode: 5);
		_env.Service.Poll();

		var path = Path.Combine(_env.RunDir(run.Id), "request.json");
		var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
		var names = json.Select(p => p.Key).ToHashSet();

		foreach (var field in new[] { "Id", "RequesterNodeId", "RequesterName", "TopLevelNodeId", "TopLevelName", "Reason", "WorkingDir", "Command", "TimeoutMinutes", "CreatedUtc", "ExpiresUtc", "Status", "StatusUtc", "StartedUtc", "EndedUtc", "ExitCode" })
			Assert.Contains(field, names);
		Assert.Empty(Directory.GetFiles(_env.RunDir(run.Id), "*.tmp"));

		var reloaded = _env.NewService();
		reloaded.Reconcile();
		var again = reloaded.Find(run.Id)!;
		Assert.Equal(ElevatedRunStatus.Completed, again.Status);
		Assert.Equal(5, again.ExitCode);
		Assert.Equal(_env.Lead.NodeId, again.TopLevelNodeId);
		Assert.Equal("Wrk", again.RequesterName);
		Assert.True(Directory.Exists(_env.RunDir(run.Id)));
		Assert.True(File.Exists(path));
	}
}
