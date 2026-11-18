using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The session's model and effort (FR.12.35): the one rule every turn path, compaction, <c>lastUsed</c> and the caller's tier (FR.16.6)
/// use, and the sticky picks of interactive sends.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionModelSettings
{
	/// <summary>
	/// What a turn of <paramref name="node"/> without a pick uses: the model is its <see cref="SessionNodeModel.ModelId"/> (<c>"default"</c> = no flag,
	/// no fallback), else the directory's, else the app's <c>SelectedModelId</c>; the effort is its <see cref="SessionNodeModel.Effort"/>. Null = no flag.
	/// </summary>
	SessionModelSettingsModel ResolveSession(SessionNodeModel node);

	/// <summary>The <c>--model</c> and <c>--effort</c> of one turn: a per-turn pick wins (<c>"default"</c> = no flag), else <see cref="ResolveSession"/>.</summary>
	SessionModelSettingsModel ResolveTurn(SessionNodeModel node, TurnOptionsModel? options);

	/// <summary>
	/// What a client shows as the session's choice (<c>lastUsed</c>): <see cref="ResolveSession"/> with the model in the catalog's form (null, shown as
	/// <c>"default"</c>, when it is not listed) and a known effort (null otherwise).
	/// </summary>
	SessionModelSettingsModel ResolveShown(SessionNodeModel node);

	/// <summary>
	/// Sticky picks: stores a model or effort pick of an interactive send (<c>turn.send</c>, <c>session.send</c>, <c>attention.reply</c>) that differs
	/// from what the client is shown for the session (<see cref="ResolveShown"/>), and saves the settings. A pick equal to it changes nothing, so a
	/// client that sends back what it shows never pins or resets a session. True when the session changed.
	/// </summary>
	bool RecordPicks(SessionNodeModel node, TurnOptionsModel? options);
}
