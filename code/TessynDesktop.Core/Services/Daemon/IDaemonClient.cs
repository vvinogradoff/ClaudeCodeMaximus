using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// The single boundary between the Avalonia UI and the daemon-owned domain (Stage 4 of the
/// UI/daemon split). Today an in-process adapter (<see cref="InProcDaemonClient"/>) delegates to
/// Core services; after Stage 5 a wire adapter (WireDaemonClient) will implement this same surface
/// over TDCP (the Tessyn Daemon Control Protocol). ViewModels are migrated to depend only on this
/// interface so that switching transport later touches no ViewModel.
/// </summary>
/// <remarks>
/// Covers the daemon-owned operations the UI's core turn / tree-CRUD / schedule / content flows
/// require. Tree CRUD was folded in during Stage 5b (it was a hard prerequisite for
/// <c>SessionTreeViewModel</c> to depend on this interface at all). Model/effort/profile selection
/// and usage still fold in as their consumers migrate (Stages 6–7). See
/// docs/refactoring/tdcp-spec.md for the wire-method mapping.
/// Created by Claude.
/// </remarks>
public interface IDaemonClient
{
	// ── Change notification (daemon → UI) ──────────────────────────────────────

	/// <summary>Raised when the session tree changes (session added, running-state, settings saved, reload).</summary>
	event EventHandler<TreeChangedEventArgs> TreeChanged;

	/// <summary>Raised when a schedule is added, removed, or fires. Used to refresh session UI.</summary>
	event EventHandler? ScheduleChanged;

	/// <summary>Raised when a single entry is appended to a session file (FR.21.9) — the tap point external mirrors (e.g. the Telegram bridge) subscribe to instead of depending on ISessionFileService directly.</summary>
	event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;

	/// <summary>Raised once per finished turn, after its last entry was appended (FR.21.22) — relayed from ISessionFileService.TurnCompleted so the Telegram bridge can post a turn's final answer.</summary>
	event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted;

	/// <summary>Raised once after a session's Attention Ledger changed (FR.23.16) — relayed from IAttentionService. May fire on any thread.</summary>
	event EventHandler<AttentionChangedEventArgs>? AttentionChanged;

	/// <summary>Raised once after a session's tags changed (FR.24.7); a null NodeId means every session may be affected. Relayed from ISessionTagService. May fire on any thread.</summary>
	event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged;

	// ── Tree read ───────────────────────────────────────────────────────────────

	/// <summary>The authoritative directory list (source of truth for the tree projection).</summary>
	IReadOnlyList<DirectoryNodeModel> Directories { get; }

	/// <summary>NodeId of the currently selected session in the UI, or null.</summary>
	string? SelectedNodeId { get; set; }

	SessionNodeModel? FindSessionByNodeId(string nodeId);
	SessionNodeModel? FindSessionByAgentToken(string token);

	/// <summary>Finds the session owning a given session file name (FR.21.9).</summary>
	SessionNodeModel? FindSessionByFileName(string fileName);

	/// <summary>Resolves the (directory label, tree path) location of a session for header display.</summary>
	(string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session);

	// ── Running state ────────────────────────────────────────────────────────────

	/// <summary>Returns true while a claude process is running for the given node.</summary>
	bool IsSessionRunning(string nodeId);

	/// <summary>Marks the session as running or not. Thread-safe. Raises <see cref="TreeChanged"/>.</summary>
	void SetSessionRunning(string nodeId, bool isRunning);

	// ── Turns ────────────────────────────────────────────────────────────────────

	/// <summary>Acquires (or creates) the exclusive per-node turn lock (at most one turn per NodeId).</summary>
	SemaphoreSlim GetTurnLock(string nodeId);

