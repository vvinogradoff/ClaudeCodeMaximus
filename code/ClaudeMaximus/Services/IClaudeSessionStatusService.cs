namespace ClaudeMaximus.Services;

/// <summary>
/// Checks whether a Claude Code session is still available for --resume
/// by probing the .jsonl file in Claude's local session storage.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeSessionStatusService
{
	/// <summary>
	/// Returns true if the Claude Code session file exists at
	/// ~/.claude/projects/{slug}/{claudeSessionId}.jsonl.
	/// </summary>
	bool IsSessionResumable(string workingDirectory, string claudeSessionId);
}
