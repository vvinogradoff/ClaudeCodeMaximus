using System.IO;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ──────────────────────────────────────────────────────────────────────

internal sealed class InProcFakeRegistry : ISessionRegistry
{
    public event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged;
    public string? LastSettingsChangedNodeId { get; private set; }
    public void RaiseSessionSettingsChanged(string nodeId)
    {
        LastSettingsChangedNodeId = nodeId;
        SessionSettingsChanged?.Invoke(this, new SessionSettingsChangedEventArgs { NodeId = nodeId });
    }
    public DirectoryNodeModel? FindDirectoryByNodeId(string nodeId) => null;
    public GroupNodeModel? FindGroupByNodeId(string nodeId) => null;
    public event EventHandler<TreeChangedEventArgs> TreeChanged = delegate { };

    public string? SelectedNodeId { get; set; }
    public IReadOnlyList<DirectoryNodeModel> Directories { get; set; } = Array.Empty<DirectoryNodeModel>();

    public SessionNodeModel? FindByNodeIdResult { get; set; }
    public SessionNodeModel? FindByAgentTokenResult { get; set; }
    public SessionNodeModel? FindByFileNameResult { get; set; }
    public (string directoryLabel, string treePath) LocationResult { get; set; } = ("dir", "path");
    public bool IsRunningResult { get; set; }
    public string? SetRunningNodeId { get; private set; }
    public bool? SetRunningValue { get; private set; }

    public bool LoadFromSettingsCalled { get; private set; }

    public DirectoryNodeModel AddDirectoryResult { get; set; } = new() { Path = "/added" };
    public string? LastAddDirectoryPath { get; private set; }

    public bool TryDeleteDirectoryResult { get; set; }
    public DirectoryNodeModel? LastTryDeleteDirectoryArg { get; private set; }

    public GroupNodeModel AddGroupResult { get; set; } = new() { Name = "added-group" };
    public DirectoryNodeModel? LastAddGroupParent { get; private set; }
    public string? LastAddGroupName { get; private set; }

    public GroupNodeModel AddGroupToGroupResult { get; set; } = new() { Name = "added-subgroup" };
    public GroupNodeModel? LastAddGroupToGroupParent { get; private set; }
    public string? LastAddGroupToGroupName { get; private set; }

    public bool TryDeleteGroupResult { get; set; }
    public (DirectoryNodeModel parent, GroupNodeModel group)? LastTryDeleteGroupArgs { get; private set; }

    public bool TryDeleteGroupFromGroupResult { get; set; }
    public (GroupNodeModel parent, GroupNodeModel group)? LastTryDeleteGroupFromGroupArgs { get; private set; }

    public (GroupNodeModel group, string newName)? LastRenameGroupArgs { get; private set; }

    public SessionNodeModel AddSessionResult { get; set; } = new() { NodeId = "added-session", Name = "Added", FileName = "added.txt" };
    public (DirectoryNodeModel parent, string name)? LastAddSessionArgs { get; private set; }

    public SessionNodeModel AddSessionToGroupResult { get; set; } = new() { NodeId = "added-session-group", Name = "AddedGroup", FileName = "added-group.txt" };
    public (GroupNodeModel parent, string name)? LastAddSessionToGroupArgs { get; private set; }

    public SessionNodeModel ImportSessionResult { get; set; } = new() { NodeId = "imported", Name = "Imported", FileName = "imported.txt" };
    public (DirectoryNodeModel parent, string name, string fileName, string claudeSessionId)? LastImportSessionArgs { get; private set; }

    public SessionNodeModel ImportSessionToGroupResult { get; set; } = new() { NodeId = "imported-group", Name = "ImportedGroup", FileName = "imported-group.txt" };
    public (GroupNodeModel parent, string name, string fileName, string claudeSessionId)? LastImportSessionToGroupArgs { get; private set; }

    public bool TryDeleteSessionResult { get; set; }
    public (DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete)? LastTryDeleteSessionArgs { get; private set; }

    public bool TryDeleteSessionFromGroupResult { get; set; }
    public (GroupNodeModel parent, SessionNodeModel session, bool forceDelete)? LastTryDeleteSessionFromGroupArgs { get; private set; }

    public (SessionNodeModel session, string newName)? LastRenameSessionArgs { get; private set; }

    public DirectoryNodeModel? RemoveSessionFromModelResult { get; set; }
    public SessionNodeModel? LastRemoveSessionFromModelArg { get; private set; }

    public (DirectoryNodeModel parent, SessionNodeModel session, int index)? LastInsertSessionIntoDirectoryArgs { get; private set; }
    public (GroupNodeModel parent, SessionNodeModel session, int index)? LastInsertSessionIntoGroupArgs { get; private set; }

    public List<SessionSummaryModel> BuildAgentSessionSummariesResult { get; set; } = new();
    public IReadOnlySet<string> CollectAllClaudeSessionIdsResult { get; set; } = new HashSet<string>();

    public void RaiseTreeChanged(TreeChangedEventArgs e) => TreeChanged.Invoke(this, e);

    public SessionNodeModel? FindModelByNodeId(string nodeId) => FindByNodeIdResult;
    public SessionNodeModel? FindModelByAgentToken(string token) => FindByAgentTokenResult;
    public Dictionary<string, SessionNodeModel> ModelsByFileName { get; } = new();
    public SessionNodeModel? FindModelByFileName(string fileName) =>
        ModelsByFileName.TryGetValue(fileName, out var model) ? model : FindByFileNameResult;
    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => LocationResult;
    public bool IsSessionRunning(string nodeId) => IsRunningResult;
    public void SetSessionRunning(string nodeId, bool isRunning) { SetRunningNodeId = nodeId; SetRunningValue = isRunning; }

    public void LoadFromSettings() => LoadFromSettingsCalled = true;

    public DirectoryNodeModel AddDirectory(string path) { LastAddDirectoryPath = path; return AddDirectoryResult; }
    public bool TryDeleteDirectory(DirectoryNodeModel directory) { LastTryDeleteDirectoryArg = directory; return TryDeleteDirectoryResult; }

