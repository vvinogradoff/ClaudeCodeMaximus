using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Events;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Tests.Codex.Fakes;

namespace TessynDesktop.Tests.Codex;

/// <summary>
/// The host and the mapper together over the in-process fake app-server: whole turns as phase 2b will drive them
/// (TC.CX.11, TC.CX.14, TC.CX.17, TC.CX.18, TC.CX.19, TC.CX.33 without the seam).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CodexTurnIntegrationTests
{
	private readonly Eventually _eventually = new();

	[Fact]
	public async Task ToolTurn_RunsToCompletion_ThroughTheHost()
	{
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => s.Scripts.ToolTurn(t.ThreadId, t.TurnId));
		var run = await RunTurnAsync(f, "do the checks");

		var done = await run.CompletedAsync();
		Assert.Equal(CodexTurnOutcome.Completed, done.Outcome);
		Assert.Equal("I'll run the three checks in order.\n\nAll three checks passed.", done.AssistantText);
		Assert.Equal(6, run.Events.OfType<CodexToolStartedEvent>().Count());
		Assert.Equal(6, run.Events.OfType<CodexToolFinishedEvent>().Count());
		Assert.Equal(20653 + 21000, done.InputTokens);
	}

	// TC.CX.18
	[Fact]
	public async Task TurnWithServerRequests_Completes()
	{
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => s.Scripts.ServerRequestTurn(t.ThreadId, t.TurnId));
		var run = await RunTurnAsync(f, "go");

		var done = await run.CompletedAsync();
		Assert.Equal(CodexTurnOutcome.Completed, done.Outcome);
		Assert.Equal("Done after the requests.", done.AssistantText);
	}

	// TC.CX.17
	[Fact]
	public async Task Interrupt_EndsTheTurnAsCancelled_WithThePartialText_AndASecondThreadKeepsStreaming()
	{
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => t.Params.GetProperty("input")[0].GetProperty("text").GetString() == "long"
			? s.Scripts.StreamingThenHang(t.ThreadId, t.TurnId, "Part one. ", "Part two")
			: s.Scripts.TextTurn(t.ThreadId, t.TurnId, "Other session answer."));

		var longRun = await RunTurnAsync(f, "long");
		await _eventually.TrueAsync(() => longRun.Events.OfType<CodexTextDeltaEvent>().Count() == 2, "both deltas streamed");

		var otherRun = await RunTurnAsync(f, "short");
		await f.Host.InterruptTurnAsync(longRun.ThreadId, longRun.TurnId);

		var interrupted = await longRun.CompletedAsync();
		Assert.Equal(CodexTurnOutcome.Interrupted, interrupted.Outcome);
		Assert.Equal("Part one. Part two\n[interrupted]", Assert.Single(longRun.Events.OfType<CodexAssistantMessageEvent>()).Text);

		var other = await otherRun.CompletedAsync();
		Assert.Equal("Other session answer.", other.AssistantText);
		Assert.Single(f.Launcher.Servers);
		Assert.Equal(CodexHostState.Running, f.Host.State);
	}

	// TC.CX.19
	[Fact]
	public async Task CrashMidTurn_LeavesTheTurnUnfinished_AndReportsTheCrash()
	{
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => s.Scripts.CrashMidTurn(t.ThreadId, t.TurnId, 3, "panic at thread-store"));
		var run = await RunTurnAsync(f, "go");

		await _eventually.TrueAsync(() => !f.Crashes.IsEmpty, "the crash was reported");
		Assert.False(run.Mapper.IsCompleted);
		Assert.Equal("Starting the", string.Concat(run.Events.OfType<CodexTextDeltaEvent>().Select(e => e.Text)));
		Assert.Equal(3, f.Crashes.Single().ExitCode);
		Assert.Equal(["panic at thread-store"], f.Crashes.Single().StderrTail);
	}

	// TC.CX.33 (the Codex side): native compaction is a turn of its own with a contextCompaction item
	[Fact]
	public async Task Compaction_GivesACompactionEvent_ThroughAMapperWithoutATurnId()
	{
		await using var f = new CodexHostFixture();
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });
		var mapper = new CodexTurnMapper(thread.Thread.Id);
		var events = new ConcurrentQueue<CodexTurnEvent>();
		f.Host.NotificationReceived += n => { foreach (var e in mapper.Map(n)) events.Enqueue(e); };

		await f.Host.CompactThreadAsync(thread.Thread.Id);

		await _eventually.TrueAsync(() => mapper.IsCompleted, "the compaction turn completed");
		Assert.Single(events.OfType<CodexCompactionEvent>());
		Assert.True(f.Host.IsThreadLoaded(thread.Thread.Id));
	}

	// TC.CX.11 (the host side): each thread carries its own cmx token in its config; nothing goes on the command line
	[Fact]
	public async Task TwoThreads_GetTheirOwnTokens_InTheirConfig_AndNoneOnTheCommandLine()
	{
		await using var f = new CodexHostFixture();
		await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\A", Config = new JsonObject { ["mcp_servers.cmx.http_headers"] = new JsonObject { ["X-CMX-Token"] = "TOKEN-A" } } });
		await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\B", Config = new JsonObject { ["mcp_servers.cmx.http_headers"] = new JsonObject { ["X-CMX-Token"] = "TOKEN-B" } } });

		var tokens = f.Server.RequestsOf("thread/start")
			.Select(m => m.Params.GetProperty("config").GetProperty("mcp_servers.cmx.http_headers").GetProperty("X-CMX-Token").GetString())
			.ToList();
		Assert.Equal(["TOKEN-A", "TOKEN-B"], tokens);
		Assert.DoesNotContain(f.Launcher.Launches.Single().Arguments, a => a.Contains("TOKEN"));
	}

	private async Task<TurnRun> RunTurnAsync(CodexHostFixture f, string text)
	{
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha", Model = "gpt-x-sol" });
		var run    = new TurnRun(thread.Thread.Id);
		f.Host.NotificationReceived += run.OnNotification;

		var turn = await f.Host.StartTurnAsync(new CodexTurnStartParams { ThreadId = thread.Thread.Id, Input = [CodexUserInput.FromText(text)], Model = "gpt-x-sol" });
		run.SetTurn(turn.Turn.Id);
		return run;
	}
}