	/// <summary>Runs a single headless turn against <paramref name="node"/> (scheduler / orchestration / UI).</summary>
	Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default);

	/// <summary>Cancels any running turn for the given node. Returns true if a running turn was cancelled.</summary>
	bool CancelTurn(string nodeId);

	/// <summary>Resolves the profile display name a turn against <paramref name="node"/> would use (inheritance).</summary>
	string? ResolveProfileName(SessionNodeModel node);

	// ── Schedules ────────────────────────────────────────────────────────────────

	/// <summary>Returns all schedules, or only those targeting <paramref name="targetNodeId"/> when non-null.</summary>
	IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null);

	/// <summary>Adds a schedule, persists immediately, and wakes the timer if needed.</summary>
	void AddSchedule(ScheduleModel schedule);

	/// <summary>Removes a schedule by ID. Returns true if found and removed.</summary>
	bool RemoveSchedule(string scheduleId);

	// ── Session content (read) ─────────────────────────────────────────────────────

	/// <summary>Reads the parsed message entries of a session file.</summary>
	IReadOnlyList<SessionEntryModel> ReadEntries(string fileName);

	/// <summary>Returns true if the session .txt file exists.</summary>
	bool SessionFileExists(string fileName);

	/// <summary>Returns the full filesystem path for a session file name.</summary>
	string GetSessionFilePath(string fileName);

	// ── Initialization (Stage 5b) ──────────────────────────────────────────────────

	/// <summary>Backfills NodeId/AgentToken on any sessions missing them, then saves if dirty. Raises <see cref="TreeChanged"/> with <see cref="TreeChangeKind.Reloaded"/>.</summary>
	void LoadFromSettings();

	// ── CRUD – Directory (Stage 5b) ─────────────────────────────────────────────────

	DirectoryNodeModel AddDirectory(string path);
	bool TryDeleteDirectory(DirectoryNodeModel directory);

	// ── CRUD – Group (Stage 5b) ──────────────────────────────────────────────────────

	GroupNodeModel AddGroup(DirectoryNodeModel parent, string name);
	GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name);
	bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group);
	bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group);
	void RenameGroup(GroupNodeModel group, string newName);

	// ── CRUD – Session (Stage 5b) ────────────────────────────────────────────────────

	SessionNodeModel AddSession(DirectoryNodeModel parent, string name);
	SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name);
	SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId);
	SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId);
	bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false);
	bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false);
	void RenameSession(SessionNodeModel session, string newName);

	// ── Model-level move (Stage 5b) ──────────────────────────────────────────────────

	/// <summary>Removes the session from its current model container. Returns the parent directory (or null if not found).</summary>
	DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session);
	void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index);
	void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index);

	// ── Orchestration helpers (Stage 5b) ─────────────────────────────────────────────

	List<SessionSummaryModel> BuildAgentSessionSummaries();
	IReadOnlySet<string> CollectAllClaudeSessionIds();

	// ── Attention Ledger (FR.23) ─────────────────────────────────────────────────────

	/// <summary>All Attention Items of a session, any status, ordered per FR.23.2.</summary>
	IReadOnlyList<AttentionItemModel> GetAttentionItems(string nodeId);

	/// <summary>Open-item count (Decided excluded) and most severe open kind — the Focus badge's inputs (FR.23.13) — plus both over every open item (Decided included) for the session-row badge in the tree and the Recent dropdown.</summary>
	AttentionSummaryModel GetAttentionSummary(string nodeId);

	/// <summary>Marks items answered from the Focus view, Answer = the text sent (FR.23.15). Unknown or closed ids are skipped.</summary>
	void MarkAttentionAnswered(string nodeId, IReadOnlyList<AttentionAnswerModel> answers);

	/// <summary>Closes an item from the Focus view: a Todo becomes Resolved, any other kind Dismissed (FR.23.15). Sends no prompt.</summary>
	void CloseAttentionByUser(string nodeId, string itemId);

	/// <summary>Returns the items the user closed since the last turn and clears their flag, so each closure is reported once (FR.23.11).</summary>
	IReadOnlyList<AttentionItemModel> TakeUnreportedAttentionClosures(string nodeId);

	// ── Session Tags (FR.24) ───────────────────────────────────────────────────────────

	/// <summary>Name-derived plus declared tags of a session in FR.24.7 order; empty for an unknown node.</summary>
	IReadOnlyList<SessionTagModel> GetSessionTags(string nodeId);

	/// <summary>True once per node per app run — the tag guidance is due on this turn (FR.24.6).</summary>
	bool TakeTagGuidanceDue(string nodeId);

	// ── Cross-project search & titles (Stage 6) ──────────────────────────────────────

	/// <summary>Re-scans ~/.claude/projects/*/*.jsonl and updates the search index (incremental). Returns the count (re)indexed.</summary>
	Task<int> ReindexSessionsAsync(CancellationToken cancellationToken = default);

	/// <summary>Full-text searches indexed session content across all projects, ranked by relevance.</summary>
	Task<IReadOnlyList<SessionSearchResultModel>> SearchSessionsAsync(
		string query, string? projectSlug = null, string? role = null,
		int? limit = null, int? offset = null, CancellationToken cancellationToken = default);

	/// <summary>Distinct external IDs with at least one matching message — unranked and uncapped, for content-filtering the tree. Optionally scoped to a single project slug.</summary>
	Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default);

	/// <summary>Per-session strict (exact phrase) and loose (all words) matching-message counts, unranked and uncapped. Optionally scoped to a single project slug.</summary>
	Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default);

	/// <summary>Re-indexes one session from its JSONL file if it changed since last indexed; false when not found or unchanged.</summary>
	Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default);

	/// <summary>Returns a title per summary — cached if already generated, otherwise generates and persists it.</summary>
	Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default);

	/// <summary>Looks up indexed session summary metadata for a set of external IDs (e.g. cross-project search hits).</summary>
	Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
		IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default);
}
