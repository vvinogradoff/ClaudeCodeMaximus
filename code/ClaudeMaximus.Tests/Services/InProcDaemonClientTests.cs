using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using ClaudeMaximus.Models.Agent;
using ClaudeMaximus.Services;
using Xunit;

namespace ClaudeMaximus.Tests.Services;

// ── Fakes ──────────────────────────────────────────────────────────────────────

internal sealed class InProcFakeRegistry : ISessionRegistry
{
    public event EventHandler<TreeChangedEventArgs> TreeChanged = delegate { };

    public string? SelectedNodeId { get; set; }
    public IReadOnlyList<DirectoryNodeModel> Directories { get; set; } = Array.Empty<DirectoryNodeModel>();

    public SessionNodeModel? FindByNodeIdResult { get; set; }
    public SessionNodeModel? FindByAgentTokenResult { get; set; }
    public (string directoryLabel, string treePath) LocationResult { get; set; } = ("dir", "path");
    public bool IsRunningResult { get; set; }
    public string? SetRunningNodeId { get; private set; }
    public bool? SetRunningValue { get; private set; }

    public void RaiseTreeChanged(TreeChangedEventArgs e) => TreeChanged.Invoke(this, e);

    public SessionNodeModel? FindModelByNodeId(string nodeId) => FindByNodeIdResult;
    public SessionNodeModel? FindModelByAgentToken(string token) => FindByAgentTokenResult;
    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => LocationResult;
    public bool IsSessionRunning(string nodeId) => IsRunningResult;
    public void SetSessionRunning(string nodeId, bool isRunning) { SetRunningNodeId = nodeId; SetRunningValue = isRunning; }
    public void LoadFromSettings() { }
    public DirectoryNodeModel AddDirectory(string path) => throw new NotImplementedException();
    public bool TryDeleteDirectory(DirectoryNodeModel directory) => false;
    public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name) => throw new NotImplementedException();
    public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name) => throw new NotImplementedException();
    public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group) => false;
    public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group) => false;
    public void RenameGroup(GroupNodeModel group, string newName) { }
    public SessionNodeModel AddSession(DirectoryNodeModel parent, string name) => throw new NotImplementedException();
    public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name) => throw new NotImplementedException();
    public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId) => throw new NotImplementedException();
    public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId) => throw new NotImplementedException();
    public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false) => false;
    public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false) => false;
    public void RenameSession(SessionNodeModel session, string newName) { }
    public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session) => null;
    public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index) { }
    public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index) { }
    public SessionNodeModel? CreateSessionForAgent(string workingDir, string name, string? group, int orchestrationDepth = 0, string? supervisorNodeId = null) => null;
    public List<SessionSummaryModel> BuildAgentSessionSummaries() => new();
    public IReadOnlySet<string> CollectAllClaudeSessionIds() => new HashSet<string>();
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

// ── Tests ───────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class InProcDaemonClientTests
{
    private readonly InProcFakeRegistry _registry = new();
    private readonly InProcFakeTurnService _turnService = new();
    private readonly InProcFakeScheduler _scheduler = new();
    private readonly InProcFakeFileService _fileService = new();
    private readonly InProcDaemonClient _client;

    public InProcDaemonClientTests()
    {
        _client = new InProcDaemonClient(_registry, _turnService, _scheduler, _fileService);
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
}
