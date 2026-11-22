using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models.Engines;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services.Engines;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// Stop (TC.CX.17), server requests (TC.CX.18), a host crash (TC.CX.19), one host per profile (TC.CX.08), the agent token staying in the pipe
/// (TC.CX.11), queued corrections (FR.33.12) and what <see cref="TessynDesktop.Services.CodexSessionEngine.IsAlive"/> reports.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexSessionEngineStopAndHostTests : IAsyncLifetime
{
	private readonly CodexEngineFixture _f = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _f.DisposeAsync();

	/// <summary>Starts a turn that streams <paramref name="deltas"/> and then hangs, and waits until the deltas have reached the host.</summary>
	private async Task<(Task Turn, RecordingEngineSink Sink, CancellationTokenSource Stop)> StartHangingTurnAsync(
		TessynDesktop.Models.SessionNodeModel node)
	{
		var stop = new CancellationTokenSource();
		var sink = new RecordingEngineSink();
		var turn = _f.RunAsync(node, "Write a long essay", ct: stop.Token, sink: sink);
		await _f.Eventually.TrueAsync(() => _f.Launcher.Servers.Count > 0 && _f.Requests("turn/start").Length > 0, "the turn was started");
		await _f.Eventually.TrueAsync(() => _f.Server.Received.Count(m => m.Method == "turn/start") > 0, "turn/start was received");
		return (turn, sink, stop);
	}

	[Fact]
	public async Task TC_CX_17_Stop_interrupts_the_turn_and_writes_the_streamed_text_with_an_interrupted_line_before_the_cancellation()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.StreamingThenHang(start.ThreadId, start.TurnId, "The first ", "paragraph");
		var (turn, sink, stop) = await StartHangingTurnAsync(node);
		await Task.Delay(200);

		stop.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
		var interrupt = Assert.Single(_f.Requests("turn/interrupt"));
		Assert.Equal(node.CodexThreadId, interrupt.GetProperty("threadId").GetString());
		Assert.Equal(["AssistantText(The first paragraph\n[interrupted], limit=False)"], sink.Calls);
		Assert.Empty(_f.Requests("thread/unsubscribe"));
		Assert.Equal(CodexHostState.Running, _f.Pool.Hosts.Single().State);
	}

	[Fact]
	public async Task TC_CX_17_Stop_before_any_delta_writes_nothing()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.StreamingThenHang(start.ThreadId, start.TurnId);
		var (turn, sink, stop) = await StartHangingTurnAsync(node);
		await Task.Delay(100);

		stop.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
		Assert.Empty(sink.Calls);
	}

	[Fact]
	public async Task TC_CX_17_an_unconfirmed_Stop_ends_the_turn_after_15_s_unsubscribes_the_thread_and_leaves_the_host_and_other_sessions_running()
	{
		var stuck = _f.Node();
		var other = _f.Node();
		_f.Launcher.Configure = server =>
		{
			server.TurnScript = start => start.ThreadId == stuck.CodexThreadId
				? server.Scripts.StreamingThenHang(start.ThreadId, start.TurnId, "partial")
				: server.Scripts.TextTurn(start.ThreadId, start.TurnId, "Other session answer.");
			server.On("turn/interrupt", call => call.ReplyEmptyAsync()); // answers, but never sends turn/completed
		};
		var (turn, sink, stop) = await StartHangingTurnAsync(stuck);
		var pid = _f.Pool.Hosts.Single().ProcessId;

		stop.Cancel();
		await _f.Eventually.TrueAsync(() => _f.Requests("turn/interrupt").Length == 1, "turn/interrupt was sent");
		await Task.Delay(100);
		Assert.False(turn.IsCompleted);
		_f.EngineClock.Advance(TimeSpan.FromSeconds(15));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
		Assert.Equal(["Error(Stop did not confirm within 15 s; the Codex turn may still be finishing., auth=False, limit=False)"], sink.Calls);
		var unsubscribe = Assert.Single(_f.Requests("thread/unsubscribe"));
		Assert.Equal(stuck.CodexThreadId, unsubscribe.GetProperty("threadId").GetString());

		var host = _f.Pool.Hosts.Single();
		Assert.Equal(CodexHostState.Running, host.State);
		Assert.Equal(pid, host.ProcessId);
		var otherSink = await _f.RunAsync(other, "still there?");
		Assert.Contains("AssistantText(Other session answer., limit=False)", otherSink.Calls);
		Assert.Single(_f.Launcher.Servers);
	}

	[Fact]
	public async Task TC_CX_17_InterruptAsync_and_Stop_reach_the_running_turn_in_band()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.StreamingThenHang(start.ThreadId, start.TurnId, "abc");
		var (turn, sink, _) = await StartHangingTurnAsync(node);
		await Task.Delay(100);

		Assert.True(await _f.Engine.InterruptAsync(node.NodeId));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
		Assert.Single(_f.Requests("turn/interrupt"));
		Assert.False(await _f.Engine.InterruptAsync(node.NodeId));
	}

	[Fact]
	public async Task TC_CX_18_every_server_request_is_answered_and_the_turn_completes()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.ServerRequestTurn(start.ThreadId, start.TurnId);

		var sink = await _f.RunAsync(node);

		Assert.True(sink.Ends.Single().Success);
		var answers = _f.Server.ServerRequestAnswers.ToList();
		Assert.True(answers.Count >= 5);
		Assert.Equal("accept", answers[0].GetProperty("result").GetProperty("decision").GetString());
		Assert.Equal("accept", answers[1].GetProperty("result").GetProperty("decision").GetString());
		Assert.Equal("decline", answers[3].GetProperty("result").GetProperty("action").GetString());
		Assert.Equal(-32601, answers[4].GetProperty("error").GetProperty("code").GetInt32());
	}

	[Fact]
	public async Task TC_CX_19_a_host_crash_mid_turn_ends_it_with_the_stopped_text_and_the_next_send_starts_a_new_host_and_resumes()
	{
		var node = _f.Node();
		await _f.RunAsync(node, "one");
		var threadId = node.CodexThreadId;
		_f.Server.TurnScript = start => _f.Scripts.CrashMidTurn(start.ThreadId, start.TurnId, 3, "panic: boom");

		var crashed = await _f.RunAsync(node, "two");

		var end = crashed.Ends.Single();
		Assert.False(end.Success);
		Assert.Equal("Codex stopped unexpectedly (exit 3). Send again to continue.", end.ErrorText);

		var next = await _f.RunAsync(node, "three");

		Assert.Equal(2, _f.Launcher.Servers.Count);
		Assert.Equal(threadId, Assert.Single(_f.Requests("thread/resume")).GetProperty("threadId").GetString());
		Assert.True(next.Ends.Single().Success);
		Assert.Equal(threadId, node.CodexThreadId);
	}

	[Fact]
	public async Task TC_CX_08_two_sessions_on_one_profile_share_one_host_and_another_profile_gets_its_own()
	{
		var a = _f.Node();
		var b = _f.Node();
		_f.Profile = new CodexProfileRef("codex_1", @"C:\Users\u\AppData\Roaming\TessynDesktop\codex-profiles\codex_1");
		await _f.RunAsync(a);
		await _f.RunAsync(b);
		Assert.Single(_f.Launcher.Servers);

		_f.Profile = new CodexProfileRef("codex_2", @"C:\Users\u\AppData\Roaming\TessynDesktop\codex-profiles\codex_2");
		var c = _f.Node();
		await _f.RunAsync(c);

		Assert.Equal(2, _f.Launcher.Servers.Count);
		Assert.Equal(2, _f.Pool.Hosts.Count);
		Assert.Equal(@"C:\Users\u\AppData\Roaming\TessynDesktop\codex-profiles\codex_2", _f.Launcher.Launches[1].CodexHome);
		Assert.Contains("cli_auth_credentials_store=\"file\"", _f.Launcher.Launches[1].Arguments);
	}

	[Fact]
	public async Task TC_CX_08_Codex_Default_is_launched_without_CODEX_HOME()
	{
		await _f.RunAsync(_f.Node());

		var launch = _f.Launcher.Launches.Single();
		Assert.Null(launch.CodexHome);
		Assert.Equal(CodexEngineFixture.Executable, launch.ExecutablePath);
		Assert.Equal(["app-server"], launch.Arguments);
		Assert.Equal(Constants.Codex.Profiles.DefaultProfileId, _f.Pool.Hosts.Single().ProfileId);
	}

	[Fact]
	public async Task TC_CX_08_a_host_whose_threads_were_detached_stops_after_15_idle_minutes()
	{
		var node = _f.Node();
		await _f.RunAsync(node);
		var host = _f.Pool.Hosts.Single();
		Assert.False(host.IsIdleShutdownArmed); // the thread is still loaded

		Assert.True(_f.Engine.DetachConversation(node));
		await _f.Eventually.TrueAsync(() => host.IsIdleShutdownArmed, "the host has nothing loaded");
		_f.HostClock.Advance(TimeSpan.FromMinutes(15));

		await _f.Eventually.TrueAsync(() => host.State == CodexHostState.Stopped, "the idle host stopped");
	}

	[Fact]
	public void TC_CX_08_the_pool_refuses_a_second_home_for_one_profile_id()
	{
		_f.Pool.GetHost(new CodexProfileRef("codex_1", @"C:\a"));

		Assert.Throws<InvalidOperationException>(() => _f.Pool.GetHost(new CodexProfileRef("codex_1", @"C:\b")));
		Assert.Same(_f.Pool.GetHost(new CodexProfileRef("codex_1", @"C:\a")), _f.Pool.GetHost(new CodexProfileRef("CODEX_1", @"c:\A")));
	}

	[Fact]
	public async Task TC_CX_11_two_sessions_on_one_host_get_their_own_token_and_no_token_reaches_a_file_or_a_command_line()
	{
		var a = _f.Node();
		var b = _f.Node();

		await _f.RunAsync(a);
		await _f.RunAsync(b);

		var tokens = _f.Requests("thread/start")
			.Select(s => s.GetProperty("config").GetProperty("mcp_servers").GetProperty("cmx").GetProperty("http_headers").GetProperty("X-CMX-Token").GetString())
			.ToList();
		Assert.Equal([a.AgentToken, b.AgentToken], tokens);
		Assert.Single(_f.Launcher.Servers);
		Assert.All(_f.Launcher.Launches, l => Assert.DoesNotContain(l.Arguments, arg => arg.Contains(a.AgentToken) || arg.Contains(b.AgentToken)));
		Assert.Equal(0, _f.Mcp.ConfigFileRequests);
		_f.Settings.Save();
		foreach (var file in Directory.EnumerateFiles(_f.Dir, "*", SearchOption.AllDirectories))
		{
			var text = File.ReadAllText(file);
			if (Path.GetFileName(file) == "appsettings.json")
			{
				// The session's own AgentToken field (FR.14.3) is the one place it is kept; the thread config is not written anywhere.
				Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, a.AgentToken));
				Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, b.AgentToken));
				Assert.DoesNotContain("mcp_servers", text);
				continue;
			}
			Assert.DoesNotContain(a.AgentToken, text);
			Assert.DoesNotContain(b.AgentToken, text);
		}
		Assert.Empty(Directory.EnumerateFiles(_f.Dir, "config.toml", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task TC_CX_11_the_preparation_and_the_redacted_config_never_print_the_token()
	{
		var node = _f.Node();
		var preparation = await _f.Engine.PrepareTurnAsync(node, CodexEngineFixture.Chosen());

		Assert.DoesNotContain(node.AgentToken, preparation!.ToString());
		var redacted = _f.Config.Redact(await _f.Config.BuildAsync(node));
		Assert.DoesNotContain(node.AgentToken, redacted.ToJsonString());
		Assert.Equal("<redacted>", redacted["mcp_servers"]!["cmx"]!["http_headers"]!["X-CMX-Token"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_mid_run_correction_is_queued_and_sent_with_the_next_turn_only()
	{
		var node = _f.Node();
		await _f.RunAsync(node, "one");

		_f.Engine.SendCorrection(node, CodexEngineFixture.Chosen(), Constants.Instructions.MidRunAutoCommitOff);
		Assert.Single(_f.Requests("turn/start"));
		await _f.RunAsync(node, "two");
		await _f.RunAsync(node, "three");

		var inputs = _f.Requests("turn/start").Select(CodexEngineFixture.InputText).ToList();
		Assert.Equal("two\n- " + Constants.Instructions.MidRunAutoCommitOff, inputs[1]);
		Assert.Equal("three", inputs[2]);
	}

	[Fact]
	public async Task IsAlive_is_false_even_with_a_loaded_thread_so_headless_turns_are_never_redirected()
	{
		var node = _f.Node();
		await _f.RunAsync(node);

		Assert.True(_f.Pool.Hosts.Single().IsThreadLoaded(node.CodexThreadId!));
		Assert.False(_f.Engine.IsAlive(node.NodeId));
		Assert.Equal(new EngineCapabilities(false, false, true, false, true, false, false, false), _f.Engine.Capabilities);
		await Assert.ThrowsAsync<NotSupportedException>(() => _f.Engine.RunTurnInLiveProcessAsync(node, "x", new RecordingEngineSink(), default));
	}

	[Fact]
	public async Task TerminateAll_kills_every_host_and_the_next_turn_starts_again()
	{
		var node = _f.Node();
		await _f.RunAsync(node);

		_f.Engine.TerminateAll();

		Assert.Equal(CodexHostState.Stopped, _f.Pool.Hosts.Single().State);
		await _f.RunAsync(node);
		Assert.Equal(2, _f.Launcher.Servers.Count);
	}

	[Fact]
	public async Task Codex_not_found_ends_the_turn_with_the_install_hint()
	{
		await using var pool = new CodexHostPool(new FakeCodexProcessLauncher(), new CodexExecutableLocator(new FakeCodexLocatorEnvironment()));
		var engine = new TessynDesktop.Services.CodexSessionEngine(pool, _f.Config, _f.Files, _f.Settings);
		var node   = _f.Node();
		var sink   = new RecordingEngineSink();

		await engine.RunTurnAsync(new EngineTurnRequest(node, "Hi", "Hi", null, CodexEngineFixture.Chosen(), null, EngineTurnMode.Headless), sink, default);

		Assert.Equal("Codex could not start the turn: Codex not found. Install it with: winget install OpenAI.Codex", sink.Ends.Single().ErrorText);
	}
}
