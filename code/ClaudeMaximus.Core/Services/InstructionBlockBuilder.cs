using System.Collections.Generic;
using System.Text;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

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
		if (options.IsAgentToolsEnabled && options.IsNewSession)
		{
			lines.AppendLine($"- {Constants.Instructions.NativeSchedulingRedirect}");
			lines.AppendLine($"- {Constants.Instructions.NoWorkflowTool}");
		}

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
