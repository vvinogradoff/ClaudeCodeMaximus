using System.Collections.Generic;
using System.Linq;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the hidden instruction block appended to every claude stdin message (FR.11.2, FR.11.10).
/// Extracted from <c>SessionViewModel.BuildInstructionBlock</c> so the headless
/// <c>SessionTurnService</c> can reuse the same logic without duplicating it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class InstructionBlockBuilder
{
	/// <summary>
	/// Builds the hidden instruction block text from the given options.
	/// The returned string begins with a blank line + delimiter and ends with a newline.
	/// </summary>
	public static string Build(InstructionOptionsModel options)
	{
		var lines = new StringBuilder();

		// Auto-commit is tri-state (FR.11.3): true=Commit, false=Do-not-commit, null=No preference
		// (silent — no commit-related line at all).
		if (options.IsAutoCommit == true)
			lines.AppendLine($"- {Constants.Instructions.AutoCommitOn}");
		else if (options.IsAutoCommit == false)
			lines.AppendLine($"- {Constants.Instructions.AutoCommitOff}");

		if (options.IsNewBranch)
			lines.AppendLine($"- {Constants.Instructions.NewBranch}");

		if (options.IsAutoDocument)
			lines.AppendLine($"- {Constants.Instructions.AutoDocument}");

		// FR.14.11 — Redirect from CLI-native scheduling to host MCP scheduling tools.
		// Also redirect away from the Workflow/Agent/deep-research tools toward CMX session orchestration.
		// Emitted only when AgentToolsEnabled (otherwise the MCP tools aren't registered and the
		// redirects would point at nothing) AND only at the start of a fresh claude process
		// (IsNewSession) — these are session-level policy, not per-turn state, and repeating them
		// on every turn reads as a prompt injection to the model and is sometimes ignored outright.
		//
		// FR.19.9 — The native-scheduling redirect is additionally gated on !IsStreamingMode: its
		// rationale (the process dies between turns) doesn't hold for a persistent streaming process.
		// NoWorkflowTool is unconditional on mode — the concern it addresses (subagents invisible to
		// the session tree) applies regardless of process lifetime.
		if (options.IsAgentToolsEnabled && options.IsNewSession && !options.IsStreamingMode)
			lines.AppendLine($"- {Constants.Instructions.NativeSchedulingRedirect}");
		if (options.IsAgentToolsEnabled && options.IsNewSession)
			lines.AppendLine($"- {Constants.Instructions.NoWorkflowTool}");

		// FR.23.10 — same gate as NoWorkflowTool, plus never for a Worker Session (its post_attention
		// call is refused, FR.23.6): session policy on how to pin items in the user's Focus view.
		if (options.IsAgentToolsEnabled && options.IsNewSession && !options.IsWorkerSession)
			lines.AppendLine($"- {Constants.Instructions.AttentionGuidance}");

		// FR.21.3 — same once-per-fresh-session gating as NoWorkflowTool: this is session-level
		// policy (a tool exists), not per-turn state. FR.21.20 — never emitted for a Worker Session:
		// its own notify_user call is dropped outright by the bridge (FR.21.19), so the guidance
		// would only invite a wasted call.
		if (options.IsAgentToolsEnabled && options.IsTelegramEnabled && options.IsNewSession && !options.IsWorkerSession)
			lines.AppendLine($"- {Constants.Instructions.TelegramNotifyGuidance}");

		// FR.21.24 — describes this turn, not session policy, so no IsNewSession gate.
		if (options.IsAgentToolsEnabled && options.IsTelegramEnabled && options.IsScheduledTurn && !options.IsWorkerSession)
			lines.AppendLine($"- {Constants.Instructions.ScheduledTurnNotifyReminder}");

		// FR.23.11 — describes this turn's state (pinned items, closures since the last turn), so no
		// IsNewSession gate; appended last, after the existing lines.
		if (options.IsAgentToolsEnabled && !options.IsWorkerSession)
			AppendAttentionLines(lines, options);

		// FR.11.2 — omit the block entirely when there is nothing to say (Auto-Commit silent and
		// no other toggle active), instead of appending a dangling preamble with no instructions.
		if (lines.Length == 0)
			return string.Empty;

		var sb = new StringBuilder();
		sb.AppendLine(Constants.Instructions.Delimiter);
		sb.Append(lines);
		return sb.ToString();
	}

	/// <summary>
	/// FR.23.11 — the Pinned list (open items, severity then number, capped) and the "closed by the user"
	/// line. Each is emitted only when it has content.
	/// </summary>
	private static void AppendAttentionLines(StringBuilder lines, InstructionOptionsModel options)
	{
		var open = AttentionOrdering.Sort(
			(options.PinnedAttentionItems ?? []).Where(i => i.Status == AttentionStatus.Open)).ToList();

		if (open.Count > 0)
		{
			lines.AppendLine($"- {Constants.Attention.PinnedHeaderLine}");
			foreach (var item in open.Take(Constants.Attention.MaxPinnedLines))
				lines.AppendLine(string.Format(
					Constants.Attention.PinnedItemLineFormat, item.Id, AttentionFormat.KindName(item.Kind), CutTitle(item.Title)));

			if (open.Count > Constants.Attention.MaxPinnedLines)
				lines.AppendLine(string.Format(Constants.Attention.PinnedMoreLineFormat, open.Count - Constants.Attention.MaxPinnedLines));
		}

		var closed = options.UserClosedAttentionItems ?? [];
		if (closed.Count > 0)
		{
			var parts = closed.Select(i => string.Format(
				Constants.Attention.ClosureFormat, i.Id, AttentionFormat.KindName(i.Kind), CutTitle(i.Title),
				i.Kind == AttentionKind.Todo ? Constants.Attention.ClosureMarkedDone : Constants.Attention.ClosureDismissed));
			lines.AppendLine($"- {Constants.Attention.ClosuresLinePrefix}{string.Join(Constants.Attention.ClosureSeparator, parts)}.");
		}
	}

	private static string CutTitle(string title) =>
		title.Length > Constants.Attention.MaxPinnedTitleLength
			? title[..Constants.Attention.MaxPinnedTitleLength] + Constants.Attention.TitleEllipsis
			: title;

	/// <summary>
	/// Builds context preamble: wraps the current message with prior conversation history
	/// so Claude can continue a detached session (FR.11.10, FR.11.11).
	/// </summary>
	public static string BuildContextPreamble(
		IReadOnlyList<SessionEntryModel> allEntries,
		string currentMessage)
	{
		var conversationEntries = new List<SessionEntryModel>();
		foreach (var e in allEntries)
		{
			if (e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
				conversationEntries.Add(e);
		}

		if (conversationEntries.Count == 0)
			return currentMessage;

		var sb = new StringBuilder();
		sb.AppendLine("The following is the conversation history from a previous session that is no longer available. Use it as context for continuity:");
		sb.AppendLine("---");

		foreach (var entry in conversationEntries)
		{
			var roleLabel = entry.Role == Constants.SessionFile.RoleUser ? "Human" : "Assistant";
			sb.AppendLine($"[{roleLabel}]: {entry.Content}");
			sb.AppendLine();
		}

		sb.AppendLine("---");
		sb.AppendLine("Now, continuing the conversation:");
		sb.AppendLine(currentMessage);

		return sb.ToString();
	}
}
