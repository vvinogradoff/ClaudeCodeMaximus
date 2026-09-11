namespace TessynDesktop.Services;

/// <summary>
/// Checks whether a Claude Code session is still available for --resume
/// by probing the .jsonl file in Claude's local session storage.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeSessionStatusService
{
	/// <summary>
	/// Returns true if the Claude Code session file {slug}/{claudeSessionId}.jsonl exists under
	/// any known Claude CLI home — the OS-default ~/.claude plus every profile directory under
	/// %AppData%/TessynDesktop/profiles/, active or removed via FR.12.18.
	/// </summary>
	bool IsSessionResumable(string workingDirectory, string claudeSessionId);

	/// <summary>
	/// Checks whether the session's JSONL already exists under the active profile's session
	/// store (see <paramref name="activeProfileConfigDir"/>); if not, searches every other known
	/// profile — the Default profile's <c>~/.claude</c> plus every directory under the profiles
	/// root, including profiles marked removed — for a matching <c>{slug}/{claudeSessionId}.jsonl</c>.
	/// When found elsewhere, the file is moved (not copied) into the active profile's store so
	/// <c>--resume</c> succeeds there natively going forward (FR.11.13).
	/// </summary>
	/// <returns>
	/// True if the session is now resumable under the active profile (already was, or was just
	/// adopted from another profile); false only when it cannot be found under any profile.
	/// </returns>
	bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir);
}