    public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name)
    {
        LastAddGroupParent = parent;
        LastAddGroupName = name;
        return AddGroupResult;
    }

    public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name)
    {
        LastAddGroupToGroupParent = parent;
        LastAddGroupToGroupName = name;
        return AddGroupToGroupResult;
    }

    public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group)
    {
        LastTryDeleteGroupArgs = (parent, group);
        return TryDeleteGroupResult;
    }

    public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group)
    {
        LastTryDeleteGroupFromGroupArgs = (parent, group);
        return TryDeleteGroupFromGroupResult;
    }

    public void RenameGroup(GroupNodeModel group, string newName) => LastRenameGroupArgs = (group, newName);

    public SessionPlacementModel? CreateSessionAfter(string anchorNodeId, string name) => throw new NotImplementedException();
    public SessionNodeModel AddSession(DirectoryNodeModel parent, string name)
    {
        LastAddSessionArgs = (parent, name);
        return AddSessionResult;
    }

    public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name)
    {
        LastAddSessionToGroupArgs = (parent, name);
        return AddSessionToGroupResult;
    }

    public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId)
    {
        LastImportSessionArgs = (parent, name, fileName, claudeSessionId);
        return ImportSessionResult;
    }

    public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId)
    {
        LastImportSessionToGroupArgs = (parent, name, fileName, claudeSessionId);
        return ImportSessionToGroupResult;
    }

    public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false)
    {
        LastTryDeleteSessionArgs = (parent, session, forceDelete);
        return TryDeleteSessionResult;
    }

    public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false)
    {
        LastTryDeleteSessionFromGroupArgs = (parent, session, forceDelete);
        return TryDeleteSessionFromGroupResult;
    }

    public void RenameSession(SessionNodeModel session, string newName) => LastRenameSessionArgs = (session, newName);

    public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session)
    {
        LastRemoveSessionFromModelArg = session;
        return RemoveSessionFromModelResult;
    }

    public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index)
        => LastInsertSessionIntoDirectoryArgs = (parent, session, index);

    public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index)
        => LastInsertSessionIntoGroupArgs = (parent, session, index);

    public SessionNodeModel? CreateSessionForAgent(string workingDir, string name, string? group, int orchestrationDepth = 0, string? supervisorNodeId = null) => null;
    public List<SessionSummaryModel> BuildAgentSessionSummaries() => BuildAgentSessionSummariesResult;
    public List<SessionTagIndexSessionModel> TagIndexSnapshot { get; set; } = new();
    public List<SessionTagIndexSessionModel> SnapshotSessionsForTagIndex() => TagIndexSnapshot;
    public SessionListPageModel QueryAgentSessions(SessionListQueryModel query, string callerNodeId) =>
        new() { Scope = query.Scope, Sections = Array.Empty<SessionListSectionModel>(), Rows = Array.Empty<SessionListRowModel>(), TotalRows = 0, Offset = 0, Limit = query.Limit };
    public NodeIdResolutionModel ResolveNodeId(string idOrPrefix) => new() { Status = NodeIdResolutionStatus.NotFound };
    public IReadOnlySet<string> CollectAllClaudeSessionIds() => CollectAllClaudeSessionIdsResult;
}

internal sealed class InProcFakeTurnService : ISessionTurnService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    public TurnResultModel RunResult { get; set; } = new(AssistantText: "ok", SessionId: null);
    public bool CancelResult { get; set; }
    public string? ResolveProfileResult { get; set; }

    public SessionNodeModel? LastRunNode { get; private set; }
    public string? LastRunPrompt { get; private set; }
    public TurnSource? LastRunSource { get; private set; }
    public string? LastCancelNodeId { get; private set; }
    public SessionNodeModel? LastResolveNode { get; private set; }

    public SemaphoreSlim GetTurnLock(string nodeId) => _lock;

    public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default)
    {
        LastRunNode = node;
        LastRunPrompt = prompt;
        LastRunSource = source;
        return Task.FromResult(RunResult);
    }

    public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt,
        TurnSource source, TurnOptionsModel? options, CancellationToken cancellationToken = default) =>
        RunTurnAsync(node, prompt, source, cancellationToken);

    public bool CancelTurn(string nodeId) { LastCancelNodeId = nodeId; return CancelResult; }
    public string? ResolveProfileName(SessionNodeModel node) { LastResolveNode = node; return ResolveProfileResult; }
}

internal sealed class InProcFakeScheduler : ISchedulerService
{
    public event EventHandler? ScheduleChanged = delegate { };

    private readonly List<ScheduleModel> _schedules = new();
    public bool RemoveResult { get; set; } = true;

    public ScheduleModel? AddedSchedule { get; private set; }
    public string? RemovedScheduleId { get; private set; }
    public string? LastGetSchedulesNodeId { get; private set; }

    public void RaiseScheduleChanged() => ScheduleChanged?.Invoke(this, EventArgs.Empty);

    public void Start() { }
    public void Stop() { }

    public void AddSchedule(ScheduleModel schedule) { AddedSchedule = schedule; _schedules.Add(schedule); }
    public void AddOrMergeMailboxDelivery(string supervisorNodeId, string workerName, string resultText) { }
    public bool RemoveSchedule(string scheduleId) { RemovedScheduleId = scheduleId; return RemoveResult; }

    public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null)
    {
        LastGetSchedulesNodeId = targetNodeId;
        return _schedules;
    }
}

internal sealed class InProcFakeFileService : ISessionFileService
{
    public event EventHandler<SessionFileRewrittenEventArgs>? SessionFileRewritten { add { } remove { } }
    public IReadOnlyList<SessionEntryModel> ReadTailEntries(string fileName, int maxBytes) => Array.Empty<SessionEntryModel>();
    public SessionFileStampModel? GetFileStamp(string fileName) => null;
    public IReadOnlyList<SessionEntryModel> ReadEntriesResult { get; set; } = Array.Empty<SessionEntryModel>();
    public bool SessionFileExistsResult { get; set; }
    public string GetFullPathResult { get; set; } = "/path/to/file.txt";

    public string? LastReadEntriesFile { get; private set; }
    public string? LastFileExistsFile { get; private set; }
    public string? LastGetFullPathFile { get; private set; }

    public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;
    public void RaiseEntryAppended(SessionEntryAppendedEventArgs e) => EntryAppended?.Invoke(this, e);

