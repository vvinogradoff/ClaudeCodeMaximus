using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using TessynDesktop.Models;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// TC.ER.56 to TC.ER.61: an agent's elevated-run request end to end (FR.15.16 to FR.15.19, FR.23.31, FR.23.32, FR.32.16). The BUILT WPF app runs against a real loopback daemon whose
/// elevated-run service, wrapper script, attention ledger and RPC modules are the real ones; the request is made with the real <c>request_elevated_run</c> tool. The app is started
/// with <c>--elevated-launcher=fake:&lt;mode&gt;</c> (honoured only with <c>--test-window</c>): the Windows prompt is replaced, and an accepted run starts the daemon's launch
/// description as a normal hidden process, so the real wrapper runs the real command, unelevated. The card is read and pressed through UI Automation.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfElevatedRunE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(90);

	private readonly ElevatedRunE2EEnv _env = new();
	private readonly UiAutomationClicker _clicker = new();
	private readonly UiAutomationExpander _expander = new();
	private readonly UiAutomationNames _names = new();

	private AppProcessHarness App => _env.App;

	public void Dispose() => _env.Dispose();

	private async Task<Process> StartAsync(string mode, int expectedConnections = 1)
	{
		var process = App.Start($"--elevated-launcher=fake:{mode}");
		var ready = await WaitForCountAsync(process, "Connected to daemon run", expectedConnections) && await WaitForCountAsync(process, $"Session {_env.Lead.NodeId} opened", expectedConnections);
		Assert.True(ready, "The app did not connect and open the lead session. Log:\n" + App.ReadLog());
		Assert.True(await _clicker.ClickAsync(process, "Focus", StepTimeout), "The Focus view switch was not found. Log:\n" + App.ReadLog());
		return process;
	}

	private async Task<bool> WaitForCountAsync(Process process, string needle, int count)
	{
		var deadline = DateTime.UtcNow + StartTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (App.Count(needle) >= count)
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private async Task<string> WaitForCardAsync(Process process)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (_clicker.GetText(process, "Command") is { } command)
				return command;

			await Task.Delay(250);
		}

		Assert.Fail("The elevated-run card did not appear in the Focus panel. Log:\n" + App.ReadLog());
		return string.Empty;
	}

	private async Task<bool> WaitForCardGoneAsync(Process process, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (_clicker.GetText(process, "Command") == null)
				return true;

			await Task.Delay(250);
		}

		return false;
	}

	private async Task<string?> WaitForOutcomeAsync(string nodeId, string runId)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (_env.OutcomeFor(nodeId, runId) is { } text)
				return text;

			await Task.Delay(200);
		}

		return null;
	}

	private void AssertNoErrors()
	{
		var log = App.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task TC_ER_56_The_card_shows_the_request_Run_as_administrator_runs_it_and_the_agent_gets_the_outcome()
	{
		const string command = "cmd /c echo hi & exit 3";
		using var process = await StartAsync("accept");
		try
		{
			var id = await _env.RequestAsync(command, "Check the exit code");
			Assert.Equal(command, await WaitForCardAsync(process));                                                    // the exact text, read through UI Automation
			Assert.Equal("Check the exit code", _clicker.GetText(process, "Reason"));
			Assert.Equal(_env.WorkDir, _clicker.GetText(process, "Working directory"));
			Assert.True(_clicker.Exists(process, "Run as administrator"));
			Assert.True(_clicker.Exists(process, "Decline"));

			Assert.True(await _clicker.ClickAsync(process, "Run as administrator", StepTimeout), "The button was not found. Log:\n" + App.ReadLog());

			var done = await _env.WaitForStatusAsync(id, RunTimeout, ElevatedRunStatus.Completed);
			Assert.NotNull(done);
			Assert.Equal(3, done.ExitCode);
			Assert.True(await WaitForCardGoneAsync(process, StepTimeout), "The card stayed open after the run ended. Log:\n" + App.ReadLog());

			var outcome = await WaitForOutcomeAsync(_env.Lead.NodeId, id);
			Assert.NotNull(outcome);
			Assert.Contains($"[Elevated run {id} completed]", outcome);
			Assert.Contains("Exit code: 3", outcome);
			Assert.Contains("hi", outcome);
			AssertNoErrors();
		}
		finally
		{
			App.Stop(process);
		}
	}

	[Fact]
	public async Task TC_ER_57_Decline_with_a_note_closes_the_card_and_nothing_is_launched()
	{
		using var process = await StartAsync("accept");
		try
		{
			var id = await _env.RequestAsync("cmd /c echo never");
			await WaitForCardAsync(process);

			Assert.True(await _clicker.SetTextAsync(process, "Note for the agent (optional)", "not now", StepTimeout), "The note box was not found. Log:\n" + App.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Decline", StepTimeout), "Decline was not found. Log:\n" + App.ReadLog());

			var run = await _env.WaitForStatusAsync(id, StepTimeout, ElevatedRunStatus.Declined);
			Assert.NotNull(run);
			Assert.Equal("not now", run.Note);
			Assert.Null(run.StartedUtc);
			Assert.False(File.Exists(Path.Combine(_env.RunsRoot, id, "started.json")), "Nothing may have been launched.");
			Assert.True(await WaitForCardGoneAsync(process, StepTimeout));

			var outcome = await WaitForOutcomeAsync(_env.Lead.NodeId, id);
			Assert.NotNull(outcome);
			Assert.Contains("declined by the user", outcome);
			Assert.Contains("Note: not now", outcome);
			AssertNoErrors();
		}
		finally
		{
			App.Stop(process);
		}
	}

	[Fact]
	public async Task TC_ER_58_A_declined_Windows_prompt_closes_the_card_and_a_new_request_can_be_approved_afterwards()
	{
		using var process = await StartAsync("decline");
		try
		{
			var first = await _env.RequestAsync("cmd /c echo first");
			await WaitForCardAsync(process);
			Assert.True(await _clicker.ClickAsync(process, "Run as administrator", StepTimeout));

			Assert.NotNull(await _env.WaitForStatusAsync(first, StepTimeout, ElevatedRunStatus.DeclinedAtPrompt));
			Assert.True(await WaitForCardGoneAsync(process, StepTimeout));
			var outcome = await WaitForOutcomeAsync(_env.Lead.NodeId, first);
			Assert.NotNull(outcome);
			Assert.Contains("declined at the Windows prompt", outcome);

			var second = await _env.RequestAsync("cmd /c echo second");
			await WaitForCardAsync(process);
			Assert.True(await _clicker.ClickAsync(process, "Run as administrator", StepTimeout), "The new request could not be approved. Log:\n" + App.ReadLog());
			Assert.NotNull(await _env.WaitForStatusAsync(second, StepTimeout, ElevatedRunStatus.DeclinedAtPrompt));
			AssertNoErrors();
		}
		finally
		{
			App.Stop(process);
		}
	}

	[Fact]
	public async Task TC_ER_58_A_failed_start_closes_the_card_with_the_failure_and_the_agent_is_told()
	{
		using var process = await StartAsync("fail");
		try
		{
			var id = await _env.RequestAsync("cmd /c echo hi");
			await WaitForCardAsync(process);
			Assert.True(await _clicker.ClickAsync(process, "Run as administrator", StepTimeout));

			var run = await _env.WaitForStatusAsync(id, StepTimeout, ElevatedRunStatus.FailedToStart);
			Assert.NotNull(run);
			Assert.Contains("fake launcher: the start failed", run.Failure);
			Assert.True(await WaitForCardGoneAsync(process, StepTimeout));
			var outcome = await WaitForOutcomeAsync(_env.Lead.NodeId, id);
			Assert.NotNull(outcome);
			Assert.Contains("failed to start", outcome);
			Assert.Contains("fake launcher: the start failed", outcome);
			AssertNoErrors();
		}
		finally
		{
			App.Stop(process);
		}
	}

	[Fact]
	public async Task TC_ER_59_Stop_on_the_running_card_cancels_the_run_within_10_seconds_and_the_process_is_gone()
	{
		using var process = await StartAsync("accept");
		try
		{
			var id = await _env.RequestAsync("ping -n 120 127.0.0.1", timeoutMinutes: 5);
			await WaitForCardAsync(process);
			Assert.True(await _clicker.ClickAsync(process, "Run as administrator", StepTimeout));
			Assert.NotNull(await _env.WaitForStatusAsync(id, RunTimeout, ElevatedRunStatus.Running));

			// FR.23.42: the approved run is in the Running group (header, Stop, Output) and is not an open Ask any more; the item stays open but counts for nothing (FR.23.41).
			Assert.NotNull(await _names.WaitForTextAsync(process, n => n == "Running (1)", StepTimeout));
			Assert.True(_clicker.Exists(process, "Stop"), "The running card has no Stop button. Log:\n" + App.ReadLog());
			Assert.False(_clicker.Exists(process, "Run as administrator"), "The approved card is still offered for approval.");
			var inProgress = _env.Refs.Attention.GetItems(_env.Lead.NodeId).Single(i => i.ElevatedRunId == id);
			Assert.Equal(AttentionStatus.Open, inProgress.Status);
			Assert.Equal(AttentionKind.Ask, inProgress.Kind);
			Assert.Equal(0, _env.Refs.Attention.GetSummary(_env.Lead.NodeId).OpenCount);

			Assert.True(await _clicker.ClickAsync(process, "Stop", StepTimeout), "The Stop button was not found on the running card. Log:\n" + App.ReadLog());

			var cancelled = await _env.WaitForStatusAsync(id, TimeSpan.FromSeconds(10), ElevatedRunStatus.Cancelled);
			Assert.NotNull(cancelled);
			Assert.Equal(ElevatedRunCancelledBy.User, cancelled.CancelledBy);
			Assert.True(await WaitForCardGoneAsync(process, TimeSpan.FromSeconds(10)));
			Assert.True(await ProcessGoneAsync(id), "The command's process is still running.");

			var outcome = await WaitForOutcomeAsync(_env.Lead.NodeId, id);
			Assert.NotNull(outcome);
			Assert.Contains("cancelled by the user", outcome);
			Assert.Contains("Pinging", outcome);                                       // the output so far
			AssertNoErrors();
		}
		finally
		{
			App.Stop(process);
		}
	}

	[Fact]
	public async Task TC_ER_60_A_workers_request_shows_on_the_supervisors_card_both_items_close_and_the_outcome_goes_to_the_worker()
	{
		using var process = await StartAsync("accept");
		try
		{
			var id = await _env.RequestAsync("cmd /c echo from the worker", caller: _env.Wrk);
			await WaitForCardAsync(process);

			var requestedBy = await _names.WaitForTextAsync(process, n => n.StartsWith("Requested by worker Wrk (", StringComparison.Ordinal) && n.EndsWith(") of this session", StringComparison.Ordinal), StepTimeout);
			Assert.NotNull(requestedBy);

			Assert.True(await _clicker.ClickAsync(process, "Run as administrator", StepTimeout));
			Assert.NotNull(await _env.WaitForStatusAsync(id, RunTimeout, ElevatedRunStatus.Completed));
			Assert.True(await WaitForCardGoneAsync(process, StepTimeout));

			foreach (var node in new[] { _env.Lead, _env.Wrk })
			{
				var item = _env.Refs.Attention.GetItems(node.NodeId).Single(i => i.ElevatedRunId == id);
				Assert.Equal(AttentionStatus.Resolved, item.Status);
			}

			var outcome = await WaitForOutcomeAsync(_env.Wrk.NodeId, id);
			Assert.NotNull(outcome);
			Assert.Contains($"[Elevated run {id} completed]", outcome);
			Assert.All(_env.DeliveriesOf(id), d => Assert.Equal(_env.Wrk.NodeId, d.target));   // not to the lead session
			AssertNoErrors();
		}
		finally
		{
			App.Stop(process);
		}
	}

	[Fact]
	public async Task TC_ER_61_Closing_the_client_mid_run_does_not_lose_the_outcome_and_a_restarted_client_shows_the_closed_card()
	{
		string id;
		using (var first = await StartAsync("accept"))
		{
			try
			{
				id = await _env.RequestAsync("ping -n 6 127.0.0.1");
				await WaitForCardAsync(first);
				Assert.True(await _clicker.ClickAsync(first, "Run as administrator", StepTimeout));
				Assert.NotNull(await _env.WaitForStatusAsync(id, RunTimeout, ElevatedRunStatus.Running));
			}
			finally
			{
				App.Stop(first);                                                         // the daemon (this test's host) keeps running
			}
		}

		var done = await _env.WaitForStatusAsync(id, RunTimeout, ElevatedRunStatus.Completed);
		Assert.NotNull(done);
		Assert.Equal(0, done.ExitCode);
		Assert.NotNull(await WaitForOutcomeAsync(_env.Lead.NodeId, id));

		using var second = await StartAsync("accept", expectedConnections: 2);
		try
		{
			Assert.Null(_clicker.GetText(second, "Command"));                              // no open card
			Assert.True(_expander.TrySet(second, "FocusClosedExpander", expand: true), "The Closed expander was not found. Log:\n" + App.ReadLog());
			var line = await _names.WaitForTextAsync(second, n => n.Contains("Completed: exit code 0", StringComparison.Ordinal), StepTimeout);
			Assert.NotNull(line);
			Assert.False(_clicker.Exists(second, "Run as administrator"));
			Assert.False(_clicker.Exists(second, "Stop"));
			AssertNoErrors();
		}
		finally
		{
			App.Stop(second);
		}
	}

	/// <summary>The command's cmd.exe (from <c>started.json</c>) no longer exists; polled for a few seconds, since the wrapper ends the tree about a second after the stop.</summary>
	private async Task<bool> ProcessGoneAsync(string id)
	{
		var started = JsonNode.Parse(File.ReadAllText(Path.Combine(_env.RunsRoot, id, "started.json")))!;
		var pid = started["commandPid"]!.GetValue<int>();
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using var p = Process.GetProcessById(pid);
				if (p.HasExited)
					return true;
			}
			catch (ArgumentException)
			{
				return true;
			}

			await Task.Delay(250);
		}

		return false;
	}
}
