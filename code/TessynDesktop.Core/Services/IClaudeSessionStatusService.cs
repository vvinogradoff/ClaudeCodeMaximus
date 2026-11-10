using System.Collections.Generic;
using TessynDesktop.Models;

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
	/// <param name="projectsDirs">
	/// Optional pre-resolved list from <see cref="GetAllProjectsDirs"/>, so a caller checking many
	/// sessions lists the profile directories once instead of once per check (FR.15.13).
	/// </param>
	bool IsSessionResumable(string workingDirectory, string claudeSessionId, IReadOnlyList<string>? projectsDirs = null);

	/// <summary>Resolves every Claude CLI <c>projects</c> directory once, for reuse across many <see cref="IsSessionResumable"/> calls (FR.15.13).</summary>
	IReadOnlyList<string> GetAllProjectsDirs();

	/// <summary>
	/// Batch form of <see cref="IsSessionResumable"/> with identical per-session results, but the file
	/// system is listed once per project directory instead of probed once per session and directory.
	/// Safe to call from a background thread.
	/// </summary>
	/// <returns>One flag per request, in request order.</returns>
	IReadOnlyList<bool> AreSessionsResumable(IReadOnlyList<SessionResumabilityRequest> requests, IReadOnlyList<string>? projectsDirs = null);

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