    public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted;
    public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText) =>
        TurnCompleted?.Invoke(this, new SessionTurnCompletedEventArgs { FileName = fileName, Cancelled = cancelled, ErrorText = errorText });

    public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) { LastReadEntriesFile = fileName; return ReadEntriesResult; }
    public bool SessionFileExists(string fileName) { LastFileExistsFile = fileName; return SessionFileExistsResult; }
    public DateTimeOffset? GetLastWriteTimeUtc(string fileName) => null;
    public string GetFullPath(string fileName) { LastGetFullPathFile = fileName; return GetFullPathResult; }

    public string CreateSessionFile() => throw new NotImplementedException();
    public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null) { }
    public void AppendCompactionSeparator(string fileName) { }
    public void DeleteSessionFile(string fileName) { }
    public void RewriteSessionFile(string fileName, string content) { }
    public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) { }
    public int RepairCorruptedCompactions() => 0;
}

internal sealed class InProcFakeIndexService : IClaudeSessionIndexService
{
    public int ReindexResult { get; set; }
    public Exception? IndexSessionException { get; set; }
    public List<(string ExternalId, string WorkingDirectory)> IndexSessionCalls { get; } = new();

    public event EventHandler<SessionIndexedEventArgs>? SessionIndexed;
    public void RaiseSessionIndexed(string? externalId) =>
        SessionIndexed?.Invoke(this, new SessionIndexedEventArgs { ExternalId = externalId });
    public IReadOnlyList<SessionSearchResultModel> SearchResult { get; set; } = Array.Empty<SessionSearchResultModel>();
    public IReadOnlySet<string> MatchingSessionIdsResult { get; set; } = new HashSet<string>();
    public IReadOnlyDictionary<string, string> EnsureTitlesResult { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<ClaudeSessionSummaryModel> SummariesResult { get; set; } = Array.Empty<ClaudeSessionSummaryModel>();

    public string? LastSearchQuery { get; private set; }
    public string? LastSearchProjectSlug { get; private set; }
    public string? LastSearchRole { get; private set; }
    public int? LastSearchLimit { get; private set; }
    public int? LastSearchOffset { get; private set; }
    public string? LastMatchingIdsQuery { get; private set; }
    public string? LastMatchingIdsProjectSlug { get; private set; }
    public string? LastScoresQuery { get; private set; }
    public string? LastScoresProjectSlug { get; private set; }
    public string? LastIndexSessionExternalId { get; private set; }
    public string? LastIndexSessionWorkingDirectory { get; private set; }
    public IReadOnlyList<SessionSearchScoreModel> ScoresResult { get; set; } = Array.Empty<SessionSearchScoreModel>();
    public Exception? ScoresException { get; set; }
    public bool IndexSessionResult { get; set; }
    public IReadOnlyList<ClaudeSessionSummaryModel>? LastEnsureTitlesSummaries { get; private set; }
    public IReadOnlyList<string>? LastGetSummariesExternalIds { get; private set; }

    public Task<int> ReindexAsync(CancellationToken cancellationToken = default) => Task.FromResult(ReindexResult);

    public Task<IReadOnlyList<SessionSearchResultModel>> SearchAsync(
        string query, string? projectSlug = null, string? role = null,
        int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        LastSearchQuery = query;
        LastSearchProjectSlug = projectSlug;
        LastSearchRole = role;
        LastSearchLimit = limit;
        LastSearchOffset = offset;
        return Task.FromResult(SearchResult);
    }

    public Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default)
    {
        LastMatchingIdsQuery = query;
        LastMatchingIdsProjectSlug = projectSlug;
        return Task.FromResult(MatchingSessionIdsResult);
    }

    public Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default)
    {
        LastScoresQuery = query;
        LastScoresProjectSlug = projectSlug;
        if (ScoresException != null) throw ScoresException;
        return Task.FromResult(ScoresResult);
    }

    public Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default)
    {
        LastIndexSessionExternalId = externalId;
        LastIndexSessionWorkingDirectory = workingDirectory;
        lock (IndexSessionCalls)
            IndexSessionCalls.Add((externalId, workingDirectory));
        if (IndexSessionException != null)
            throw IndexSessionException;
        return Task.FromResult(IndexSessionResult);
    }

    public Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
        IReadOnlyList<ClaudeSessionSummaryModel> summaries,
        Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
        CancellationToken cancellationToken = default)
    {
        LastEnsureTitlesSummaries = summaries;
        return Task.FromResult(EnsureTitlesResult);
    }

    public Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
        IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default)
    {
        LastGetSummariesExternalIds = externalIds;
        return Task.FromResult(SummariesResult);
    }
}

