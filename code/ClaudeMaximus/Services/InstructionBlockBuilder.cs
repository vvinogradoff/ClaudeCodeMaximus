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
		var sb = new StringBuilder();
		sb.AppendLine(Constants.Instructions.Delimiter);

		// Auto-commit: always inject (ON or OFF) per FR.11.3
		sb.AppendLine(options.IsAutoCommit
			? $"- {Constants.Instructions.AutoCommitOn}"
			: $"- {Constants.Instructions.AutoCommitOff}");

		if (options.IsNewBranch)
			sb.AppendLine($"- {Constants.Instructions.NewBranch}");

		if (options.IsAutoDocument)
			sb.AppendLine($"- {Constants.Instructions.AutoDocument}");

		// FR.14.11 — Redirect from CLI-native scheduling to host MCP scheduling tools.
		// Also redirect away from the Workflow/Agent multi-agent tools toward CMX session orchestration.
		// Both emitted only when AgentToolsEnabled, because otherwise the MCP tools are not registered
		// and the redirects would point at nothing.
		if (options.IsAgentToolsEnabled)
		{
			sb.AppendLine($"- {Constants.Instructions.NativeSchedulingRedirect}");
			sb.AppendLine($"- {Constants.Instructions.NoWorkflowTool}");
		}

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
