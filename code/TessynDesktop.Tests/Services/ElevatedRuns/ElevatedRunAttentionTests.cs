using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>TC.ER.41 to TC.ER.47 and TC.ER.63: the guidance, the Focus item, its mirror, its resolution, the notification and the counts (FR.15.20, FR.23.31).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunAttentionTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private static InstructionOptionsModel Options(bool agentTools, bool newSession, bool worker = false, IReadOnlyList<AttentionItemModel>? pinned = null) =>
		new(IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false, IsAgentToolsEnabled: agentTools, IsNewSession: newSession, IsStreamingMode: false,
			IsWorkerSession: worker, PinnedAttentionItems: pinned);

	// ── TC.ER.41: guidance, skill and tool descriptions ───────────────────────

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Guidance_IsInTheBlockOnceForAFreshSession_WorkersIncluded_TC_ER_41(bool worker)
	{
		var fresh = InstructionBlockBuilder.Build(Options(agentTools: true, newSession: true, worker));
		var later = InstructionBlockBuilder.Build(Options(agentTools: true, newSession: false, worker));
		var off   = InstructionBlockBuilder.Build(Options(agentTools: false, newSession: true, worker));

		Assert.Contains(Constants.Instructions.ElevatedRunGuidance, fresh);
		Assert.DoesNotContain(Constants.Instructions.ElevatedRunGuidance, later);
		Assert.DoesNotContain(Constants.Instructions.ElevatedRunGuidance, off);
	}

	[Fact]
	public void Guidance_Text_IsTheSpecifiedWording_TC_ER_41()
	{
		Assert.Equal(
			"You cannot run commands as administrator yourself. When something really needs it (for example the WPF end-to-end tests), call `mcp__cmx__request_elevated_run` with the exact command line, a working directory (yours, a project directory or a sibling `-wt-` worktree), a one-line reason and, for a long run, `timeoutMinutes`. The user approves it in the desktop app; the call returns at once and the outcome arrives later as a new turn in this session, so end your turn instead of waiting or polling. Never ask the user to run something as administrator in plain text when this tool applies. Do not request the same run twice; `mcp__cmx__list_elevated_runs` shows your requests.",
			Constants.Instructions.ElevatedRunGuidance);
	}

	[Fact]
	public void Skill_NamesTheThreeTools_AndTheWorkerRule_TC_ER_41()
	{
		var skill = Constants.Agent.OrchestrationSkillMarkdown;

		Assert.Contains("## Elevated runs", skill);
		Assert.Contains("request_elevated_run", skill);
		Assert.Contains("cancel_elevated_run", skill);
		Assert.Contains("list_elevated_runs", skill);
		Assert.Contains("worker that needs an elevated run requests it itself", System.Text.RegularExpressions.Regex.Replace(skill, @"\s+", " "));
	}

	[Fact]
	public void ToolDescriptions_CarryTheSpecifiedWording_TC_ER_41()
	{
		var list = (JsonNode)typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
		var tools = list["tools"]!.AsArray().ToDictionary(t => t!["name"]!.GetValue<string>(), t => t!);

		var request = tools["request_elevated_run"]["description"]!.GetValue<string>();
		Assert.StartsWith("Ask the user to run ONE command line as administrator. Nothing runs until the user approves it in the Tessyn desktop app:", request);
		Assert.Contains("The call returns at once with a request id", request);
		Assert.Contains("arrives later as a new turn in this session - do not wait for it and do not poll.", request);
		Assert.EndsWith("Requests expire after 24 hours; withdraw one you no longer need with cancel_elevated_run.", request);
		Assert.Equal(["command", "reason", "timeoutMinutes", "workingDir"], tools["request_elevated_run"]["inputSchema"]!["properties"]!.AsObject().Select(p => p.Key).Order());
		Assert.Equal(["command", "workingDir", "reason"], tools["request_elevated_run"]["inputSchema"]!["required"]!.AsArray().Select(v => v!.GetValue<string>()));
		Assert.Contains("requestId", tools["cancel_elevated_run"]["inputSchema"]!["properties"]!.AsObject().Select(p => p.Key));
		Assert.Contains("status", tools["list_elevated_runs"]["inputSchema"]!["properties"]!.AsObject().Select(p => p.Key));
	}

	// ── TC.ER.42: the item ────────────────────────────────────────────────────

	[Fact]
	public async Task Item_IsAnAskWithTheFactsAndTheCommandInAFence_TC_ER_42()
	{
		var command = "powershell -NoProfile -File x.ps1 " + new string('a', 150);
		var run = await _env.RequestAsync(command: command, reason: "why");

		var item = Assert.Single(_env.Attention.GetItems(_env.Lead.NodeId));

		Assert.Equal(AttentionKind.Ask, item.Kind);
		Assert.Equal(AttentionSource.ElevatedRun, item.Source);
		Assert.Equal("elevated:" + run.Id, item.Key);
		Assert.Equal(run.Id, item.ElevatedRunId);
		Assert.Equal("Run as administrator: " + command[..120], item.Title);
		Assert.Empty(item.Options);
		Assert.Null(item.Recommendation);
		Assert.Equal(
			$"Reason: why\nWorking directory: {_env.Alpha}\nRequested by: Lead ({_env.Lead.NodeId[..8]})\nApprove this on the PC (Tessyn desktop app).\n\n```\n{command}\n```",
			item.Body);
	}

	[Theory]
	[InlineData("echo plain", "```")]
	[InlineData("echo ``` three", "````")]
	[InlineData("echo `` and ````", "`````")]
	public async Task Item_TheFenceIsOneLongerThanTheLongestBacktickRun_TC_ER_42(string command, string fence)
	{
		await _env.RequestAsync(command: command);

		var body = _env.Attention.GetItems(_env.Lead.NodeId).Single().Body;

		Assert.EndsWith($"\n\n{fence}\n{command}\n{fence}", body);
	}

	// ── TC.ER.43: the mirror ──────────────────────────────────────────────────

	[Fact]
	public async Task Mirror_AWorkersRequestIsPinnedInItsOwnAndTheSupervisorsLedger_TC_ER_43()
	{
		var run = await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);

		var own    = Assert.Single(_env.Attention.GetItems(_env.Wrk.NodeId));
		var mirror = Assert.Single(_env.Attention.GetItems(_env.Lead.NodeId));

		Assert.Equal(run.Id, own.ElevatedRunId);
		Assert.Equal(run.Id, mirror.ElevatedRunId);
		Assert.StartsWith("Run as administrator:", mirror.Title);
		Assert.Contains($"Requested by worker: Wrk ({_env.Wrk.NodeId[..8]})", mirror.Body);
		Assert.Contains("Requested by: Wrk", own.Body);
		Assert.Equal(1, own.Number);
		Assert.Equal(1, mirror.Number);
	}

	[Fact]
	public async Task Mirror_WithTheSupervisorGoneOnlyTheWorkersItemExists_TC_ER_43()
	{
		foreach (var dir in _env.Settings.Settings.Tree)
			dir.Sessions.Remove(_env.Lead);

		await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);

		Assert.Single(_env.Attention.GetItems(_env.Wrk.NodeId));
		Assert.Empty(_env.Attention.GetItems(_env.Lead.NodeId));
	}

	// ── TC.ER.44: resolution ──────────────────────────────────────────────────

	[Fact]
	public async Task Resolution_EveryFinalStatusClosesAllItemsAsResolved_NeverDismissed_TC_ER_44()
	{
		var run = await _env.RequestAsync(_env.Wrk, command: "secret-cmd-xyz", workingDir: _env.Wrk.WorkingDirectory, reason: "the reason");
		_env.MakeRunning(run.Id, 90);
		_env.Clock.Advance(TimeSpan.FromSeconds(75));
		_env.WriteResult(run.Id, exitCode: 6);
		_env.Service.Poll();

		var note = $"Elevated run {run.Id}: completed, exit code 6, 1m 15s";
		foreach (var node in new[] { _env.Wrk, _env.Lead })
		{
			var item = Assert.Single(_env.Attention.GetItems(node.NodeId));
			Assert.Equal(AttentionStatus.Resolved, item.Status);
			Assert.Equal(note, item.ResolutionNote);
			Assert.False(item.IsUserCloseUnreported);
			Assert.Empty(_env.Attention.TakeUnreportedUserClosures(node.NodeId));
		}
	}

	[Theory]
	[InlineData("declined")]
	[InlineData("cancelledByAgent")]
	[InlineData("expired")]
	public async Task Resolution_DeclineWithdrawAndExpiry_AreResolvedToo_TC_ER_44(string how)
	{
		var run = await _env.RequestAsync();
		switch (how)
		{
			case "declined": _env.Service.Decline(run.Id, null); break;
			case "cancelledByAgent": _env.Service.Cancel(_env.Lead, run.Id); break;
			default: _env.Clock.Advance(TimeSpan.FromHours(25)); _env.Service.Poll(); break;
		}

		var item = Assert.Single(_env.Attention.GetItems(_env.Lead.NodeId));
		Assert.Equal(AttentionStatus.Resolved, item.Status);
		Assert.StartsWith($"Elevated run {run.Id}: ", item.ResolutionNote);
		Assert.Empty(_env.Attention.TakeUnreportedUserClosures(_env.Lead.NodeId));
	}

	[Fact]
	public async Task Resolution_TheSystemEntriesCarryTheReasonButNeverTheCommand_TC_ER_44()
	{
		var run = await _env.RequestAsync(command: "secret-cmd-xyz", reason: "the reason");
		_env.Service.Decline(run.Id, null);

		var entries = _env.AttentionFiles.Appended.Where(a => a.fileName == _env.Lead.FileName).Select(a => a.content).ToList();

		Assert.Equal($"[Attention A1 (ask)] Elevated run {run.Id} requested: the reason", entries[0]);
		Assert.Equal($"[Attention A1 resolved] Elevated run {run.Id}: declined by the user", entries[1]);
		Assert.All(entries, e => Assert.DoesNotContain("secret-cmd-xyz", e));
	}

	// ── TC.ER.45: the session cannot close it ─────────────────────────────────

	[Fact]
	public async Task ResolveAttention_OnTheCard_IsRefusedAndChangesNothing_TC_ER_45()
	{
		await _env.RequestAsync();

		var result = await _env.Server.HandleToolCallAsync(new JsonObject
		{
			["name"] = "resolve_attention",
			["arguments"] = new JsonObject { ["id"] = "a1", ["status"] = "resolved", ["note"] = "done" },
		}, _env.Lead);

		Assert.Equal("A1 is an elevated-run request; withdraw it with cancel_elevated_run.", TextOf(result));
		Assert.Equal(AttentionStatus.Open, _env.Attention.GetItems(_env.Lead.NodeId).Single().Status);
	}

	// ── TC.ER.46, 63: the notification ────────────────────────────────────────

	[Fact]
	public async Task Notification_OneTextMessagePerRequest_WithTheFolderNameNotThePath_TC_ER_46()
	{
		var run = await _env.RequestAsync(reason: "run the E2E");

		var (node, message) = Assert.Single(_env.Channels.Sent);

		Assert.Equal(_env.Lead.NodeId, node.NodeId);
		Assert.Equal(
			$"Lead ({_env.Lead.NodeId[..8]}) asks to run a command as administrator: run the E2E\n\nCommand: echo hi\nWorking directory: Alpha\nApprove it on the PC.",
			message);
		Assert.DoesNotContain(_env.Root, message);
		Assert.DoesNotContain(run.Id, message);
	}

	[Fact]
	public async Task Notification_AWorkersRequestNotifiesOnceAndNamesTheWorker_TC_ER_46()
	{
		await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);

		var (node, message) = Assert.Single(_env.Channels.Sent);

		Assert.Equal(_env.Lead.NodeId, node.NodeId);
		Assert.StartsWith($"Wrk ({_env.Wrk.NodeId[..8]}) asks to run a command as administrator: because", message);
	}

	[Fact]
	public async Task Notification_ALongCommandIsCutAt300WithANote_TC_ER_46()
	{
		await _env.RequestAsync(command: "echo " + new string('q', 400));

		var message = _env.Channels.Sent.Single().Message;

		Assert.Contains("Command: echo " + new string('q', 295) + "... (cut, see the card)\n", message);
	}

	[Fact]
	public async Task Notification_OutcomesUpdatesAndClosesAreSilent_AndTheSoundPlaysOncePerRequest_TC_ER_46()
	{
		var created = 0;
		_env.Attention.ItemsCreated += (_, e) => created += e.Items.Count;

		var run = await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);
		_env.MakeRunning(run.Id, 80);
		_env.WriteResult(run.Id);
		_env.Service.Poll();

		Assert.Single(_env.Channels.Sent);
		Assert.Equal(1, created);
	}

	[Fact]
	public void Telegram_TheBridgeCarriesTextOnly_TC_ER_63()
	{
		var notify = typeof(ITelegramBridgeService).GetMethod(nameof(ITelegramBridgeService.NotifyAsync))!;

		Assert.Equal([typeof(string), typeof(string)], notify.GetParameters().Select(p => p.ParameterType));
	}

	[Fact]
	public async Task Telegram_TheTextHasNoLinkThatCouldApproveAnything_TC_ER_63()
	{
		await _env.RequestAsync(command: "echo hi");

		var message = _env.Channels.Sent.Single().Message;

		Assert.DoesNotContain("http", message, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("tessyn://", message, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith("Approve it on the PC.", message);
	}

	// ── TC.ER.47: it counts as an Ask ─────────────────────────────────────────

	[Fact]
	public async Task Counts_TheItemIsAnAsk_InTheBadgeTheOrderAndThePinnedList_TC_ER_47()
	{
		_env.Attention.Post(_env.Lead, [ElevatedTestItem("a todo", AttentionKind.Todo), ElevatedTestItem("a critical", AttentionKind.Critical)]);
		var run = await _env.RequestAsync(command: "echo hi");

		var summary = _env.Attention.GetSummary(_env.Lead.NodeId);
		var items   = _env.Attention.GetItems(_env.Lead.NodeId);
		var block   = InstructionBlockBuilder.Build(Options(agentTools: true, newSession: false, pinned: items));

		Assert.Equal(3, summary.OpenCount);
		Assert.Equal(AttentionKind.Critical, summary.TopKind);
		Assert.Equal(["a critical", "Run as administrator: echo hi", "a todo"], items.Select(i => i.Title));
		Assert.Contains("  - A3 ask: Run as administrator: echo hi", block);

		_env.Service.Decline(run.Id, null);
		Assert.Equal(2, _env.Attention.GetSummary(_env.Lead.NodeId).OpenCount);
	}

	[Fact]
	public async Task Counts_AnAskAloneIsGoldenrodTop_AndClosingItClearsTheBadge_TC_ER_47()
	{
		var run = await _env.RequestAsync();

		Assert.Equal(AttentionKind.Ask, _env.Attention.GetSummary(_env.Lead.NodeId).TopKind);
		Assert.Equal(1, _env.Attention.GetSummary(_env.Lead.NodeId).AllOpenCount);

		_env.Service.Cancel(_env.Lead, run.Id);

		Assert.Equal(0, _env.Attention.GetSummary(_env.Lead.NodeId).OpenCount);
		Assert.Null(_env.Attention.GetSummary(_env.Lead.NodeId).TopKind);
	}

	private static AttentionPostRequestModel ElevatedTestItem(string title, AttentionKind kind) =>
		new() { Kind = kind, Title = title };
}
