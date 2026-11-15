using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Moves a Claude Code session into a profile's store (ISSUE.013 R3): the transcript <c>.jsonl</c> and the sibling
/// <c>&lt;id&gt;\</c> folder (tool results), merged when both exist. Move, not copy (as FR.11.13).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeSessionMigrator
{
	/// <summary>
	/// Moves the session from every other profile store into the one at <paramref name="targetConfigDir"/> (null = the Default profile).
	/// The target's own files win on a name conflict (the source copy is left in place). Never throws: a failure is logged and reported as not moved.
	/// </summary>
	/// <param name="skipWhenTargetExists">Return at once when the target already holds the transcript (the turn-time check): nothing else is scanned.</param>
	SessionMigrationReport Migrate(string workingDirectory, string claudeSessionId, string? targetConfigDir, bool skipWhenTargetExists = false);
}
