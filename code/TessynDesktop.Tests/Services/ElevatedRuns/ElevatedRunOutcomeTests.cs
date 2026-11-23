using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>TC.ER.39 and TC.ER.40: the outcome turn, one golden text per status, and where it goes (FR.15.19, FR.15.12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunOutcomeTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private string LastDelivery() => _env.Scheduler.ElevatedDeliveries.Last().text;

	private string Log(string id) => Path.Combine(_env.RunDir(id), "output.log");

	/// <summary>Runs a request to its end: started, <paramref name="lines"/> in the log, <paramref name="after"/> later the result.</summary>
	private async Task<ElevatedRunModel> FinishAsync(string ended, int exitCode, TimeSpan after, string? lines = null, int timeout = 60, SessionNodeModel? caller = null, string? workingDir = null)
	{
		var outcome = await _env.Service.RequestAsync(caller ?? _env.Lead, _env.Req(workingDir: workingDir, timeout: JsonValue.Create(timeout)));
		var run = outcome.Run;
		_env.MakeRunning(run.Id);
		if (lines != null)
			File.WriteAllText(Log(run.Id), lines);
		_env.Clock.Advance(after);
		_env.WriteResult(run.Id, exitCode, ended);
		_env.Service.Poll();
		return _env.Service.Find(run.Id)!;
	}

	[Fact]
	public async Task Completed_WithExitCode0_AndTheTailOfTheOutput_TC_ER_39()
	{
		var run = await FinishAsync("exited", 0, TimeSpan.FromMinutes(41) + TimeSpan.FromSeconds(5), "first\r\nsecond\r\n");

		Assert.Equal(
			$"[Elevated run {run.Id} completed] because\nCommand: echo hi\nWorking directory: {_env.Alpha}\nExit code: 0\nDuration: 41m 05s\nLog: {Log(run.Id)}\nOutput (last 2 lines):\nfirst\nsecond",
			LastDelivery());
		Assert.True(Path.IsPathRooted(Log(run.Id)));
	}

	[Fact]
	public async Task Completed_WithExitCode3_IsStillCompleted_TC_ER_39()
	{
		var run = await FinishAsync("exited", 3, TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5));

		var text = LastDelivery();
		Assert.StartsWith($"[Elevated run {run.Id} completed] because\n", text);
		Assert.Contains("\nExit code: 3\nDuration: 1h 02m 05s\n", text);
		Assert.DoesNotContain("Output (last", text);
	}

	[Fact]
	public async Task DeclinedByTheUser_WithAndWithoutANote_AndNoLogLine_TC_ER_39()
	{
		var noted = await _env.RequestAsync(command: "echo 1");
		var plain = await _env.RequestAsync(command: "echo 2");
		_env.Service.Decline(noted.Id, "not now");
		_env.Service.Decline(plain.Id, null);

		var texts = _env.Scheduler.ElevatedDeliveries.Select(d => d.text).ToList();

		Assert.Equal($"[Elevated run {noted.Id} declined by the user] because\nCommand: echo 1\nWorking directory: {_env.Alpha}\nNote: not now", texts[0]);
		Assert.Equal($"[Elevated run {plain.Id} declined by the user] because\nCommand: echo 2\nWorking directory: {_env.Alpha}", texts[1]);
	}

	[Fact]
	public async Task DeclinedAtThePrompt_AndFailedToStart_TC_ER_39()
	{
		var prompt = await _env.RequestAsync(command: "echo 1");
		var broken = await _env.RequestAsync(command: "echo 2");
		_env.Service.Approve(prompt.Id);
		_env.Service.ReportLaunchFailed(prompt.Id, declined: true, null);
		_env.Service.Approve(broken.Id);
		_env.Service.ReportLaunchFailed(broken.Id, declined: false, "Access is denied");

		var texts = _env.Scheduler.ElevatedDeliveries.Select(d => d.text).ToList();

		Assert.Equal($"[Elevated run {prompt.Id} declined at the Windows prompt] because\nCommand: echo 1\nWorking directory: {_env.Alpha}", texts[0]);
		Assert.Equal($"[Elevated run {broken.Id} failed to start: Access is denied] because\nCommand: echo 2\nWorking directory: {_env.Alpha}", texts[1]);
	}

	[Fact]
	public async Task TimedOut_CancelledByTheUser_AndExpired_TC_ER_39()
	{
		var timed = await FinishAsync("timedOut", -1, TimeSpan.FromMinutes(90), timeout: 90);
		var stopped = await FinishAsync("stopped", 1, TimeSpan.FromSeconds(7));
		var pending = await _env.RequestAsync(command: "echo late");
		_env.Clock.Advance(TimeSpan.FromHours(24));
		_env.Service.Poll();

		var texts = _env.Scheduler.ElevatedDeliveries.Select(d => d.text).ToList();

		Assert.StartsWith($"[Elevated run {timed.Id} timed out after 90 min (the process was killed)] because\n", texts[0]);
		Assert.Contains("\nDuration: 1h 30m 00s\n", texts[0]);
		Assert.StartsWith($"[Elevated run {stopped.Id} cancelled by the user (stopped while running)] because\n", texts[1]);
		Assert.Contains("\nExit code: 1\nDuration: 7s\n", texts[1]);
		Assert.Equal($"[Elevated run {pending.Id} expired (not approved within 24 hours)] because\nCommand: echo late\nWorking directory: {_env.Alpha}", texts[2]);
	}

	[Fact]
	public async Task Tail_IsTheLast200Lines_EachCutTo500Characters_TC_ER_39()
	{
		var lines = string.Join("\r\n", Enumerable.Range(1, 300).Select(i => "line " + i)) + "\r\n";
		var run = await FinishAsync("exited", 0, TimeSpan.FromSeconds(1), lines);

		var text = LastDelivery();
		var tail = text[(text.IndexOf("Output (last 200 lines):\n", StringComparison.Ordinal) + "Output (last 200 lines):\n".Length)..].Split('\n');

		Assert.Equal(200, tail.Length);
		Assert.Equal("line 101", tail[0]);
		Assert.Equal("line 300", tail[^1]);
		Assert.Contains(run.Id, text);
	}

	[Fact]
	public async Task Tail_LongLinesAreCut_AndTheWholeTailIsAtMost20000Characters_TC_ER_39()
	{
		var cutRun = await FinishAsync("exited", 0, TimeSpan.FromSeconds(1), new string('x', 600) + "\r\n");
		var cutTail = LastDelivery().Split('\n').Last();
		Assert.Equal(new string('x', 500), cutTail);

		var many = string.Join("\r\n", Enumerable.Range(1, 100).Select(_ => new string('y', 400))) + "\r\n";
		await FinishAsync("exited", 0, TimeSpan.FromSeconds(1), many);
		var text = LastDelivery();
		var tailStart = text.IndexOf("\nOutput (last ", StringComparison.Ordinal) + 1;
		var header = text[tailStart..text.IndexOf('\n', tailStart)];
		var body   = text[(text.IndexOf('\n', tailStart) + 1)..];

		Assert.Equal("Output (last 49 lines):", header);
		Assert.True(body.Length <= 20_000, body.Length.ToString());
		Assert.NotEqual(cutRun.Id, string.Empty);
	}

	// ── TC.ER.40: routing ─────────────────────────────────────────────────────

	[Fact]
	public async Task Routing_AWorkersOutcomeGoesToTheWorker_NotToTheSupervisor_TC_ER_40()
	{
		var run = await FinishAsync("exited", 0, TimeSpan.FromSeconds(1), caller: _env.Wrk, workingDir: _env.Wrk.WorkingDirectory);

		var delivery = Assert.Single(_env.Scheduler.ElevatedDeliveries);
		Assert.Equal(_env.Wrk.NodeId, delivery.target);
		Assert.Equal(run.Id, delivery.runId);
	}

	[Fact]
	public async Task Routing_AWithdrawnRequestDeliversNothing_TC_ER_40()
	{
		var run = await _env.RequestAsync();

		_env.Service.Cancel(_env.Lead, run.Id);

		Assert.Empty(_env.Scheduler.ElevatedDeliveries);
	}

	[Fact]
	public async Task Routing_ASessionRemovedFromTheTreeDeliversNothingAndIsLogged_TC_ER_40()
	{
		var run = await _env.RequestAsync(_env.Other, workingDir: _env.Beta);
		foreach (var dir in _env.Settings.Settings.Tree)
			dir.Sessions.Remove(_env.Other);

		_env.Service.Decline(run.Id, null);

		Assert.Empty(_env.Scheduler.ElevatedDeliveries);
		Assert.Contains(_env.Sink.Lines(Serilog.Events.LogEventLevel.Information), l => l.Contains($"ElevatedRun {run.Id} outcome not delivered"));
	}

	[Fact]
	public async Task Routing_ExactlyOneDeliveryPerRequest_TC_ER_40()
	{
		var run = await _env.RequestAsync();
		_env.Service.Decline(run.Id, null);

		_env.Service.Poll();
		_env.NewService().Reconcile();
		_env.Clock.Advance(TimeSpan.FromDays(3));
		_env.Service.Poll();

		Assert.Single(_env.Scheduler.ElevatedDeliveries);
	}
}
