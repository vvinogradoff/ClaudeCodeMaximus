using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Codex;

/// <summary>
/// Picks the Codex profile a Codex session's turn runs on (FR.33.5). Phase 2b has only <see cref="CodexDefaultProfileSelector"/>; the stored
/// profiles, pins and the inherit rule come with phase 3, which replaces it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexProfileSelector
{
	/// <summary>The profile of the session's turn. <paramref name="settings"/> is null outside a turn (compaction, renaming the thread).</summary>
	CodexProfileRef Select(SessionNodeModel node, TurnSettings? settings);
}
