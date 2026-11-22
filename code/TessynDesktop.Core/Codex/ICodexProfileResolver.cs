using TessynDesktop.Models;

namespace TessynDesktop.Codex;

/// <summary>
/// Which ChatGPT profile a Codex session runs on (FR.33.5): its pin, exact (FR.12.27 rules), or the inherit rule for a session without one.
/// A Claude pin never stands in for a ChatGPT one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexProfileResolver
{
	/// <summary>
	/// The profile of the session's next Codex turn: <paramref name="perTurnProfile"/> (an id or a display name) when given; else the session's
	/// Codex pin, exactly (a missing pin is an error naming it); else the inherit rule from its supervisor and directory.
	/// </summary>
	CodexProfileChoice ResolveForTurn(SessionNodeModel node, string? perTurnProfile = null);

	/// <summary>
	/// The inherit rule (FR.33.5, spawn, create, engine change): <paramref name="creator"/>'s profile when it is a ChatGPT one; else the
	/// directory's last-used ChatGPT profile; else the catalog profile; else the error <c>No signed-in ChatGPT profile…</c>.
	/// </summary>
	CodexProfileChoice Inherit(SessionNodeModel? creator, string workingDirectory);

	/// <summary>A ChatGPT profile given by id or by its <c>ChatGPT · &lt;email&gt;</c> name; null when it is not one.</summary>
	CodexProfileChoice? Find(string idOrName);

	/// <summary>The display names of the known ChatGPT profiles, for error texts.</summary>
	string ListNames();
}
