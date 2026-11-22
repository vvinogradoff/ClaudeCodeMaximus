using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>FR.33.16: the per-engine wording of the instruction block. The Claude wording is unchanged (InstructionBlockBuilderTests).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexInstructionBlockTests
{
	private static string Build(bool agentTools = true, bool isNew = true, bool telegram = true, bool worker = false, string? engine = Constants.SessionEngines.Codex) =>
		InstructionBlockBuilder.Build(new InstructionOptionsModel(
			IsAutoCommit: true, IsNewBranch: false, IsAutoDocument: false, IsAgentToolsEnabled: agentTools, IsNewSession: isNew, IsStreamingMode: false,
			IsTelegramEnabled: telegram, IsWorkerSession: worker, EngineId: engine));

	[Fact]
	public void A_new_Codex_session_gets_the_shell_line_the_cmx_scheduling_and_the_sub_agent_line_instead_of_the_Claude_Code_tool_bans()
	{
		var block = Build();

		Assert.Contains("- Shell commands run in Windows PowerShell 5.1.", block);
		Assert.Contains("- Do not use Codex sub-agents or Codex's own multi-agent features; start sessions with the cmx tools so they appear in the user's session tree.", block);
		Assert.Contains("`mcp__cmx__schedule_wake`", block);
		Assert.Contains("`mcp__cmx__spawn_session`", block);
		Assert.Contains("`mcp__cmx__post_attention`", block);
		Assert.Contains(Constants.Instructions.AttentionGuidance, block);
		Assert.Contains(Constants.Instructions.FullUrlReferences, block);
		Assert.Contains(Constants.Instructions.AutoCommitOn, block);
		foreach (var claudeOnly in new[] { "ScheduleWakeup", "CronCreate", "CronList", "CronDelete", "`Workflow`", "`Agent` tool", "/deep-research", "Task-tool" })
			Assert.DoesNotContain(claudeOnly, block);
	}

	[Fact]
	public void The_session_start_lines_come_once_per_new_thread_and_the_cmx_lines_only_with_agent_tools()
	{
		var later   = Build(isNew: false);
		var noTools = Build(agentTools: false);

		Assert.DoesNotContain("PowerShell 5.1", later);
		Assert.DoesNotContain("Codex sub-agents", later);
		Assert.Contains("PowerShell 5.1", noTools);
		Assert.DoesNotContain("Codex sub-agents", noTools);
		Assert.DoesNotContain("mcp__cmx__", noTools);
	}

	[Fact]
	public void The_Claude_wording_is_unchanged_for_a_null_or_claude_engine()
	{
		var none   = Build(engine: null);
		var claude = Build(engine: Constants.SessionEngines.Claude);

		Assert.Equal(none, claude);
		Assert.Contains(Constants.Instructions.NativeSchedulingRedirect, none);
		Assert.Contains(Constants.Instructions.NoWorkflowTool, none);
		Assert.Contains(Constants.Instructions.TelegramNotifyGuidance, none);
		Assert.DoesNotContain("PowerShell 5.1", none);
	}
}
