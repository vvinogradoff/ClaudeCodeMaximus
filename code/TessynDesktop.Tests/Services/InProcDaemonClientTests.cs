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
    public SessionNodeModel? FindModelByFileName(string fileName) => FindByFileNameResult;
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
    public bool RemoveSchedule(string scheduleId) { RemovedScheduleId = scheduleId; return RemoveResult; }

    public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null)
    {
        LastGetSchedulesNodeId = targetNodeId;
        return _schedules;
    }
}

internal sealed class InProcFakeFileService : ISessionFileService
{
    public IReadOnlyList<SessionEntryModel> ReadEntriesResult { get; set; } = Array.Empty<SessionEntryModel>();
    public bool SessionFileExistsResult { get; set; }
    public string GetFullPathResult { get; set; } = "/path/to/file.txt";

    public string? LastReadEntriesFile { get; private set; }
    public string? LastFileExistsFile { get; private set; }
    public string? LastGetFullPathFile { get; private set; }

    public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;
    public void RaiseEntryAppended(SessionEntryAppendedEventArgs e) => EntryAppended?.Invoke(this, e);

    public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) { LastReadEntriesFile = fileName; return ReadEntriesResult; }
    public bool SessionFileExists(string fileName) { LastFileExistsFile = fileName; return SessionFileExistsResult; }
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
    public IReadOnlyList<SessionSearchResultModel> SearchResult { get; set; } = Array.Empty<SessionSearchResultModel>();
    public IReadOnlyDictionary<string, string> EnsureTitlesResult { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<ClaudeSessionSummaryModel> SummariesResult { get; set; } = Array.Empty<ClaudeSessionSummaryModel>();

    public string? LastSearchQuery { get; private set; }
    public string? LastSearchProjectSlug { get; private set; }
    public string? LastSearchRole { get; private set; }
    public int? LastSearchLimit { get; private set; }
    public int? LastSearchOffset { get; private set; }
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
    private readonly InProcDaemonClient _client;

    public InProcDaemonClientTests()
    {
        _client = new InProcDaemonClient(_registry, _turnService, _scheduler, _fileService, _indexService);
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
            new("node-1", "Test", "Dir", "/work", true, true, "hello"),
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
}
