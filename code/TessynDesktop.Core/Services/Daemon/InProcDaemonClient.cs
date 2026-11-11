using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// In-process implementation of <see cref="IDaemonClient"/>: delegates directly to the Core
/// services running in the same process. This is the Stage 4 adapter that lets ViewModels move
/// onto the daemon boundary before the daemon is a separate process. In Stage 5 a WireDaemonClient
/// implementing the same interface over TDCP replaces this one, with no ViewModel changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InProcDaemonClient : IDaemonClient, IDisposable
{
	private readonly ISessionRegistry _registry;
	private readonly ISessionTurnService _turnService;
	private readonly ISchedulerService _scheduler;
	private readonly ISessionFileService _fileService;
	private readonly IClaudeSessionIndexService _indexService;
	private readonly IAttentionService _attention;
	private readonly ISessionTagService _tags;

	public InProcDaemonClient(
		ISessionRegistry registry,
		ISessionTurnService turnService,
		ISchedulerService scheduler,
		ISessionFileService fileService,
		IClaudeSessionIndexService indexService,
		IAttentionService attention,
		ISessionTagService tags)
	{
		_registry = registry;
		_turnService = turnService;
		_scheduler = scheduler;
		_fileService = fileService;
		_indexService = indexService;
		_attention = attention;
		_tags = tags;

		// Forward the underlying domain events through the boundary. Over TDCP these become
		// push notifications on the change channel; in-process they are a direct relay.
		_registry.TreeChanged += (s, e) => TreeChanged?.Invoke(this, e);
		_scheduler.ScheduleChanged += (s, e) => ScheduleChanged?.Invoke(this, e);
		_fileService.EntryAppended += (s, e) => EntryAppended?.Invoke(this, e);
		_fileService.TurnCompleted += (s, e) => TurnCompleted?.Invoke(this, e);
		_indexService.SessionIndexed += (s, e) => SessionIndexed?.Invoke(this, e);
		_attention.AttentionChanged += OnAttentionChanged;
		_tags.SessionTagsChanged += OnSessionTagsChanged;
	}

	/// <summary>Stops relaying <see cref="IAttentionService.AttentionChanged"/> (FR.23.16).</summary>
	public void Dispose()
	{
		_attention.AttentionChanged -= OnAttentionChanged;
		_tags.SessionTagsChanged -= OnSessionTagsChanged;
	}

	private void OnAttentionChanged(object? sender, AttentionChangedEventArgs e) => AttentionChanged?.Invoke(this, e);

	private void OnSessionTagsChanged(object? sender, SessionTagsChangedEventArgs e) => SessionTagsChanged?.Invoke(this, e);

	// ── Change notification ─────────────────────────────────────────────────────

	public event EventHandler<TreeChangedEventArgs>? TreeChanged;
	public event EventHandler? ScheduleChanged;
	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;
	public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted;
	public event EventHandler<SessionIndexedEventArgs>? SessionIndexed;
	public event EventHandler<AttentionChangedEventArgs>? AttentionChanged;
	public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged;

	// ── Tree read ─────────────────────────────────────────────────────────────────

	public IReadOnlyList<DirectoryNodeModel> Directories => _registry.Directories;

	public string? SelectedNodeId
	{
		get => _registry.SelectedNodeId;
		set => _registry.SelectedNodeId = value;
	}

	public SessionNodeModel? FindSessionByNodeId(string nodeId) => _registry.FindModelByNodeId(nodeId);

	public SessionNodeModel? FindSessionByAgentToken(string token) => _registry.FindModelByAgentToken(token);

	public SessionNodeModel? FindSessionByFileName(string fileName) => _registry.FindModelByFileName(fileName);

	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session)
		=> _registry.GetSessionLocation(session);

	// ── Running state ───────────────────────────────────────────────────────────

	public bool IsSessionRunning(string nodeId) => _registry.IsSessionRunning(nodeId);

	public void SetSessionRunning(string nodeId, bool isRunning) => _registry.SetSessionRunning(nodeId, isRunning);

	// ── Turns ─────────────────────────────────────────────────────────────────────

	public SemaphoreSlim GetTurnLock(string nodeId) => _turnService.GetTurnLock(nodeId);

	public Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default)
		=> _turnService.RunTurnAsync(node, prompt, source, cancellationToken);

	public bool CancelTurn(string nodeId) => _turnService.CancelTurn(nodeId);

	public string? ResolveProfileName(SessionNodeModel node) => _turnService.ResolveProfileName(node);

	// ── Schedules ───────────────────────────────────────────────────────────────

	public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null) => _scheduler.GetSchedules(targetNodeId);

	public void AddSchedule(ScheduleModel schedule) => _scheduler.AddSchedule(schedule);

	public bool RemoveSchedule(string scheduleId) => _scheduler.RemoveSchedule(scheduleId);

	// ── Session content (read) ─────────────────────────────────────────────────────

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => _fileService.ReadEntries(fileName);

	public bool SessionFileExists(string fileName) => _fileService.SessionFileExists(fileName);

	public string GetSessionFilePath(string fileName) => _fileService.GetFullPath(fileName);

	// ── Initialization ───────────────────────────────────────────────────────────

	public void LoadFromSettings() => _registry.LoadFromSettings();

	// ── CRUD – Directory ─────────────────────────────────────────────────────────

	public DirectoryNodeModel AddDirectory(string path) => _registry.AddDirectory(path);

	public bool TryDeleteDirectory(DirectoryNodeModel directory) => _registry.TryDeleteDirectory(directory);

	// ── CRUD – Group ─────────────────────────────────────────────────────────────

	public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name) => _registry.AddGroup(parent, name);

	public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name) => _registry.AddGroupToGroup(parent, name);

	public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group) => _registry.TryDeleteGroup(parent, group);

	public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group) => _registry.TryDeleteGroupFromGroup(parent, group);

	public void RenameGroup(GroupNodeModel group, string newName) => _registry.RenameGroup(group, newName);

	// ── CRUD – Session ───────────────────────────────────────────────────────────

	public SessionNodeModel AddSession(DirectoryNodeModel parent, string name) => _registry.AddSession(parent, name);

	public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name) => _registry.AddSessionToGroup(parent, name);

	public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId)
		=> _registry.ImportSession(parent, name, fileName, claudeSessionId);

	public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId)
		=> _registry.ImportSessionToGroup(parent, name, fileName, claudeSessionId);

	public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false)
		=> _registry.TryDeleteSession(parent, session, forceDelete);

	public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false)
		=> _registry.TryDeleteSessionFromGroup(parent, session, forceDelete);

	public void RenameSession(SessionNodeModel session, string newName) => _registry.RenameSession(session, newName);

	// ── Model-level move ─────────────────────────────────────────────────────────

	public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session) => _registry.RemoveSessionFromModel(session);

	public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index)
		=> _registry.InsertSessionIntoDirectory(parent, session, index);

	public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index)
		=> _registry.InsertSessionIntoGroup(parent, session, index);

	// ── Orchestration helpers ────────────────────────────────────────────────────

	public List<SessionSummaryModel> BuildAgentSessionSummaries() => _registry.BuildAgentSessionSummaries();

	public IReadOnlySet<string> CollectAllClaudeSessionIds() => _registry.CollectAllClaudeSessionIds();

	// ── Attention Ledger ─────────────────────────────────────────────────────────

	public IReadOnlyList<AttentionItemModel> GetAttentionItems(string nodeId) => _attention.GetItems(nodeId);

	public AttentionSummaryModel GetAttentionSummary(string nodeId) => _attention.GetSummary(nodeId);

	public void MarkAttentionAnswered(string nodeId, IReadOnlyList<AttentionAnswerModel> answers)
		=> _attention.MarkAnsweredByUser(nodeId, answers);

	public void CloseAttentionByUser(string nodeId, string itemId)
	{
		// The SYSTEM entry needs the session file, so the node is resolved through the registry.
		var node = _registry.FindModelByNodeId(nodeId);
		if (node == null)
			return;

		_attention.CloseByUser(node, itemId);
	}

	public IReadOnlyList<AttentionItemModel> TakeUnreportedAttentionClosures(string nodeId)
		=> _attention.TakeUnreportedUserClosures(nodeId);

	// ── Session Tags ─────────────────────────────────────────────────────────────

	/// <summary>The node (and so its current name, for name-derived tags) is resolved through the registry.</summary>
	public IReadOnlyList<SessionTagModel> GetSessionTags(string nodeId)
	{
		var node = _registry.FindModelByNodeId(nodeId);
		return node == null ? [] : _tags.GetTags(nodeId, node.Name);
	}

	public bool TakeTagGuidanceDue(string nodeId) => _tags.TakeTagGuidanceDue(nodeId);

	/// <summary>FR.24.13 — the tree is snapshotted briefly under the registry lock; tags (which may read files) are read outside it.</summary>
	public IReadOnlyList<SessionTagUsageModel> GetSessionTagIndex(string? directoryPath)
	{
		var snapshot = _registry.SnapshotSessionsForTagIndex();
		return SessionTagIndexBuilder.Build(snapshot, s => _tags.GetTags(s.NodeId, s.Name), directoryPath);
	}

	// ── Cross-project search & titles ────────────────────────────────────────────────

	public Task<int> ReindexSessionsAsync(CancellationToken cancellationToken = default)
		=> _indexService.ReindexAsync(cancellationToken);

	public Task<IReadOnlyList<SessionSearchResultModel>> SearchSessionsAsync(
		string query, string? projectSlug = null, string? role = null,
		int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
		=> _indexService.SearchAsync(query, projectSlug, role, limit, offset, cancellationToken);

	public Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default)
		=> _indexService.SearchMatchingSessionIdsAsync(query, projectSlug, cancellationToken);

	public Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default)
		=> _indexService.SearchSessionScoresAsync(query, projectSlug, cancellationToken);

	public Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default)
		=> _indexService.IndexSessionAsync(externalId, workingDirectory, cancellationToken);

	public Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default)
		=> _indexService.EnsureTitlesAsync(summaries, onBatchComplete, cancellationToken);

	public Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
		IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default)
		=> _indexService.GetSessionSummariesAsync(externalIds, cancellationToken);
}
