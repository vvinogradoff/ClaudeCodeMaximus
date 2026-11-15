using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// Async client of the daemon's remote API (TDCP): one typed method per remote method of the contract
/// (<c>.web-work/10-contract.md</c>), plus the push notification stream. Every call takes a <see cref="CancellationToken"/>.
/// Events are raised on thread-pool threads, never on the caller's context; a UI consumer marshals them itself.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial interface ITessynClient : IAsyncDisposable
{
	/// <summary>Current connection state.</summary>
	TessynClientState State { get; }

	/// <summary>Raised after <see cref="State"/> changed.</summary>
	event EventHandler<TessynClientStateChangedEventArgs>? StateChanged;

	/// <summary>The latest <c>client.hello</c> result (run id, protocol versions, capabilities, event seq); null before the first successful connect.</summary>
	HelloResult? ServerInfo { get; }

	/// <summary>
	/// Raised for every notification of the daemon's event log, in seq order, once per event
	/// (<see cref="RemoteNotification.Seq"/> is set). <c>resync</c> frames are not delivered here; see <see cref="Resynced"/>.
	/// </summary>
	event EventHandler<RemoteNotification>? NotificationReceived;

	/// <summary>Raised when the consumer must reload its state: the daemon could not replay missed events, or it restarted.</summary>
	event EventHandler<ResyncedEventArgs>? Resynced;

	/// <summary>
	/// Performs the handshake and starts the notification stream. Throws <see cref="TessynAuthException"/> (state AuthFailed),
	/// <see cref="TessynIncompatibleException"/> (state Incompatible) or <see cref="HttpRequestException"/> (state Disconnected).
	/// After it returned, a dropped stream is re-established in the background (state Reconnecting).
	/// </summary>
	Task ConnectAsync(CancellationToken cancellationToken = default);

	/// <summary><c>client.hello</c>: the daemon's identity, protocol range and capabilities.</summary>
	Task<HelloResult> HelloAsync(CancellationToken cancellationToken = default);

	/// <summary><c>tree.snapshot</c>; with <paramref name="sinceVersion"/> equal to the current version the answer is <c>Unchanged</c>.</summary>
	Task<TreeSnapshotResult> TreeSnapshotAsync(long? sinceVersion = null, CancellationToken cancellationToken = default);

	/// <summary><c>tree.workers</c>: the worker sessions of a supervisor.</summary>
	Task<TreeWorkersResult> TreeWorkersAsync(string supervisorId, CancellationToken cancellationToken = default);

	/// <summary><c>tree.search</c>.</summary>
	Task<TreeSearchResult> TreeSearchAsync(string query, bool? exact = null, string? scopeNodeId = null, CancellationToken cancellationToken = default);

	/// <summary><c>tree.createSession</c>: creates a session next to <paramref name="anchorId"/>.</summary>
	Task<CreateSessionResult> TreeCreateSessionAsync(string anchorId, string name, CancellationToken cancellationToken = default);

	/// <summary><c>recent.list</c>.</summary>
	Task<RecentListResult> RecentListAsync(CancellationToken cancellationToken = default);

	/// <summary><c>recent.hide</c>.</summary>
	Task<RecentHideResult> RecentHideAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary><c>tags.search</c>.</summary>
	Task<TagSearchResult> TagsSearchAsync(string text, IReadOnlyList<TagRefDto> selected, string? scopeNodeId = null, CancellationToken cancellationToken = default);

	/// <summary><c>session.get</c>.</summary>
	Task<SessionDetailDto> SessionGetAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary><c>session.entries</c>: a page of transcript entries (newest page when no index is given).</summary>
	Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, int? beforeIndex = null, int? afterIndex = null, int? limit = null, CancellationToken cancellationToken = default);

	/// <summary><c>session.entry</c>: one entry with its full content; <paramref name="epoch"/> comes from <see cref="SessionEntriesResult.Epoch"/>.</summary>
	Task<SessionEntryDto> SessionEntryAsync(string nodeId, int index, int epoch, CancellationToken cancellationToken = default);

	/// <summary><c>session.send</c>.</summary>
	Task<SessionSendResult> SessionSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default);

	/// <summary><c>session.stop</c>.</summary>
	Task<SessionStopResult> SessionStopAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary><c>session.compact</c>.</summary>
	Task<SessionCompactResult> SessionCompactAsync(string nodeId, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default);

	/// <summary><c>session.setFavorite</c>.</summary>
	Task<SetFavoriteResult> SessionSetFavoriteAsync(string nodeId, bool value, CancellationToken cancellationToken = default);

	/// <summary><c>session.setStreaming</c>; turning it off may answer <c>NeedsConfirm</c> unless <paramref name="force"/> is true.</summary>
	Task<SetStreamingResult> SessionSetStreamingAsync(string nodeId, bool enabled, bool? force = null, CancellationToken cancellationToken = default);

	/// <summary><c>attention.list</c>.</summary>
	Task<AttentionListResult> AttentionListAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary><c>attention.reply</c>.</summary>
	Task<AttentionReplyResult> AttentionReplyAsync(string nodeId, IReadOnlyList<AttentionAnswerParams> answers, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default);

	/// <summary><c>attention.dismiss</c>.</summary>
	Task<AttentionDismissResult> AttentionDismissAsync(string nodeId, string itemId, CancellationToken cancellationToken = default);

	/// <summary><c>catalog.get</c>: models, efforts and profiles.</summary>
	Task<CatalogResult> CatalogGetAsync(CancellationToken cancellationToken = default);

	/// <summary><c>usage.get</c>.</summary>
	Task<UsageResult> UsageGetAsync(string? profileId = null, CancellationToken cancellationToken = default);

	// ── P1.F / P1.G (additive; grouped at the end) ───────────────────────────────────────────

	/// <summary><c>session.entries</c> with the P1.F bounds (<c>afterSeq</c>, <c>beforeEntryId</c>); needs the <c>entries.ids</c> capability for ids.</summary>
	Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, SessionEntriesQuery query, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>session.entry</c> by stable id (<see cref="SessionEntryDto.EntryId"/>), optionally only a character slice of the content.
	/// Fails with a <see cref="TessynRpcException"/> "Session history changed" when the id's content generation is gone.
	/// </summary>
	Task<SessionEntryDto> SessionEntryByIdAsync(string nodeId, string entryId, EntryRangeParams? range = null, CancellationToken cancellationToken = default);

	/// <summary><c>session.headers</c> (loopback only): O(1) content headers of many sessions at once; at most 500 ids.</summary>
	Task<SessionHeadersResult> SessionHeadersAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default);

	// ── Focus references (additive) ──────────────────────────────────────────────────────────

	/// <summary>
	/// <c>file.read</c>: one chunk of the file behind a handle (<see cref="AttentionRefDto.RefId"/> or a <see cref="FilePreviewResult.Handle"/>).
	/// Text comes back as UTF-8, other types as base64; continue at <c>offset + length</c> until <see cref="FileReadResult.Eof"/>.
	/// </summary>
	Task<FileReadResult> FileReadAsync(string handle, long? offset = null, int? length = null, CancellationToken cancellationToken = default);

	/// <summary><c>file.preview</c>: a handle for a path that occurs in the text of the given entry.</summary>
	Task<FilePreviewResult> FilePreviewAsync(string nodeId, string entryId, string path, CancellationToken cancellationToken = default);
}
