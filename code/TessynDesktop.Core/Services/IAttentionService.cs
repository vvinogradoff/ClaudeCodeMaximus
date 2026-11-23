using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Owns every session's Attention Ledger (FR.23): posting and deduping items, closing them from the
/// session or the user, and reporting the state the Focus view, badge and instruction block need.
/// A Singleton: it caches ledgers and serialises writes. The UI reaches it only through
/// <see cref="IDaemonClient"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAttentionService
{
	/// <summary>Raised once after every call that changed a ledger (FR.23.16). May fire on any thread.</summary>
	event EventHandler<AttentionChangedEventArgs>? AttentionChanged;

	/// <summary>Raised once per <see cref="Post"/> that created at least one new item (FR.22.1), after <see cref="AttentionChanged"/>. Never for an update or a close. May fire on any thread.</summary>
	event EventHandler<AttentionItemsCreatedEventArgs>? ItemsCreated;

	/// <summary>
	/// Pins <paramref name="requests"/> to the node's ledger (FR.23.4): item by item, an open item with the same
	/// key (else the same normalised title) is updated, otherwise a new <c>A{n}</c> is created. Writes a SYSTEM
	/// entry per item (FR.23.8). Outcomes are in request order. Raises <see cref="AttentionChanged"/> once.
	/// </summary>
	IReadOnlyList<AttentionPostOutcomeModel> Post(SessionNodeModel node, IReadOnlyList<AttentionPostRequestModel> requests);

	/// <summary>
	/// Closes an open item from the session side (FR.23.5): <see cref="AttentionStatus.Answered"/> sets the answer,
	/// <see cref="AttentionStatus.Resolved"/> the resolution note. <paramref name="itemId"/> is case-insensitive.
	/// An already closed or unknown item is left untouched (no SYSTEM entry, no event).
	/// </summary>
	AttentionResolveResultModel ResolveBySession(SessionNodeModel node, string itemId, AttentionStatus status, string? note);

	/// <summary>
	/// Closes every open item of the node that is the card of elevated run <paramref name="runId"/> as <see cref="AttentionStatus.Resolved"/> with
	/// <paramref name="note"/> (FR.23.31); never Dismissed, so no "closed by the user" line follows. Writes a SYSTEM entry per item when <paramref name="node"/> is
	/// known. Returns how many items were closed.
	/// </summary>
	int CloseElevatedRunItems(string nodeId, SessionNodeModel? node, string runId, string note);

	/// <summary>Raises <see cref="AttentionChanged"/> for the node without changing a ledger: the run behind an elevated-run item changed (tdcp-spec 10.A13).</summary>
	void NotifyChanged(string nodeId);

	/// <summary>
	/// Tells the ledger how to see whether an elevated run is launching or running (FR.23.41), by run id. The elevated-run service sets it once, as it cannot be a
	/// constructor dependency of this service. Without it no item is in progress.
	/// </summary>
	void SetRunProgress(Func<string, bool> isRunInProgress) { }

	/// <summary>All items of the node, any status, in FR.23.2 order (in-progress items after the other open ones, FR.23.41). Returns copies.</summary>
	IReadOnlyList<AttentionItemModel> GetItems(string nodeId);

	/// <summary>
	/// False when the node's ledger file was unreadable the last time it was loaded, so <see cref="GetItems"/> answers with an empty list that does not mean
	/// "no items". Anything that deletes data because an item is missing (the Focus reply drafts, FR.23.19) must not act then.
	/// </summary>
	bool IsLedgerReadable(string nodeId) => true;

	/// <summary>Open-item count (Decided excluded) and most severe open kind (FR.23.13), plus both over every open item (Decided included) for session-row badges.</summary>
	AttentionSummaryModel GetSummary(string nodeId);

	/// <summary>
	/// Marks items answered from the Focus view (FR.23.15): status Answered, Answer = the text sent, no SYSTEM entry.
	/// Unknown or already closed ids are skipped.
	/// </summary>
	void MarkAnsweredByUser(string nodeId, IReadOnlyList<AttentionAnswerModel> answers);

	/// <summary>
	/// Closes an open item from the Focus view (FR.23.15): a Todo becomes Resolved ("Marked done by the user"), any
	/// other kind Dismissed; flags the closure for the next instruction block and writes a SYSTEM entry.
	/// Returns false (and changes nothing) for an unknown or already closed item.
	/// </summary>
	bool CloseByUser(SessionNodeModel node, string itemId);

	/// <summary>Returns the user-closed items not yet reported to the session, oldest closure first, and clears their flag (FR.23.11).</summary>
	IReadOnlyList<AttentionItemModel> TakeUnreportedUserClosures(string nodeId);

	/// <summary>
	/// The file reference with this opaque <see cref="AttentionRefModel.RefId"/> and the session it was posted from, or null. Looks
	/// through every ledger, including those not loaded yet; the index is built once and kept current by <see cref="Post"/>.
	/// </summary>
	(string NodeId, AttentionRefModel Ref)? FindFileRef(string refId);

	/// <summary>The file references of the node that grant read access now: those of open items and of items closed within <see cref="Constants.FileAccess.RefGracePeriod"/>.</summary>
	IReadOnlyList<AttentionRefModel> GetActiveFileRefs(string nodeId);
}
