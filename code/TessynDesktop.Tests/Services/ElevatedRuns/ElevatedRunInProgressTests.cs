using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>FR.23.41, FR.23.43: an approved elevated run is in progress, not an Ask: the badges, the session row, the pinned list, the mirrored copy and the closing.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunInProgressTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static InstructionOptionsModel Options(IReadOnlyList<AttentionItemModel> pinned) =>
		new(IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false, IsAgentToolsEnabled: true, IsNewSession: false, IsStreamingMode: false,
			IsWorkerSession: false, PinnedAttentionItems: pinned);

	private static AttentionPostRequestModel Plain(string title, AttentionKind kind) => new() { Kind = kind, Title = title };

	private AttentionItemModel RunItem(SessionNodeModel node, string runId) =>
		_env.Attention.GetItems(node.NodeId).Single(i => i.ElevatedRunId == runId);

	[Fact]
	public async Task Pending_StillCountsAsAnAsk()
	{
		var run = await _env.RequestAsync();

		var s = _env.Attention.GetSummary(_env.Lead.NodeId);

		Assert.Equal((1, AttentionKind.Ask, 1, AttentionKind.Ask), (s.OpenCount, s.TopKind!.Value, s.AllOpenCount, s.AllOpenTopKind!.Value));
		Assert.False(AttentionOrdering.IsInProgress(RunItem(_env.Lead, run.Id)));
	}

	[Fact]
	public async Task Launching_StaysOpenAndAnAsk_ButIsInProgress_TC_ER_FR_23_41()
	{
		var run = await _env.RequestAsync();
		_env.Service.Approve(run.Id);

		var item = RunItem(_env.Lead, run.Id);

		Assert.Equal(AttentionStatus.Open, item.Status);
		Assert.Equal(AttentionKind.Ask, item.Kind);
		Assert.True(AttentionOrdering.IsInProgress(item));
		Assert.False(AttentionOrdering.IsCounted(item));
	}

	[Fact]
	public async Task Badge_TheCountAndTheTopKindSkipTheInProgressItem()
	{
		_env.Attention.Post(_env.Lead, [Plain("an alert", AttentionKind.Alert)]);
		var run = await _env.RequestAsync();
		Assert.Equal((2, AttentionKind.Ask), (_env.Attention.GetSummary(_env.Lead.NodeId).OpenCount, _env.Attention.GetSummary(_env.Lead.NodeId).TopKind!.Value));

		_env.Service.Approve(run.Id);                    // launching
		var launching = _env.Attention.GetSummary(_env.Lead.NodeId);
		_env.Service.ReportLaunched(run.Id);
		_env.WriteStarted(run.Id);
		_env.Service.Poll();                             // running
		var running = _env.Attention.GetSummary(_env.Lead.NodeId);

		foreach (var s in new[] { launching, running })
			Assert.Equal((1, AttentionKind.Alert, 1, AttentionKind.Alert), (s.OpenCount, s.TopKind!.Value, s.AllOpenCount, s.AllOpenTopKind!.Value));
	}

	[Fact]
	public async Task SessionRow_ASessionWhoseOnlyOpenItemIsInProgressShowsNoBadge()
	{
		var run = await _env.RequestAsync();
		_env.MakeRunning(run.Id);

		var s = _env.Attention.GetSummary(_env.Lead.NodeId);

		Assert.Equal((0, 0), (s.OpenCount, s.AllOpenCount));
		Assert.Null(s.TopKind);
		Assert.Null(s.AllOpenTopKind);
	}

	[Fact]
	public async Task SessionRow_AllOpenCountSkipsItToo_ButStillCountsTheOtherDecidedItems()
	{
		_env.Attention.Post(_env.Lead, [Plain("decided", AttentionKind.Decided)]);
		var run = await _env.RequestAsync();
		_env.MakeRunning(run.Id);

		var s = _env.Attention.GetSummary(_env.Lead.NodeId);

		Assert.Equal((0, 1, AttentionKind.Decided), (s.OpenCount, s.AllOpenCount, s.AllOpenTopKind!.Value));
	}

	[Fact]
	public async Task PinnedList_TheLineSaysItWasApproved_AndItSortsAfterTheOtherOpenItems()
	{
		_env.Attention.Post(_env.Lead, [Plain("a todo", AttentionKind.Todo)]);
		var run = await _env.RequestAsync(command: "echo hi");
		_env.MakeRunning(run.Id);

		var items = _env.Attention.GetItems(_env.Lead.NodeId);
		var block = InstructionBlockBuilder.Build(Options(items));

		Assert.Equal(["a todo", "Run as administrator: echo hi"], items.Select(i => i.Title));
		Assert.Contains("  - A2 running as administrator (approved): Run as administrator: echo hi", block);
		Assert.DoesNotContain("A2 ask:", block);
		Assert.True(block.IndexOf("A1 todo: a todo", StringComparison.Ordinal) < block.IndexOf("A2 running as administrator", StringComparison.Ordinal));
	}

	[Fact]
	public async Task PinnedList_AClosedItemIsNeverInProgress_AndAPendingOneStaysAnAsk()
	{
		var pending = await _env.RequestAsync(command: "echo pending");

		var block = InstructionBlockBuilder.Build(Options(_env.Attention.GetItems(_env.Lead.NodeId)));

		Assert.Contains("  - A1 ask: Run as administrator: echo pending", block);
		Assert.DoesNotContain("running as administrator (approved)", block);

		_env.Service.Cancel(_env.Lead, pending.Id);
		Assert.False(AttentionOrdering.IsInProgress(RunItem(_env.Lead, pending.Id)));
	}

	[Fact]
	public async Task Mirror_TheSupervisorsCopyIsInProgressToo_BothItemsShareTheRun()
	{
		_env.Attention.Post(_env.Lead, [Plain("lead todo", AttentionKind.Todo)]);
		var run = await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);
		_env.MakeRunning(run.Id);

		var own    = RunItem(_env.Wrk, run.Id);
		var mirror = RunItem(_env.Lead, run.Id);
		var lead   = _env.Attention.GetSummary(_env.Lead.NodeId);
		var wrk    = _env.Attention.GetSummary(_env.Wrk.NodeId);
		var block  = InstructionBlockBuilder.Build(Options(_env.Attention.GetItems(_env.Lead.NodeId)));

		Assert.True(AttentionOrdering.IsInProgress(own));
		Assert.True(AttentionOrdering.IsInProgress(mirror));
		Assert.Equal((1, AttentionKind.Todo), (lead.OpenCount, lead.TopKind!.Value));
		Assert.Equal((0, 0), (wrk.OpenCount, wrk.AllOpenCount));
		Assert.Contains("running as administrator (approved): Run as administrator:", block);
	}

	[Fact]
	public async Task Notification_ApprovingFiresNothingNew()
	{
		var run = await _env.RequestAsync();
		var sent = _env.Channels.Sent.Count;

		_env.MakeRunning(run.Id);

		Assert.Equal(sent, _env.Channels.Sent.Count);
	}

	[Fact]
	public async Task Close_EveryFinalStatusResolvesTheItems_PendingThroughLaunchingAndRunning()
	{
		// pending -> launching -> running -> completed / timedOut / cancelled / failedToStart; launching -> declinedAtPrompt / failedToStart; pending -> declined / cancelled / expired
		var finals = new List<(string Id, SessionNodeModel Node)>();

		var done = await _env.RequestAsync(_env.Wrk, command: "done", workingDir: _env.Wrk.WorkingDirectory);
		_env.MakeRunning(done.Id, 11);
		Assert.True(AttentionOrdering.IsInProgress(RunItem(_env.Wrk, done.Id)));
		_env.WriteResult(done.Id);
		finals.Add((done.Id, _env.Wrk));

		var timed = await _env.RequestAsync(command: "timed");
		_env.MakeRunning(timed.Id, 12);
		_env.WriteResult(timed.Id, ended: "timedOut");
		finals.Add((timed.Id, _env.Lead));

		var stopped = await _env.RequestAsync(command: "stopped");
		_env.MakeRunning(stopped.Id, 13);
		_env.WriteResult(stopped.Id, ended: "stopped");
		finals.Add((stopped.Id, _env.Lead));

		var gone = await _env.RequestAsync(command: "gone");
		_env.MakeRunning(gone.Id, 14);
		_env.AlivePids.Remove(14);
		finals.Add((gone.Id, _env.Lead));

		_env.Service.Poll();

		var prompt = await _env.RequestAsync(command: "prompt");
		_env.Service.Approve(prompt.Id);
		_env.Service.ReportLaunchFailed(prompt.Id, declined: true, null);
		finals.Add((prompt.Id, _env.Lead));

		var broken = await _env.RequestAsync(command: "broken");
		_env.Service.Approve(broken.Id);
		_env.Service.ReportLaunchFailed(broken.Id, declined: false, "boom");
		finals.Add((broken.Id, _env.Lead));

		var declined = await _env.RequestAsync(command: "declined");
		_env.Service.Decline(declined.Id, null);
		finals.Add((declined.Id, _env.Lead));

		foreach (var (id, node) in finals)
		{
			var item = RunItem(node, id);
			Assert.Equal(AttentionStatus.Resolved, item.Status);
			Assert.False(string.IsNullOrEmpty(item.ResolutionNote));
			Assert.False(AttentionOrdering.IsInProgress(item));
		}

		// The mirror of the worker's run closed with it.
		Assert.Equal(AttentionStatus.Resolved, RunItem(_env.Lead, done.Id).Status);
		Assert.Equal(0, _env.Attention.GetSummary(_env.Lead.NodeId).AllOpenCount);
	}

	[Fact]
	public async Task Close_TheItemMovesToClosedWhileTheRunIsFinal_NotWhileItRuns()
	{
		var run = await _env.RequestAsync();
		_env.MakeRunning(run.Id);

		Assert.Equal(AttentionStatus.Open, RunItem(_env.Lead, run.Id).Status);

		_env.WriteResult(run.Id);
		_env.Service.Poll();

		Assert.Equal(AttentionStatus.Resolved, RunItem(_env.Lead, run.Id).Status);
	}
}
