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

	/// <summary>All items of the node, any status, in FR.23.2 order. Returns copies.</summary>
	IReadOnlyList<AttentionItemModel> GetItems(string nodeId);

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
}