// ── Tests ───────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class InProcDaemonClientTests
{
    private readonly InProcFakeRegistry _registry = new();
    private readonly InProcFakeTurnService _turnService = new();
    private readonly InProcFakeScheduler _scheduler = new();
    private readonly InProcFakeFileService _fileService = new();
    private readonly InProcFakeIndexService _indexService = new();
    private readonly FakeAttentionService _attention = new();
    private readonly FakeSessionTagService _tags = new();
    private readonly InProcDaemonClient _client;

    public InProcDaemonClientTests()
    {
        _client = new InProcDaemonClient(_registry, _turnService, _scheduler, _fileService, _indexService, _attention, _tags);
    }

    private static SessionNodeModel MakeNode(string nodeId = "node-1") =>
        new() { NodeId = nodeId, Name = "Test", FileName = $"{nodeId}.txt" };

    // ── Tree read ────────────────────────────────────────────────────────────────

    [Fact]
    public void Directories_DelegatesToRegistry()
    {
        var dirs = new List<DirectoryNodeModel> { new() { Path = "/test" } };
        _registry.Directories = dirs;

        Assert.Same(dirs, _client.Directories);
    }

    [Fact]
    public void SelectedNodeId_Get_DelegatesToRegistry()
    {
        _registry.SelectedNodeId = "abc";

        Assert.Equal("abc", _client.SelectedNodeId);
    }

    [Fact]
    public void SelectedNodeId_Set_DelegatesToRegistry()
    {
        _client.SelectedNodeId = "xyz";

        Assert.Equal("xyz", _registry.SelectedNodeId);
    }

    [Fact]
    public void FindSessionByNodeId_DelegatesToRegistry()
    {
        var node = MakeNode();
        _registry.FindByNodeIdResult = node;

        var result = _client.FindSessionByNodeId("node-1");

        Assert.Same(node, result);
    }

    [Fact]
    public void FindSessionByAgentToken_DelegatesToRegistry()
    {
        var node = MakeNode();
        _registry.FindByAgentTokenResult = node;

        var result = _client.FindSessionByAgentToken("tok-abc");

        Assert.Same(node, result);
    }

    [Fact]
    public void FindSessionByFileName_DelegatesToRegistry()
    {
        var node = MakeNode();
        _registry.FindByFileNameResult = node;

        var result = _client.FindSessionByFileName("node-1.txt");

        Assert.Same(node, result);
    }

    [Fact]
    public void GetSessionLocation_DelegatesToRegistry()
    {
        _registry.LocationResult = ("My Dir", "My Dir / session");

        var (label, path) = _client.GetSessionLocation(MakeNode());

        Assert.Equal("My Dir", label);
        Assert.Equal("My Dir / session", path);
    }

    // ── Running state ────────────────────────────────────────────────────────────

    [Fact]
    public void IsSessionRunning_DelegatesToRegistry()
    {
        _registry.IsRunningResult = true;
        Assert.True(_client.IsSessionRunning("node-1"));

        _registry.IsRunningResult = false;
        Assert.False(_client.IsSessionRunning("node-1"));
    }

    [Fact]
    public void SetSessionRunning_DelegatesToRegistry()
    {
        _client.SetSessionRunning("node-2", true);

        Assert.Equal("node-2", _registry.SetRunningNodeId);
        Assert.True(_registry.SetRunningValue);
    }

    // ── Turns ────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetTurnLock_ReturnsSameLockAsUnderlyingService()
    {
        var expected = _turnService.GetTurnLock("node-1");

        var result = _client.GetTurnLock("node-1");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task RunTurnAsync_DelegatesToTurnService_AndReturnsResult()
    {
        var node = MakeNode();
        var expected = new TurnResultModel(AssistantText: "reply", SessionId: "sid-1");
        _turnService.RunResult = expected;

        var result = await _client.RunTurnAsync(node, "hello", TurnSource.Scheduled);

        Assert.Same(node, _turnService.LastRunNode);
        Assert.Equal("hello", _turnService.LastRunPrompt);
        Assert.Equal(TurnSource.Scheduled, _turnService.LastRunSource);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CancelTurn_DelegatesToTurnService_WhenRunning_ReturnsTrue()
    {
        _turnService.CancelResult = true;

        var result = _client.CancelTurn("node-1");

        Assert.True(result);
        Assert.Equal("node-1", _turnService.LastCancelNodeId);
    }

    [Fact]
    public void CancelTurn_DelegatesToTurnService_WhenNotRunning_ReturnsFalse()
    {
        _turnService.CancelResult = false;

        var result = _client.CancelTurn("node-1");

        Assert.False(result);
    }

    [Fact]
    public void ResolveProfileName_DelegatesToTurnService()
    {
        var node = MakeNode();
        _turnService.ResolveProfileResult = "MyProfile";

        var result = _client.ResolveProfileName(node);

        Assert.Same(node, _turnService.LastResolveNode);
        Assert.Equal("MyProfile", result);
    }

    [Fact]
    public void ResolveProfileName_ReturnsNull_WhenNoneConfigured()
    {
        _turnService.ResolveProfileResult = null;

        var result = _client.ResolveProfileName(MakeNode());

        Assert.Null(result);
    }

    // ── Schedules ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddSchedule_DelegatesToScheduler()
    {
        var schedule = new ScheduleModel { ScheduleId = "s1", TargetNodeId = "node-1" };

        _client.AddSchedule(schedule);

        Assert.Same(schedule, _scheduler.AddedSchedule);
    }

    [Fact]
    public void RemoveSchedule_DelegatesToScheduler_ReturnsTrue_WhenFound()
    {
        _scheduler.RemoveResult = true;

        var result = _client.RemoveSchedule("s1");

        Assert.True(result);
        Assert.Equal("s1", _scheduler.RemovedScheduleId);
    }

    [Fact]
    public void RemoveSchedule_DelegatesToScheduler_ReturnsFalse_WhenNotFound()
    {
        _scheduler.RemoveResult = false;

        var result = _client.RemoveSchedule("does-not-exist");

        Assert.False(result);
    }

    [Fact]
    public void GetSchedules_DelegatesToScheduler_WithNodeIdFilter()
    {
        var schedule = new ScheduleModel { ScheduleId = "s1", TargetNodeId = "node-1" };
        _client.AddSchedule(schedule);

        var result = _client.GetSchedules("node-1");

        Assert.Equal("node-1", _scheduler.LastGetSchedulesNodeId);
        Assert.Contains(schedule, result);
    }

    [Fact]
    public void GetSchedules_DelegatesToScheduler_WithNullFilter()
    {
        _client.GetSchedules();

        Assert.Null(_scheduler.LastGetSchedulesNodeId);
    }

    // ── Session content ──────────────────────────────────────────────────────────

    [Fact]
    public void ReadEntries_DelegatesToFileService()
    {
        var entries = new List<SessionEntryModel>
        {
            new() { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "hello" },
        };
        _fileService.ReadEntriesResult = entries;

        var result = _client.ReadEntries("sess.txt");

        Assert.Equal("sess.txt", _fileService.LastReadEntriesFile);
        Assert.Same(entries, result);
    }

    [Fact]
    public void SessionFileExists_DelegatesToFileService_ReturnsTrue_WhenExists()
    {
        _fileService.SessionFileExistsResult = true;

        var result = _client.SessionFileExists("sess.txt");

        Assert.True(result);
        Assert.Equal("sess.txt", _fileService.LastFileExistsFile);
    }

    [Fact]
    public void SessionFileExists_DelegatesToFileService_ReturnsFalse_WhenMissing()
    {
        _fileService.SessionFileExistsResult = false;

        var result = _client.SessionFileExists("missing.txt");

        Assert.False(result);
    }

    [Fact]
    public void GetSessionFilePath_DelegatesToFileService()
    {
        _fileService.GetFullPathResult = @"C:\sessions\sess.txt";

        var result = _client.GetSessionFilePath("sess.txt");

        Assert.Equal("sess.txt", _fileService.LastGetFullPathFile);
        Assert.Equal(@"C:\sessions\sess.txt", result);
    }

    // ── Event relay ──────────────────────────────────────────────────────────────

    [Fact]
    public void TreeChanged_FiresWhenRegistryRaisesEvent()
    {
        TreeChangedEventArgs? received = null;
        _client.TreeChanged += (_, e) => received = e;

        var args = new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified };
        _registry.RaiseTreeChanged(args);

        Assert.NotNull(received);
        Assert.Equal(TreeChangeKind.SettingsModified, received!.Kind);
    }

    [Fact]
    public void TreeChanged_RelayesEventArgs_WithSessionAdded()
    {
        TreeChangedEventArgs? received = null;
        _client.TreeChanged += (_, e) => received = e;
        var session = MakeNode();
        var args = new TreeChangedEventArgs
        {
            Kind = TreeChangeKind.SessionAdded,
            Session = session,
        };

        _registry.RaiseTreeChanged(args);

        Assert.NotNull(received);
        Assert.Equal(TreeChangeKind.SessionAdded, received!.Kind);
        Assert.Same(session, received.Session);
    }

    [Fact]
    public void SessionSettingsChanged_FiresWhenRegistryRaisesEvent()
    {
        SessionSettingsChangedEventArgs? received = null;
        _client.SessionSettingsChanged += (_, e) => received = e;

        _registry.RaiseSessionSettingsChanged("node-9");

        Assert.Equal("node-9", received?.NodeId);
    }

    [Fact]
    public void ScheduleChanged_FiresWhenSchedulerRaisesEvent()
    {
        int count = 0;
        _client.ScheduleChanged += (_, _) => count++;

        _scheduler.RaiseScheduleChanged();
        _scheduler.RaiseScheduleChanged();

        Assert.Equal(2, count);
    }

    [Fact]
    public void ScheduleChanged_DoesNotFireBeforeSchedulerRaisesEvent()
    {
        int count = 0;
        _client.ScheduleChanged += (_, _) => count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public void EntryAppended_FiresWhenFileServiceRaisesEvent()
    {
        SessionEntryAppendedEventArgs? received = null;
        _client.EntryAppended += (_, e) => received = e;
        var args = new SessionEntryAppendedEventArgs
        {
            FileName = "f.txt",
            Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "hi" },
        };

        _fileService.RaiseEntryAppended(args);

        Assert.NotNull(received);
        Assert.Equal("f.txt", received!.FileName);
        Assert.Equal("hi", received.Entry.Content);
    }

    [Fact]
    public void SessionIndexed_FiresWhenIndexServiceRaisesEvent()
    {
        SessionIndexedEventArgs? received = null;
        _client.SessionIndexed += (_, e) => received = e;

        _indexService.RaiseSessionIndexed("session-1");

        Assert.NotNull(received);
        Assert.Equal("session-1", received!.ExternalId);
    }

    [Fact]
    public void TurnCompleted_FiresWhenFileServiceRaisesEvent()
    {
        SessionTurnCompletedEventArgs? received = null;
        _client.TurnCompleted += (_, e) => received = e;

        _fileService.NotifyTurnCompleted("f.txt", cancelled: false, errorText: "You've hit your session limit");

        Assert.NotNull(received);
        Assert.Equal("f.txt", received!.FileName);
        Assert.False(received.Cancelled);
        Assert.Equal("You've hit your session limit", received.ErrorText);
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromSettings_DelegatesToRegistry()
    {
        _client.LoadFromSettings();

        Assert.True(_registry.LoadFromSettingsCalled);
    }

    // ── CRUD – Directory ─────────────────────────────────────────────────────────

    [Fact]
    public void AddDirectory_DelegatesToRegistry()
    {
        var expected = new DirectoryNodeModel { Path = "/new-dir" };
        _registry.AddDirectoryResult = expected;

        var result = _client.AddDirectory("/new-dir");

        Assert.Equal("/new-dir", _registry.LastAddDirectoryPath);
        Assert.Same(expected, result);
    }

    [Fact]
    public void TryDeleteDirectory_DelegatesToRegistry()
    {
        var dir = new DirectoryNodeModel { Path = "/dir" };
        _registry.TryDeleteDirectoryResult = true;

        var result = _client.TryDeleteDirectory(dir);

        Assert.True(result);
        Assert.Same(dir, _registry.LastTryDeleteDirectoryArg);
    }

    // ── CRUD – Group ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddGroup_DelegatesToRegistry()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var expected = new GroupNodeModel { Name = "MyGroup" };
        _registry.AddGroupResult = expected;

        var result = _client.AddGroup(parent, "MyGroup");

        Assert.Same(parent, _registry.LastAddGroupParent);
        Assert.Equal("MyGroup", _registry.LastAddGroupName);
        Assert.Same(expected, result);
    }

    [Fact]
    public void AddGroupToGroup_DelegatesToRegistry()
    {
        var parent = new GroupNodeModel { Name = "Parent" };
        var expected = new GroupNodeModel { Name = "Child" };
        _registry.AddGroupToGroupResult = expected;

        var result = _client.AddGroupToGroup(parent, "Child");

        Assert.Same(parent, _registry.LastAddGroupToGroupParent);
        Assert.Equal("Child", _registry.LastAddGroupToGroupName);
        Assert.Same(expected, result);
    }

    [Fact]
    public void TryDeleteGroup_DelegatesToRegistry()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var group = new GroupNodeModel { Name = "Group" };
        _registry.TryDeleteGroupResult = true;

        var result = _client.TryDeleteGroup(parent, group);

        Assert.True(result);
        Assert.Equal((parent, group), _registry.LastTryDeleteGroupArgs);
    }

    [Fact]
    public void TryDeleteGroupFromGroup_DelegatesToRegistry()
    {
        var parent = new GroupNodeModel { Name = "Parent" };
        var group = new GroupNodeModel { Name = "Child" };
        _registry.TryDeleteGroupFromGroupResult = true;

        var result = _client.TryDeleteGroupFromGroup(parent, group);

        Assert.True(result);
        Assert.Equal((parent, group), _registry.LastTryDeleteGroupFromGroupArgs);
    }

    [Fact]
    public void RenameGroup_DelegatesToRegistry()
    {
        var group = new GroupNodeModel { Name = "Old" };

        _client.RenameGroup(group, "New");

        Assert.Equal((group, "New"), _registry.LastRenameGroupArgs);
    }

    // ── CRUD – Session ───────────────────────────────────────────────────────────

    [Fact]
    public void AddSession_DelegatesToRegistry()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var expected = MakeNode("added");
        _registry.AddSessionResult = expected;

        var result = _client.AddSession(parent, "Added");

        Assert.Equal((parent, "Added"), _registry.LastAddSessionArgs);
        Assert.Same(expected, result);
    }

    [Fact]
    public void AddSessionToGroup_DelegatesToRegistry()
    {
        var parent = new GroupNodeModel { Name = "Group" };
        var expected = MakeNode("added-group");
        _registry.AddSessionToGroupResult = expected;

        var result = _client.AddSessionToGroup(parent, "AddedGroup");

        Assert.Equal((parent, "AddedGroup"), _registry.LastAddSessionToGroupArgs);
        Assert.Same(expected, result);
    }

    [Fact]
    public void ImportSession_DelegatesToRegistry()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var expected = MakeNode("imported");
        _registry.ImportSessionResult = expected;

        var result = _client.ImportSession(parent, "Imported", "imported.txt", "claude-sess-1");

        Assert.Equal((parent, "Imported", "imported.txt", "claude-sess-1"), _registry.LastImportSessionArgs);
        Assert.Same(expected, result);
    }

    [Fact]
    public void ImportSessionToGroup_DelegatesToRegistry()
    {
        var parent = new GroupNodeModel { Name = "Group" };
        var expected = MakeNode("imported-group");
        _registry.ImportSessionToGroupResult = expected;

        var result = _client.ImportSessionToGroup(parent, "ImportedGroup", "imported-group.txt", "claude-sess-2");

        Assert.Equal((parent, "ImportedGroup", "imported-group.txt", "claude-sess-2"), _registry.LastImportSessionToGroupArgs);
        Assert.Same(expected, result);
    }

    [Fact]
    public void TryDeleteSession_DelegatesToRegistry_WithDefaultForceDelete()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var session = MakeNode();
        _registry.TryDeleteSessionResult = true;

        var result = _client.TryDeleteSession(parent, session);

        Assert.True(result);
        Assert.Equal((parent, session, false), _registry.LastTryDeleteSessionArgs);
    }

    [Fact]
    public void TryDeleteSession_DelegatesToRegistry_WithForceDeleteTrue()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var session = MakeNode();
        _registry.TryDeleteSessionResult = false;

        var result = _client.TryDeleteSession(parent, session, forceDelete: true);

        Assert.False(result);
        Assert.Equal((parent, session, true), _registry.LastTryDeleteSessionArgs);
    }

    [Fact]
    public void TryDeleteSessionFromGroup_DelegatesToRegistry()
    {
        var parent = new GroupNodeModel { Name = "Group" };
        var session = MakeNode();
        _registry.TryDeleteSessionFromGroupResult = true;

        var result = _client.TryDeleteSessionFromGroup(parent, session, forceDelete: true);

        Assert.True(result);
        Assert.Equal((parent, session, true), _registry.LastTryDeleteSessionFromGroupArgs);
    }

    [Fact]
    public void RenameSession_DelegatesToRegistry()
    {
        var session = MakeNode();

        _client.RenameSession(session, "Renamed");

        Assert.Equal((session, "Renamed"), _registry.LastRenameSessionArgs);
    }

    // ── Model-level move ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSessionFromModel_DelegatesToRegistry()
    {
        var session = MakeNode();
        var expectedParent = new DirectoryNodeModel { Path = "/dir" };
        _registry.RemoveSessionFromModelResult = expectedParent;

        var result = _client.RemoveSessionFromModel(session);

        Assert.Same(session, _registry.LastRemoveSessionFromModelArg);
        Assert.Same(expectedParent, result);
    }

    [Fact]
    public void RemoveSessionFromModel_ReturnsNull_WhenNotFound()
    {
        var session = MakeNode();
        _registry.RemoveSessionFromModelResult = null;

        var result = _client.RemoveSessionFromModel(session);

        Assert.Null(result);
    }

    [Fact]
    public void InsertSessionIntoDirectory_DelegatesToRegistry()
    {
        var parent = new DirectoryNodeModel { Path = "/dir" };
        var session = MakeNode();

        _client.InsertSessionIntoDirectory(parent, session, 2);

        Assert.Equal((parent, session, 2), _registry.LastInsertSessionIntoDirectoryArgs);
    }

    [Fact]
    public void InsertSessionIntoGroup_DelegatesToRegistry()
    {
        var parent = new GroupNodeModel { Name = "Group" };
        var session = MakeNode();

        _client.InsertSessionIntoGroup(parent, session, 3);

        Assert.Equal((parent, session, 3), _registry.LastInsertSessionIntoGroupArgs);
    }

    // ── Orchestration helpers ────────────────────────────────────────────────────

    [Fact]
    public void BuildAgentSessionSummaries_DelegatesToRegistry()
    {
        var expected = new List<SessionSummaryModel>
        {
            new("node-1", "Test", "Dir", "/work", true, true),
        };
        _registry.BuildAgentSessionSummariesResult = expected;

        var result = _client.BuildAgentSessionSummaries();

        Assert.Same(expected, result);
    }

    [Fact]
    public void CollectAllClaudeSessionIds_DelegatesToRegistry()
    {
        var expected = new HashSet<string> { "claude-sess-1", "claude-sess-2" };
        _registry.CollectAllClaudeSessionIdsResult = expected;

        var result = _client.CollectAllClaudeSessionIds();

        Assert.Same(expected, result);
    }

    // ── Cross-project search ─────────────────────────────────────────────────────

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_DelegatesToIndexService_WithProjectSlug()
    {
        var expected = new HashSet<string> { "ext-1" };
        _indexService.MatchingSessionIdsResult = expected;

        var result = await _client.SearchMatchingSessionIdsAsync("Visual Studio", "-Users-x-project-a");

        Assert.Same(expected, result);
        Assert.Equal("Visual Studio", _indexService.LastMatchingIdsQuery);
        Assert.Equal("-Users-x-project-a", _indexService.LastMatchingIdsProjectSlug);
    }

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_DelegatesToIndexService_WithoutProjectSlug()
    {
        await _client.SearchMatchingSessionIdsAsync("Visual Studio");

        Assert.Null(_indexService.LastMatchingIdsProjectSlug);
    }

    [Fact]
    public async Task SearchSessionScoresAsync_DelegatesToIndexService()
    {
        IReadOnlyList<SessionSearchScoreModel> expected = [new SessionSearchScoreModel { ExternalId = "ext-1", StrictMatchCount = 1, LooseMatchCount = 2 }];
        _indexService.ScoresResult = expected;

        var result = await _client.SearchSessionScoresAsync("Visual Studio", "-Users-x-project-a");

        Assert.Same(expected, result);
        Assert.Equal("Visual Studio", _indexService.LastScoresQuery);
        Assert.Equal("-Users-x-project-a", _indexService.LastScoresProjectSlug);
    }

    [Fact]
    public async Task IndexSessionAsync_DelegatesToIndexService()
    {
        _indexService.IndexSessionResult = true;

        var result = await _client.IndexSessionAsync("ext-1", "/Users/x/project-a");

        Assert.True(result);
        Assert.Equal("ext-1", _indexService.LastIndexSessionExternalId);
        Assert.Equal("/Users/x/project-a", _indexService.LastIndexSessionWorkingDirectory);
    }

    // ── Attention Ledger (FR.23.15, FR.23.16) ─────────────────────────────────────

    [Fact]
    public void GetAttentionItems_AndSummary_DelegateToTheAttentionService()   // TC.FV.19-1
    {
        var items = new List<AttentionItemModel> { new() { Id = "A1" }, new() { Id = "A2" } };
        _attention.Items = items;
        _attention.Summary = new AttentionSummaryModel { OpenCount = 2, TopKind = AttentionKind.Ask };

        Assert.Same(items, _client.GetAttentionItems("node-1"));
        Assert.Equal("node-1", _attention.LastItemsNodeId);

        var summary = _client.GetAttentionSummary("node-2");
        Assert.Equal(2, summary.OpenCount);
        Assert.Equal(AttentionKind.Ask, summary.TopKind);
        Assert.Equal("node-2", _attention.LastSummaryNodeId);
    }

    [Fact]
    public void MarkAttentionAnswered_DelegatesToTheAttentionService()   // TC.FV.19-1
    {
        var answers = new List<AttentionAnswerModel> { new() { ItemId = "A1", Title = "t", Answer = "a" } };

        _client.MarkAttentionAnswered("node-1", answers);

        Assert.Equal("node-1", _attention.LastMarkAnswered!.Value.nodeId);
        Assert.Same(answers, _attention.LastMarkAnswered.Value.answers);
    }

    [Fact]
    public void CloseAttentionByUser_ResolvesTheNodeThroughTheRegistryAndDelegates()   // TC.FV.19-1
    {
        _registry.FindByNodeIdResult = MakeNode("node-1");

        _client.CloseAttentionByUser("node-1", "A3");

        Assert.Equal(("node-1", "A3"), _attention.LastCloseByUser);
    }

    [Fact]
    public void CloseAttentionByUser_UnknownNode_DoesNothing()
    {
        _registry.FindByNodeIdResult = null;

        _client.CloseAttentionByUser("ghost", "A3");

        Assert.Null(_attention.LastCloseByUser);
    }

    [Fact]
    public void TakeUnreportedAttentionClosures_DelegatesToTheAttentionService()   // FR.23.11
    {
        var closures = new List<AttentionItemModel> { new() { Id = "A5" } };
        _attention.Closures = closures;

        Assert.Same(closures, _client.TakeUnreportedAttentionClosures("node-1"));
        Assert.Equal("node-1", _attention.LastTakeNodeId);
    }

    [Fact]
    public void AttentionChanged_RelaysTheServiceEventWithTheSameNodeId()   // TC.FV.19-2
    {
        AttentionChangedEventArgs? received = null;
        _client.AttentionChanged += (_, e) => received = e;

        _attention.Raise("node-9");

        Assert.Equal("node-9", received!.NodeId);
    }

    [Fact]
    public void AttentionChanged_AfterDispose_NoLongerFires()   // TC.FV.19-3
    {
        var count = 0;
        _client.AttentionChanged += (_, _) => count++;
        _attention.Raise("node-1");

        _client.Dispose();
        _attention.Raise("node-1");

        Assert.Equal(1, count);
        Assert.Equal(0, _attention.SubscriberCount);
    }

    [Fact]
    public async Task ItemPostedThroughTheMcpServer_IsVisibleThroughTheClientOverTheSameService()   // TC.FV.19-4
    {
        using var env = new AttentionTestEnv();
        var tempDir = Path.Combine(Path.GetTempPath(), $"inproc-attention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var server = new AgentMcpServer(
                new AppSettingsService(tempDir),
                new Lazy<ISessionTurnService>(() => new FakeTurnService()),
                new Lazy<ISchedulerService>(() => new RecordingScheduler()),
                new FakeSessionRegistry(),
                fileService: null!, modelService: null!, tdcpServer: null!, configMerger: null!,
                new FakeTelegramBridge(), new FakeNotificationService(), new FakeDirectoryLabelService(),
                env.Service, TagTestEnv.Unused());
            using var client = new InProcDaemonClient(_registry, _turnService, _scheduler, _fileService, _indexService, env.Service, _tags);
            var changed = new List<string>();
            client.AttentionChanged += (_, e) => changed.Add(e.NodeId);

            var item = new System.Text.Json.Nodes.JsonObject { ["kind"] = "ask", ["title"] = "Shared?" };
            await server.HandleToolCallAsync(new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = "post_attention",
                ["arguments"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["items"] = new System.Text.Json.Nodes.JsonArray(item),
                },
            }, AttentionTestEnv.Node("top"));

            Assert.Equal("Shared?", Assert.Single(client.GetAttentionItems("top")).Title);
            Assert.Equal(1, client.GetAttentionSummary("top").OpenCount);
            Assert.Equal(["top"], changed);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Session Tags (FR.24.8) ──────────────────────────────────────────────────────

    [Fact]
    public void GetSessionTags_ResolvesTheNodeNameThroughTheRegistryAndReturnsTheServiceTags()   // TC.TG.18-1
    {
        var node = MakeNode("node-1");
        node.Name = "HSE-150 spike";
        _registry.FindByNodeIdResult = node;
        var tags = new List<SessionTagModel> { new() { Key = "HSE-150", Label = "HSE-150" } };
        _tags.Tags = tags;

        Assert.Same(tags, _client.GetSessionTags("node-1"));
        Assert.Equal(("node-1", "HSE-150 spike"), _tags.LastGetTags);
    }

    [Fact]
    public void GetSessionTags_UnknownNode_ReturnsAnEmptyList()   // TC.TG.18-1
    {
        _registry.FindByNodeIdResult = null;
        _tags.Tags = new List<SessionTagModel> { new() { Key = "x" } };

        Assert.Empty(_client.GetSessionTags("ghost"));
        Assert.Null(_tags.LastGetTags);
    }

    [Fact]
    public void GetSessionTags_AfterARename_UsesTheNewName()   // TC.TG.18-2
    {
        var node = MakeNode("node-1");
        _registry.FindByNodeIdResult = node;
        _client.GetSessionTags("node-1");
        Assert.Equal("Test", _tags.LastGetTags!.Value.name);

        node.Name = "HSE-165 W14 rebase";
        _client.GetSessionTags("node-1");

        Assert.Equal("HSE-165 W14 rebase", _tags.LastGetTags!.Value.name);
    }

    [Fact]
    public void GetSessionTags_WithTheRealService_ReflectsTheRenamedNameDerivedTags()   // TC.TG.18-2
    {
        using var env = new TagTestEnv();
        env.Store.SaveWorkspaces(new Dictionary<string, string> { ["HSE"] = "hardstop" });
        var node = MakeNode("node-1");
        node.Name = "HSE-150 spike";
        _registry.FindByNodeIdResult = node;
        using var client = new InProcDaemonClient(_registry, _turnService, _scheduler, _fileService, _indexService, _attention, env.Service);

        Assert.Equal(["HSE-150"], client.GetSessionTags("node-1").Select(t => t.Key).ToArray());
        node.Name = "HSE-165 W14 rebase";
        Assert.Equal(["HSE-165"], client.GetSessionTags("node-1").Select(t => t.Key).ToArray());
    }

    [Fact]
    public void TakeTagGuidanceDue_DelegatesAndIsTrueOncePerNode()   // TC.TG.18-3
    {
        Assert.True(_client.TakeTagGuidanceDue("node-1"));
        Assert.False(_client.TakeTagGuidanceDue("node-1"));
        Assert.True(_client.TakeTagGuidanceDue("node-2"));
        Assert.Equal(3, _tags.TakeCalls);
    }

    [Fact]
    public void SessionTagsChanged_RelaysTheServiceEventWithAndWithoutANodeId()   // TC.TG.18-4
    {
        var received = new List<string?>();
        _client.SessionTagsChanged += (_, e) => received.Add(e.NodeId);

        _tags.Raise("node-9");
        _tags.Raise(null);

        Assert.Equal(["node-9", null], received);
    }

    [Fact]
    public void SessionTagsChanged_AfterDispose_NoLongerFires()   // TC.TG.18-5
    {
        var count = 0;
        _client.SessionTagsChanged += (_, _) => count++;
        _tags.Raise("node-1");

        _client.Dispose();
        _tags.Raise("node-1");

        Assert.Equal(1, count);
        Assert.Equal(0, _tags.SubscriberCount);
    }

    // ── Session Tag Index (FR.24.13) ─────────────────────────────────────────────────

    [Fact]
    public void GetSessionTagIndex_SnapshotsTheRegistry_NameDerivedTagsUseTheCurrentName_WorkersRollUp()   // TC.TG.35
    {
        using var env = new TagTestEnv();
        env.Store.SaveWorkspaces(new Dictionary<string, string> { ["HSE"] = "hardstop" });
        _registry.TagIndexSnapshot =
        [
            new("top", "HSE-150 spike", null, @"C:\Proj"),
            new("worker", "HSE-150 child", "top", @"C:\Other"),
        ];
        using var client = new InProcDaemonClient(_registry, _turnService, _scheduler, _fileService, _indexService, _attention, env.Service);

        var usage = Assert.Single(client.GetSessionTagIndex(null));
        Assert.Equal("HSE-150", usage.Tag.Key);
        Assert.Equal(["top"], usage.NodeIds);

        _registry.TagIndexSnapshot = [new("top", "HSE-165 renamed", null, @"C:\Proj")];
        Assert.Equal(["HSE-165"], client.GetSessionTagIndex(null).Select(u => u.Tag.Key).ToArray());
    }

    [Fact]
    public void GetSessionTagIndex_ReadsTagsPerSessionThroughTheTagService_AndAggregatesByKey()   // TC.TG.35
    {
        _registry.TagIndexSnapshot = [new("a", "A", null, @"C:\Proj"), new("b", "B", null, @"C:\Proj")];
        _tags.TagsByNode["a"] = [new() { Key = "k1", Label = "k1" }];
        _tags.TagsByNode["b"] = [new() { Key = "K1", Label = "k1" }, new() { Key = "k2", Label = "k2" }];

        var index = _client.GetSessionTagIndex(null).OrderBy(u => u.Tag.Key, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(2, index.Count);
        Assert.Equal(["a", "b"], index[0].NodeIds);
        Assert.Equal(["b"], index[1].NodeIds);
        Assert.Equal(("b", "B"), _tags.LastGetTags);
    }

    [Fact]
    public void GetSessionTagIndex_ScopesByDirectoryPath_CaseInsensitively()   // TC.TG.35
    {
        _registry.TagIndexSnapshot = [new("a", "A", null, @"C:\Proj"), new("b", "B", null, @"C:\Other")];
        _tags.TagsByNode["a"] = [new() { Key = "ka", Label = "ka" }];
        _tags.TagsByNode["b"] = [new() { Key = "kb", Label = "kb" }];

        var index = _client.GetSessionTagIndex(@"c:\proj");

        Assert.Equal("ka", Assert.Single(index).Tag.Key);
    }
}
