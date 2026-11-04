using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;
// <remarks>Created by Claude</remarks>
public sealed class InstructionBlockBuilderTests
{
    private static InstructionOptionsModel Options(
        bool isAgentToolsEnabled, bool isNewSession, bool isStreamingMode, bool isTelegramEnabled = false,
        bool isWorkerSession = false, bool isScheduledTurn = false) =>
        new(IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false,
            IsAgentToolsEnabled: isAgentToolsEnabled, IsNewSession: isNewSession, IsStreamingMode: isStreamingMode,
            IsTelegramEnabled: isTelegramEnabled, IsWorkerSession: isWorkerSession, IsScheduledTurn: isScheduledTurn);

    [Fact]
    public void Build_OneShotNewSessionAgentToolsEnabled_IncludesBothRedirects()
    {
        var block = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false));

        Assert.Contains(Constants.Instructions.NativeSchedulingRedirect, block);
        Assert.Contains(Constants.Instructions.NoWorkflowTool, block);
    }

    [Fact]
    public void Build_StreamingModeNewSessionAgentToolsEnabled_OmitsSchedulingRedirectButKeepsWorkflowRedirect()
    {
        // FR.19.9: the native-scheduling redirect's rationale (process dies between turns) doesn't
        // hold for a persistent streaming process, so it must be omitted — but NoWorkflowTool is
        // unconditional on mode since it addresses session-tree visibility, not process lifetime.
        var block = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: true));

        Assert.DoesNotContain(Constants.Instructions.NativeSchedulingRedirect, block);
        Assert.Contains(Constants.Instructions.NoWorkflowTool, block);
    }

    [Fact]
    public void Build_AgentToolsDisabled_OmitsBothRedirectsRegardlessOfMode()
    {
        var oneShot = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: false, isNewSession: true, isStreamingMode: false));
        var streaming = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: false, isNewSession: true, isStreamingMode: true));

        Assert.DoesNotContain(Constants.Instructions.NativeSchedulingRedirect, oneShot);
        Assert.DoesNotContain(Constants.Instructions.NoWorkflowTool, oneShot);
        Assert.DoesNotContain(Constants.Instructions.NativeSchedulingRedirect, streaming);
        Assert.DoesNotContain(Constants.Instructions.NoWorkflowTool, streaming);
    }

    [Fact]
    public void Build_NotNewSession_OmitsBothRedirectsRegardlessOfMode()
    {
        // Both redirects are session-start policy, not per-turn — resumed turns (IsNewSession=false)
        // never re-inject them, in either mode.
        var oneShot = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: true, isNewSession: false, isStreamingMode: false));
        var streaming = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: true, isNewSession: false, isStreamingMode: true));

        Assert.DoesNotContain(Constants.Instructions.NativeSchedulingRedirect, oneShot);
        Assert.DoesNotContain(Constants.Instructions.NoWorkflowTool, oneShot);
        Assert.DoesNotContain(Constants.Instructions.NativeSchedulingRedirect, streaming);
        Assert.DoesNotContain(Constants.Instructions.NoWorkflowTool, streaming);
    }

    [Fact]
    public void Build_OneShotNewSessionAgentToolsEnabled_ReturnsNonEmptyBlock()
    {
        var block = InstructionBlockBuilder.Build(Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false));

        Assert.NotEqual(string.Empty, block);
        Assert.Contains("Session preferences", block);
    }

    // ── FR.21.3 — TelegramNotifyGuidance gating ────────────────────────────────

    [Fact]
    public void Build_AgentToolsAndTelegramEnabledOnNewSession_IncludesTelegramNotifyGuidance()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false, isTelegramEnabled: true));

        Assert.Contains(Constants.Instructions.TelegramNotifyGuidance, block);
    }

    [Fact]
    public void Build_TelegramDisabled_OmitsTelegramNotifyGuidance()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false, isTelegramEnabled: false));

        Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, block);
    }

    [Fact]
    public void Build_NotANewSession_OmitsTelegramNotifyGuidanceEvenIfEnabled()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: false, isStreamingMode: false, isTelegramEnabled: true));

        Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, block);
    }

    [Fact]
    public void Build_AgentToolsDisabled_OmitsTelegramNotifyGuidanceEvenIfTelegramEnabled()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: false, isNewSession: true, isStreamingMode: false, isTelegramEnabled: true));

        Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, block);
    }

    [Fact]
    public void Build_WorkerSession_OmitsTelegramNotifyGuidanceEvenIfOtherwiseEligible()
    {
        // FR.21.20 — a Worker Session's own notify_user call is dropped outright by the bridge
        // (FR.21.19), so telling it the tool exists would only invite a wasted call.
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false,
                isTelegramEnabled: true, isWorkerSession: true));

        Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, block);
    }

    [Fact]
    public void Build_ScheduledTurnWithAgentToolsAndTelegram_IncludesScheduledTurnNotifyReminder()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false,
                isTelegramEnabled: true, isWorkerSession: false, isScheduledTurn: true));

        Assert.Contains(Constants.Instructions.ScheduledTurnNotifyReminder, block);
    }

    [Fact]
    public void Build_ScheduledTurn_IncludesReminderEvenWhenNotANewSession()
    {
        // FR.21.24 - describes this turn, not session policy: no once-per-fresh-session gate.
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: false, isStreamingMode: false,
                isTelegramEnabled: true, isWorkerSession: false, isScheduledTurn: true));

        Assert.Contains(Constants.Instructions.ScheduledTurnNotifyReminder, block);
        Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, block);
    }

    [Fact]
    public void Build_ScheduledTurn_WorkerSession_OmitsReminder()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false,
                isTelegramEnabled: true, isWorkerSession: true, isScheduledTurn: true));

        Assert.DoesNotContain(Constants.Instructions.ScheduledTurnNotifyReminder, block);
    }

    [Fact]
    public void Build_ScheduledTurn_TelegramDisabled_OmitsReminder()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false,
                isTelegramEnabled: false, isWorkerSession: false, isScheduledTurn: true));

        Assert.DoesNotContain(Constants.Instructions.ScheduledTurnNotifyReminder, block);
    }

    [Fact]
    public void Build_ScheduledTurn_AgentToolsDisabled_OmitsReminder()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: false, isNewSession: true, isStreamingMode: false,
                isTelegramEnabled: true, isWorkerSession: false, isScheduledTurn: true));

        Assert.DoesNotContain(Constants.Instructions.ScheduledTurnNotifyReminder, block);
    }

    [Fact]
    public void Build_NotAScheduledTurn_OmitsReminder()
    {
        var block = InstructionBlockBuilder.Build(
            Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false,
                isTelegramEnabled: true, isWorkerSession: false, isScheduledTurn: false));

        Assert.DoesNotContain(Constants.Instructions.ScheduledTurnNotifyReminder, block);
    }

    // ── FR.23 — Attention guidance, Pinned list, amended texts (TC.FV.14–TC.FV.16) ──────────

    private static AttentionItemModel Pinned(
        int number, AttentionKind kind, string? title = null, AttentionStatus status = AttentionStatus.Open) => new()
    {
        Id = $"A{number}", Number = number, Kind = kind, Title = title ?? $"Title {number}", Status = status,
    };

    private static InstructionOptionsModel AttentionOptions(
        IReadOnlyList<AttentionItemModel>? pinned = null, IReadOnlyList<AttentionItemModel>? closed = null,
        bool isAgentToolsEnabled = true, bool isNewSession = false, bool isWorkerSession = false) =>
        new(IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false,
            IsAgentToolsEnabled: isAgentToolsEnabled, IsNewSession: isNewSession, IsStreamingMode: false,
            IsWorkerSession: isWorkerSession, PinnedAttentionItems: pinned, UserClosedAttentionItems: closed);

    private static string[] Lines(string block) =>
        block.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Build_AttentionGuidance_TextMatchesFr2310()   // TC.FV.14-1
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(isNewSession: true));

        Assert.Contains(Constants.Instructions.AttentionGuidance, block);
        Assert.StartsWith("The user has a Focus view beside this session's output. Pin anything they need there with `mcp__cmx__post_attention`: `critical`", Constants.Instructions.AttentionGuidance);
        Assert.EndsWith("call `mcp__cmx__resolve_attention`.", Constants.Instructions.AttentionGuidance);
        Assert.Contains("refer to them by id. When the user answers an item in conversation", Constants.Instructions.AttentionGuidance);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Build_AttentionGuidance_AbsentWhenAnyGateFails(bool agentTools, bool newSession, bool worker)   // TC.FV.14-2
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(
            isAgentToolsEnabled: agentTools, isNewSession: newSession, isWorkerSession: worker));

        Assert.DoesNotContain(Constants.Instructions.AttentionGuidance, block);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void Build_AttentionGuidance_FollowsTheNoWorkflowToolGate(bool agentTools, bool newSession, bool worker, bool expectGuidance)   // TC.FV.14-3
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(
            isAgentToolsEnabled: agentTools, isNewSession: newSession, isWorkerSession: worker));

        Assert.Equal(expectGuidance, block.Contains(Constants.Instructions.AttentionGuidance));
        // NoWorkflowTool has no worker gate; for non-workers the two travel together.
        if (!worker)
            Assert.Equal(block.Contains(Constants.Instructions.NoWorkflowTool), block.Contains(Constants.Instructions.AttentionGuidance));
    }

    [Fact]
    public void Build_PinnedItems_EmitsExactHeaderAndItemLinesAtTheEnd()   // TC.FV.15-1
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(
            pinned: [Pinned(12, AttentionKind.Critical, "Prod down"), Pinned(3, AttentionKind.Ask, "Which plan?")]));

        Assert.EndsWith(
            "- Pinned in the user's Focus view - they can see these, so don't restate them; refer to them by id, and call `mcp__cmx__resolve_attention` when one is answered in conversation or no longer applies:\n" +
            "  - A12 critical: Prod down\n" +
            "  - A3 ask: Which plan?\n",
            block.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Build_PinnedItems_AreAppendedAfterTheExistingLines()   // TC.FV.15-1
    {
        var options = new InstructionOptionsModel(
            IsAutoCommit: true, IsNewBranch: false, IsAutoDocument: false,
            IsAgentToolsEnabled: true, IsNewSession: false, IsStreamingMode: false,
            IsTelegramEnabled: true, IsScheduledTurn: true,
            PinnedAttentionItems: [Pinned(1, AttentionKind.Ask)]);

        var lines = Lines(InstructionBlockBuilder.Build(options));

        var reminder = Array.FindIndex(lines, l => l.Contains(Constants.Instructions.ScheduledTurnNotifyReminder));
        var pinned   = Array.FindIndex(lines, l => l.StartsWith("- Pinned in the user's Focus view"));
        Assert.True(reminder >= 0 && pinned > reminder);
    }

    [Fact]
    public void Build_PinnedItems_AreOrderedBySeverityThenNumber_DecidedIncluded()   // TC.FV.15-2
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(pinned:
        [
            Pinned(6, AttentionKind.Decided), Pinned(1, AttentionKind.Alert), Pinned(5, AttentionKind.Ask),
            Pinned(4, AttentionKind.Todo), Pinned(3, AttentionKind.Ask), Pinned(2, AttentionKind.Critical),
        ]));

        var items = Lines(block).Where(l => l.StartsWith("  - ")).Select(l => l.Split(' ')[3]).ToArray();
        Assert.Equal(["A2", "A3", "A5", "A4", "A1", "A6"], items);
    }

    [Fact]
    public void Build_PinnedItems_CapAtTwentyLinesWithAMoreLine()   // TC.FV.15-3
    {
        var items = Enumerable.Range(1, 25).Select(n => Pinned(n, AttentionKind.Ask)).ToArray();

        var lines = Lines(InstructionBlockBuilder.Build(AttentionOptions(pinned: items)));

        Assert.Equal(20, lines.Count(l => l.StartsWith("  - A")));
        Assert.Equal("  - ...and 5 more (call mcp__cmx__list_attention)", lines[^1]);
    }

    [Fact]
    public void Build_ExactlyTwentyPinnedItems_HaveNoMoreLine()   // TC.FV.15-3
    {
        var items = Enumerable.Range(1, 20).Select(n => Pinned(n, AttentionKind.Ask)).ToArray();

        var block = InstructionBlockBuilder.Build(AttentionOptions(pinned: items));

        Assert.DoesNotContain("more (call", block);
    }

    [Fact]
    public void Build_PinnedTitle_IsCutTo120PlusEllipsis()   // TC.FV.15-4
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(pinned:
        [
            Pinned(1, AttentionKind.Ask, new string('a', 130)),
            Pinned(2, AttentionKind.Ask, new string('b', 120)),
        ]));

        Assert.Contains("  - A1 ask: " + new string('a', 120) + "...\n", block.ReplaceLineEndings("\n"));
        Assert.Contains("  - A2 ask: " + new string('b', 120) + "\n", block.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Build_UserClosures_EmitsTheClosuresLine()   // TC.FV.15-5
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(closed:
        [
            Pinned(5, AttentionKind.Todo, "t5", AttentionStatus.Resolved),
            Pinned(4, AttentionKind.Ask, "t4", AttentionStatus.Dismissed),
        ]));

        Assert.Contains(
            "- Closed by the user in the Focus view since your last turn: A5 todo \"t5\" marked done; A4 ask \"t4\" dismissed without an answer.",
            block);
    }

    [Theory]
    [InlineData(AttentionKind.Critical)]
    [InlineData(AttentionKind.Alert)]
    [InlineData(AttentionKind.Decided)]
    public void Build_UserClosedNonTodo_ReadsDismissedWithoutAnAnswer(AttentionKind kind)   // TC.FV.15-5
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(closed: [Pinned(7, kind, "x", AttentionStatus.Dismissed)]));

        Assert.Contains($"A7 {kind.ToString().ToLowerInvariant()} \"x\" dismissed without an answer.", block);
    }

    [Fact]
    public void Build_OpenItemsOnly_EmitsOnlyThePinnedLine()   // TC.FV.15-6
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(pinned: [Pinned(1, AttentionKind.Ask)]));

        Assert.Contains("- Pinned in the user's Focus view", block);
        Assert.DoesNotContain("Closed by the user", block);
    }

    [Fact]
    public void Build_ClosuresOnly_EmitsNoPinnedLine()   // TC.FV.15-6
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(closed: [Pinned(1, AttentionKind.Ask, "t", AttentionStatus.Dismissed)]));

        Assert.Contains("Closed by the user", block);
        Assert.DoesNotContain("- Pinned in the user's Focus view", block);
    }

    [Fact]
    public void Build_NeitherPinnedNorClosures_LeavesTheBlockUnchanged()   // TC.FV.15-6
    {
        var plain = Options(isAgentToolsEnabled: true, isNewSession: false, isStreamingMode: false);

        Assert.Equal(
            InstructionBlockBuilder.Build(plain),
            InstructionBlockBuilder.Build(plain with { PinnedAttentionItems = [], UserClosedAttentionItems = [] }));
        Assert.Equal(string.Empty, InstructionBlockBuilder.Build(plain with { PinnedAttentionItems = null }));
    }

    [Fact]
    public void Build_ClosedItemsInThePinnedList_AreNotListed()   // TC.FV.15-6
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(pinned: [Pinned(1, AttentionKind.Ask, "gone", AttentionStatus.Answered)]));

        Assert.Equal(string.Empty, block);
    }

    [Theory]
    [InlineData(false, false)]   // agent tools disabled
    [InlineData(true, true)]     // worker session
    public void Build_PinnedAndClosures_RespectTheGate(bool agentToolsEnabled, bool isWorker)   // TC.FV.15-7
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(
            pinned: [Pinned(1, AttentionKind.Ask)],
            closed: [Pinned(2, AttentionKind.Ask, "t", AttentionStatus.Dismissed)],
            isAgentToolsEnabled: agentToolsEnabled,
            isWorkerSession: isWorker));

        Assert.DoesNotContain("Pinned in the user's Focus view", block);
        Assert.DoesNotContain("Closed by the user", block);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_PinnedList_IsNotGatedOnNewSession(bool isNewSession)   // TC.FV.15-8
    {
        var block = InstructionBlockBuilder.Build(AttentionOptions(pinned: [Pinned(1, AttentionKind.Ask)], isNewSession: isNewSession));

        Assert.Contains("- Pinned in the user's Focus view", block);
    }

    [Fact]
    public void InstructionOptionsModel_AttentionListsAreOptional()   // TC.FV.15-9
    {
        var options = Options(isAgentToolsEnabled: true, isNewSession: true, isStreamingMode: false);

        Assert.Null(options.PinnedAttentionItems);
        Assert.Null(options.UserClosedAttentionItems);
        Assert.DoesNotContain("Pinned in the user's Focus view", InstructionBlockBuilder.Build(options));
    }

    [Fact]
    public void TelegramNotifyGuidance_HasTheAmendedTextAndUnchangedGate()   // TC.FV.16-1
    {
        var text = Constants.Instructions.TelegramNotifyGuidance;

        Assert.StartsWith("A Telegram bridge is available: `critical` items, and items you post with `notify: true`, also ping the user's Telegram and desktop.", text);
        Assert.Contains("critical", text);
        Assert.Contains("notify: true", text);
        Assert.Contains("post_attention", text);
        Assert.Contains("notify_user", text);
        Assert.Contains("There is no blocking \"ask and wait\" tool", text);
        Assert.EndsWith("The user's reply arrives later as an ordinary new prompt in this same session.", text);

        var on = InstructionBlockBuilder.Build(Options(true, true, false, isTelegramEnabled: true));
        var off = InstructionBlockBuilder.Build(Options(true, true, false, isTelegramEnabled: false));
        Assert.Contains(text, on);
        Assert.DoesNotContain(text, off);
    }

    [Fact]
    public void ScheduledTurnNotifyReminder_HasTheAmendedTextAndUnchangedGate()   // TC.FV.16-2
    {
        var text = Constants.Instructions.ScheduledTurnNotifyReminder;

        Assert.Equal(
            "The scheduler started this turn, so the user may not be watching and is not told when it finishes. If — and only if — your reply needs the user to decide or act and it isn't pinned yet, post it with `mcp__cmx__post_attention` (`notify: true` if they must act soon). Don't restate items that are already pinned.",
            text);
        Assert.Contains("mcp__cmx__post_attention", text);
        Assert.Contains("Don't restate items that are already pinned.", text);

        var scheduled = InstructionBlockBuilder.Build(Options(true, false, false, isTelegramEnabled: true, isScheduledTurn: true));
        var notScheduled = InstructionBlockBuilder.Build(Options(true, false, false, isTelegramEnabled: true, isScheduledTurn: false));
        Assert.Contains(text, scheduled);
        Assert.DoesNotContain(text, notScheduled);
    }

    [Fact]
    public void Build_WorkerSession_GetsNoGuidanceNoTelegramNoReminderNoPinnedNoClosures()   // TC.FV.16-3
    {
        var options = new InstructionOptionsModel(
            IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false,
            IsAgentToolsEnabled: true, IsNewSession: true, IsStreamingMode: false,
            IsTelegramEnabled: true, IsWorkerSession: true, IsScheduledTurn: true,
            PinnedAttentionItems: [Pinned(1, AttentionKind.Ask)],
            UserClosedAttentionItems: [Pinned(2, AttentionKind.Ask, "t", AttentionStatus.Dismissed)]);

        var block = InstructionBlockBuilder.Build(options);

        Assert.DoesNotContain(Constants.Instructions.AttentionGuidance, block);
        Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, block);
        Assert.DoesNotContain(Constants.Instructions.ScheduledTurnNotifyReminder, block);
        Assert.DoesNotContain("Pinned in the user's Focus view", block);
        Assert.DoesNotContain("Closed by the user", block);
    }

    [Fact]
    public void OrchestrationSkill_SupervisorPostsTheAskOnce_WorkersCallNeitherTool()   // TC.FV.16-4
    {
        var skill      = Constants.Agent.OrchestrationSkillMarkdown;
        var supervisor = skill[..skill.IndexOf("## As a worker", StringComparison.Ordinal)];
        var worker     = skill[skill.IndexOf("## As a worker", StringComparison.Ordinal)..];

        Assert.Contains("Post the ask once with `post_attention` (`notify: true` when the user must act soon)", supervisor);
        Assert.Contains("don't restate it in later delivery", supervisor);
        Assert.DoesNotContain("Call `notify_user` once", supervisor);
        Assert.Contains("Do not call `notify_user` or `post_attention`", worker);
    }
}
