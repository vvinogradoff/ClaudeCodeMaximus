using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;
// <remarks>Created by Claude</remarks>
public sealed class InstructionBlockBuilderTests
{
    private static InstructionOptionsModel Options(
        bool isAgentToolsEnabled, bool isNewSession, bool isStreamingMode, bool isTelegramEnabled = false) =>
        new(IsAutoCommit: null, IsNewBranch: false, IsAutoDocument: false,
            IsAgentToolsEnabled: isAgentToolsEnabled, IsNewSession: isNewSession, IsStreamingMode: isStreamingMode,
            IsTelegramEnabled: isTelegramEnabled);

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
}
