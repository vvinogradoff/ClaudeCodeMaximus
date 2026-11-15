using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// The composer methods of package J2: unsent drafts and autocomplete. Loopback only; the daemon announces them with the <c>drafts</c> and
/// <c>autocomplete</c> capabilities (the session auth error and the turn options have their own flags, <c>session.authError</c> and <c>turn.options</c>).
/// </summary>
public partial interface ITessynClient
{
	/// <summary><c>draft.get</c>: the unsent text of a session (empty when there is none) and its revision.</summary>
	Task<DraftGetResult> DraftGetAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>draft.set</c>: stores the draft; an empty <paramref name="text"/> deletes it. With a <paramref name="baseRevision"/> (the revision this client last
	/// read or wrote) the write is refused with a <see cref="TessynRpcException"/> of code -32000 "Draft changed" when another writer changed the draft
	/// meanwhile: re-read it with <see cref="DraftGetAsync"/> and merge. Without one the draft is overwritten.
	/// </summary>
	Task<DraftSetResult> DraftSetAsync(string nodeId, string text, string? baseRevision = null, CancellationToken cancellationToken = default);

	/// <summary><c>draft.get</c> with <c>itemId</c> (FR.23.19): the unsent reply text of one Focus item and its revision. An unknown or closed item is -32602.</summary>
	Task<DraftGetResult> ReplyDraftGetAsync(string nodeId, string itemId, CancellationToken cancellationToken = default);

	/// <summary><c>draft.set</c> with <c>itemId</c> (FR.23.19): like <see cref="DraftSetAsync"/>, for the reply draft of one Focus item.</summary>
	Task<DraftSetResult> ReplyDraftSetAsync(string nodeId, string itemId, string text, string? baseRevision = null, CancellationToken cancellationToken = default);

	/// <summary><c>draft.delete</c>: removes the draft; <see cref="DraftDeleteResult.Deleted"/> says whether there was one.</summary>
	Task<DraftDeleteResult> DraftDeleteAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>autocomplete.suggest</c>: the <c>#</c> symbol, <c>##</c> file or drive-path suggestions for the caret position of <paramref name="text"/>. The debounce is
	/// the caller's. While the code index of the session's directory is being built the answer has <see cref="AutocompleteSuggestResult.IndexReady"/> false and no
	/// items: ask again shortly.
	/// </summary>
	Task<AutocompleteSuggestResult> AutocompleteSuggestAsync(string nodeId, string text, int caret, CancellationToken cancellationToken = default);
}
