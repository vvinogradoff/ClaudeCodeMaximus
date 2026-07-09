namespace ClaudeMaximus.Models.Agent;

/// <summary>Arguments for the <c>send_to_session</c> MCP tool (FR.15.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SendToSessionArgs(
	/// <summary>NodeId of the target session to resume.</summary>
	string NodeId,

	/// <summary>Prompt to send.</summary>
	string Prompt,

	/// <summary>
	/// <c>wait</c> — block until the turn completes and return the result.
	/// <c>async</c> — return immediately; result is posted back to the caller's session via the mailbox.
	/// </summary>
	string Mode = "wait");
