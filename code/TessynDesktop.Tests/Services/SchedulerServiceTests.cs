using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeTurnService : ISessionTurnService
{
    public List<(string nodeId, string prompt)> Calls { get; } = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>What every turn returns.</summary>
    public TurnResultModel Result { get; set; } = new(AssistantText: "ok", SessionId: null);

    /// <summary>The single turn lock every node shares; tests hold it to simulate a turn in progress.</summary>
    public SemaphoreSlim TurnLock => _lock;

    public SemaphoreSlim GetTurnLock(string nodeId) => _lock;

    public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt,
        TurnSource source, CancellationToken cancellationToken = default)
    {
        lock (Calls) Calls.Add((node.NodeId, prompt));
        return Task.FromResult(Result);
    }

    public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt,
        TurnSource source, TurnOptionsModel? options, CancellationToken cancellationToken = default) =>
        RunTurnAsync(node, prompt, source, cancellationToken);

    public bool CancelTurn(string nodeId) => false;
    public string? ResolveProfileName(SessionNodeModel node) => null;
}

internal sealed class FakeSessionRegistry : ISessionRegistry
{
    public event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged { add { } remove { } }
    public void RaiseSessionSettingsChanged(string nodeId) { }
    public DirectoryNodeModel? FindDirectoryByNodeId(string nodeId) => null;
    public GroupNodeModel? FindGroupByNodeId(string nodeId) => null;
    private readonly List<SessionNodeModel> _nodes = new();
    public string? SelectedNodeId { get; set; }

    public event EventHandler<TreeChangedEventArgs> TreeChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<DirectoryNodeModel> Directories => Array.Empty<DirectoryNodeModel>();

    public void AddNode(SessionNodeModel node) { lock (_nodes) _nodes.Add(node); }

    public SessionNodeModel? FindModelByNodeId(string nodeId)
    {
        lock (_nodes) return _nodes.FirstOrDefault(n => n.NodeId == nodeId);
    }

    public SessionNodeModel? FindModelByAgentToken(string token) => null;
    public SessionNodeModel? FindModelByFileName(string fileName) => null;
    public void LoadFromSettings() { }

    public DirectoryNodeModel AddDirectory(string path) => throw new NotImplementedException();
    public bool TryDeleteDirectory(DirectoryNodeModel directory) => false;
    public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name) => throw new NotImplementedException();
    public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name) => throw new NotImplementedException();
    public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group) => false;
    public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group) => false;
    public void RenameGroup(GroupNodeModel group, string newName) { }
    public SessionPlacementModel? CreateSessionAfter(string anchorNodeId, string name) => throw new NotImplementedException();
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
    public bool MoveSession(SessionNodeModel session, DirectoryNodeModel targetDirectory, GroupNodeModel? targetGroup, int index) => throw new System.NotSupportedException();
    public void RaiseTreeModified() { }
    public SessionNodeModel? CreateSessionForAgent(string workingDir, string name, string? group, int orchestrationDepth = 0, string? supervisorNodeId = null) => null;
    public List<SessionSummaryModel> BuildAgentSessionSummaries() => new();
    public IReadOnlyList<SessionSnapshotModel> SnapshotSessionsForListing() => new List<SessionSnapshotModel>();
    public List<SessionTagIndexSessionModel> SnapshotSessionsForTagIndex() => new();
    public SessionListPageModel QueryAgentSessions(SessionListQueryModel query, string callerNodeId) =>
        new() { Scope = query.Scope, Sections = Array.Empty<SessionListSectionModel>(), Rows = Array.Empty<SessionListRowModel>(), TotalRows = 0, Offset = 0, Limit = query.Limit };
    public NodeIdResolutionModel ResolveNodeId(string idOrPrefix)
    {
        lock (_nodes)
        {
            var exact = _nodes.FirstOrDefault(n => n.NodeId == idOrPrefix);
            return exact != null
                ? new NodeIdResolutionModel { Status = NodeIdResolutionStatus.Resolved, NodeId = exact.NodeId }
                : new NodeIdResolutionModel { Status = NodeIdResolutionStatus.NotFound };
        }
    }
    public IReadOnlySet<string> CollectAllClaudeSessionIds() => new HashSet<string>();
    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => ("", "");
    public void SetSessionRunning(string nodeId, bool isRunning) { }
    public bool IsSessionRunning(string nodeId) => false;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class SchedulerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettingsService _appSettings;
    private readonly FakeTurnService _turnService;
    private readonly FakeSessionRegistry _registry;
    private SchedulerService? _service;

    public SchedulerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cmx-sched-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _appSettings   = new AppSettingsService(_tempDir);
        _turnService   = new FakeTurnService();
        _registry      = new FakeSessionRegistry();
    }

    private SchedulerService BuildService() =>
        _service = new SchedulerService(_appSettings, _turnService, _registry);

    public void Dispose()
    {
        _service?.Stop();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SessionNodeModel MakeNode(string nodeId) =>
        new() { NodeId = nodeId, Name = $"Node-{nodeId}", FileName = $"{nodeId}.txt" };

    private static ScheduleModel OneShotSchedule(string nodeId, DateTimeOffset fireAt) =>
        new()
        {
            ScheduleId    = Guid.NewGuid().ToString("N"),
            TargetNodeId  = nodeId,
            Kind          = ScheduleKind.At,
            FireAtUtc     = fireAt,
            Prompt        = "hello",
        };

    private static ScheduleModel CronSchedule(string nodeId, string cron,
        DateTimeOffset? fireAt = null, MissedFirePolicy policy = MissedFirePolicy.FireOnce) =>
        new()
        {
            ScheduleId       = Guid.NewGuid().ToString("N"),
            TargetNodeId     = nodeId,
            Kind             = ScheduleKind.Cron,
            CronExpression   = cron,
            FireAtUtc        = fireAt,
            MissedFirePolicy = policy,
            Prompt           = "tick",
        };

    // ── Test 1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AddSchedule_OneShot_AddsToListAndFiresScheduleChanged()
    {
        var svc = BuildService();
        int events = 0;
        svc.ScheduleChanged += (_, _) => events++;

        var fireAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var schedule = OneShotSchedule("node-1", fireAt);

        svc.AddSchedule(schedule);

        Assert.Single(_appSettings.Settings.Schedules);
        Assert.Equal(schedule.ScheduleId, _appSettings.Settings.Schedules[0].ScheduleId);
        Assert.Equal(fireAt, _appSettings.Settings.Schedules[0].FireAtUtc);
        Assert.Equal(1, events);
    }

    // ── Test 2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AddSchedule_Cron_WithNullFireAt_ComputesFutureFireAtUtc()
    {
        var svc = BuildService();
        var schedule = CronSchedule("node-1", "* * * * *"); // every minute, FireAtUtc = null

        svc.AddSchedule(schedule);

        var saved = _appSettings.Settings.Schedules.Single();
        Assert.NotNull(saved.FireAtUtc);
        Assert.True(saved.FireAtUtc!.Value > DateTimeOffset.UtcNow,
            "FireAtUtc should be in the future after cron calculation");
    }

    // ── Test 3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSchedule_Existing_ReturnsTrueAndRemovesFromList()
    {
        var svc = BuildService();
        var schedule = OneShotSchedule("node-1", DateTimeOffset.UtcNow.AddMinutes(5));
        svc.AddSchedule(schedule);

        var result = svc.RemoveSchedule(schedule.ScheduleId);

        Assert.True(result);
        Assert.Empty(_appSettings.Settings.Schedules);
    }

    // ── Test 4 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSchedule_Missing_ReturnsFalse()
    {
        var svc = BuildService();

        var result = svc.RemoveSchedule("does-not-exist");

        Assert.False(result);
    }

    // ── Test 5 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSchedules_FilteredByNodeId_ReturnsOnlyMatchingSchedules()
    {
        var svc = BuildService();
        svc.AddSchedule(OneShotSchedule("node-A", DateTimeOffset.UtcNow.AddMinutes(5)));
        svc.AddSchedule(OneShotSchedule("node-A", DateTimeOffset.UtcNow.AddMinutes(10)));
        svc.AddSchedule(OneShotSchedule("node-B", DateTimeOffset.UtcNow.AddMinutes(5)));

        var forA = svc.GetSchedules("node-A");
        var forB = svc.GetSchedules("node-B");

        Assert.Equal(2, forA.Count);
        Assert.Single(forB);
        Assert.All(forA, s => Assert.Equal("node-A", s.TargetNodeId));
    }

    // ── Test 6 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleMissedFires_FireOncePolicy_FiresTurnOnTargetNode()
    {
        const string nodeId = "node-fire";
        _registry.AddNode(MakeNode(nodeId));

        var svc = BuildService();
        var pastFireAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var schedule = new ScheduleModel
        {
            ScheduleId       = "missed-fire",
            TargetNodeId     = nodeId,
            Kind             = ScheduleKind.At,
            FireAtUtc        = pastFireAt,
            Prompt           = "do something",
            MissedFirePolicy = MissedFirePolicy.FireOnce,
        };
        _appSettings.Settings.Schedules.Add(schedule);

        svc.Start();
        await Task.Delay(200); // let the async fire complete

        Assert.Contains(_turnService.Calls, c => c.nodeId == nodeId);
    }

    // ── Test 7 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleMissedFires_SkipPolicy_AdvancesFireAtUtcWithoutFiringTurn()
    {
        const string nodeId = "node-skip";
        _registry.AddNode(MakeNode(nodeId));

        var svc = BuildService();
        var pastFireAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        // Yearly cron (not "* * * * *"): the advanced FireAtUtc is months away, so the poll timer that Start()
        // arms can never find it due, whatever the wall clock does (a per-minute cron could cross a minute
        // boundary mid-test and legitimately fire). Skip handling runs synchronously inside Start(), so no
        // sleep is needed before asserting.
        var schedule = CronSchedule(nodeId, "0 0 1 1 *", pastFireAt, MissedFirePolicy.Skip);
        _appSettings.Settings.Schedules.Add(schedule);

        svc.Start();
        await Task.Yield();

        var saved = _appSettings.Settings.Schedules.FirstOrDefault(s => s.ScheduleId == schedule.ScheduleId);

        // The schedule should still exist (not removed) with an updated future FireAtUtc.
        Assert.NotNull(saved);
        Assert.True(saved!.FireAtUtc!.Value > DateTimeOffset.UtcNow,
            "FireAtUtc should be advanced to a future time by skip policy");

        // No turn should have been fired for this node.
        Assert.DoesNotContain(_turnService.Calls, c => c.nodeId == nodeId);
    }

    // ── Test 8 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CronRearm_AfterFire_AdvancesFireAtUtcAndIncrementsFireCount()
    {
        const string nodeId = "node-cron-rearm";
        _registry.AddNode(MakeNode(nodeId));

        var svc = BuildService();
        // Set FireAtUtc in the past so it fires immediately on HandleMissedFires.
        var pastFireAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var schedule = CronSchedule(nodeId, "* * * * *", pastFireAt);
        _appSettings.Settings.Schedules.Add(schedule);

        svc.Start();
        await Task.Delay(300);

        var saved = _appSettings.Settings.Schedules.FirstOrDefault(s => s.ScheduleId == schedule.ScheduleId);

        // Cron schedules stay in the list after firing (re-armed).
        Assert.NotNull(saved);
        Assert.True(saved!.FireAtUtc!.Value > DateTimeOffset.UtcNow,
            "Cron schedule should re-arm with a future FireAtUtc after firing");
        Assert.True(saved.FireCount >= 1, "FireCount should be incremented after firing");
    }

    // ── Test 9 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneShotRemoved_AfterFire_ScheduleIsRemovedFromList()
    {
        const string nodeId = "node-oneshot-remove";
        _registry.AddNode(MakeNode(nodeId));

        var svc = BuildService();
        var pastFireAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var schedule = OneShotSchedule(nodeId, pastFireAt);
        _appSettings.Settings.Schedules.Add(schedule);

        svc.Start();
        await Task.Delay(300);

        var remaining = _appSettings.Settings.Schedules
            .FirstOrDefault(s => s.ScheduleId == schedule.ScheduleId);

        Assert.Null(remaining);
    }

    // ── TC.21.17: no completion notification ──────────────────────────────────

    [Fact]
    public async Task FireSchedule_CompletedTurn_RunsTurnWithoutAnyNotification()
    {
        // SchedulerService no longer has a notification dependency; a finished turn just runs and the
        // one-shot schedule is removed as before.
        const string nodeId = "node-no-notify";
        _registry.AddNode(MakeNode(nodeId));
        var svc = BuildService();
        var schedule = OneShotSchedule(nodeId, DateTimeOffset.UtcNow.AddMinutes(-1));
        _appSettings.Settings.Schedules.Add(schedule);

        svc.Start();
        await Task.Delay(300);

        Assert.Contains(_turnService.Calls, c => c.nodeId == nodeId);
        Assert.DoesNotContain(_appSettings.Settings.Schedules, s => s.ScheduleId == schedule.ScheduleId);
    }

    // ── TC.15.1: mailbox coalescing ───────────────────────────────────────────

    [Fact]
    public void AddOrMergeMailboxDelivery_NoPendingDelivery_AddsDelayZeroMailboxSchedule()
    {
        var svc = BuildService();

        svc.AddOrMergeMailboxDelivery("sup", "W1", "result one");

        var s = Assert.Single(_appSettings.Settings.Schedules);
        Assert.Equal("sup", s.TargetNodeId);
        Assert.True(s.IsMailboxDelivery);
        Assert.Equal(ScheduleKind.Delay, s.Kind);
        Assert.Equal("Worker 'W1' result", s.Note);
        Assert.Equal("[Worker finished] W1\nresult one", s.Prompt);
    }

    [Fact]
    public void AddOrMergeMailboxDelivery_PendingDeliveryExists_AppendsResultAndUpdatesNote()
    {
        var svc = BuildService();
        svc.AddOrMergeMailboxDelivery("sup", "W1", "one");
        svc.AddOrMergeMailboxDelivery("sup", "W2", "two");

        var s = Assert.Single(_appSettings.Settings.Schedules);
        Assert.Equal("[Worker finished] W1\none\n\n[Worker finished] W2\ntwo", s.Prompt);
        Assert.Equal("Worker results (2): W1, W2", s.Note);

        svc.AddOrMergeMailboxDelivery("sup", "W3", "three");
        Assert.Equal("Worker results (3): W1, W2, W3", Assert.Single(_appSettings.Settings.Schedules).Note);
    }

    [Fact]
    public void AddOrMergeMailboxDelivery_DifferentSupervisors_NotMerged()
    {
        var svc = BuildService();
        svc.AddOrMergeMailboxDelivery("sup-a", "W1", "one");
        svc.AddOrMergeMailboxDelivery("sup-b", "W2", "two");

        Assert.Equal(2, _appSettings.Settings.Schedules.Count);
        Assert.All(_appSettings.Settings.Schedules, s => Assert.Equal(1, CountOccurrences(s.Prompt, "[Worker finished]")));
    }

    [Fact]
    public async Task AddOrMergeMailboxDelivery_ConcurrentCalls_NoResultLost()
    {
        const int n = 50;
        var svc = BuildService();

        await Task.WhenAll(Enumerable.Range(0, n).Select(i =>
            Task.Run(() => svc.AddOrMergeMailboxDelivery("sup", $"W{i}", $"text-{i}"))));

        var all = string.Join("\n", _appSettings.Settings.Schedules.Select(s => s.Prompt));
        for (var i = 0; i < n; i++)
            Assert.Equal(1, CountOccurrences(all, $"[Worker finished] W{i}\ntext-{i}"));
    }

    /// <summary>ISSUE.013 P3: a refused scheduled turn is not lost - the supervisor gets a mailbox note.</summary>
    [Fact]
    public async Task ScheduledTurnRefused_NotifiesTheSupervisorThroughTheMailbox()
    {
        var supervisor = MakeNode("sup-1");
        var worker = MakeNode("worker-1");
        worker.SupervisorNodeId = "sup-1";
        _registry.AddNode(supervisor);
        _registry.AddNode(worker);
        _turnService.Result = new TurnResultModel(string.Empty, null, IsError: true, ErrorMessage: "Session 'Node-worker-1' is pinned to profile 'Retired', which no longer exists.", Refused: true);
        var svc = BuildService();
        _appSettings.Settings.Schedules.Add(new ScheduleModel
        {
            ScheduleId = "due", TargetNodeId = "worker-1", Kind = ScheduleKind.At,
            FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), Prompt = "do something", MissedFirePolicy = MissedFirePolicy.FireOnce,
        });

        svc.Start();
        // The note is either still a pending mailbox delivery or, once Poll fired it, the prompt of the supervisor's turn.
        string? mail = null;
        await WaitForAsync(() =>
        {
            mail = _appSettings.Settings.Schedules.ToList().FirstOrDefault(s => s.IsMailboxDelivery && s.TargetNodeId == "sup-1")?.Prompt
                   ?? _turnService.Calls.ToList().Where(c => c.nodeId == "sup-1").Select(c => c.prompt).FirstOrDefault();
            return mail != null;
        });

        Assert.Contains("Scheduled turn was refused", mail);
        Assert.Contains("'Retired'", mail);
    }

    [Fact]
    public async Task ScheduledTurnRefused_ForATopLevelSession_IsOnlyLogged_NoMailboxNote()
    {
        _registry.AddNode(MakeNode("top-1"));
        _turnService.Result = new TurnResultModel(string.Empty, null, IsError: true, ErrorMessage: "refused", Refused: true);
        var svc = BuildService();
        _appSettings.Settings.Schedules.Add(new ScheduleModel
        {
            ScheduleId = "due", TargetNodeId = "top-1", Kind = ScheduleKind.At,
            FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), Prompt = "x", MissedFirePolicy = MissedFirePolicy.FireOnce,
        });

        svc.Start();
        await WaitForAsync(() => _turnService.Calls.Count > 0);
        await Task.Delay(100);

        Assert.DoesNotContain(_appSettings.Settings.Schedules, s => s.IsMailboxDelivery);
    }

    [Fact]
    public async Task Poll_DueMailboxDelivery_TargetBusy_StaysPendingAndRunsNoTurn()
    {
        const string nodeId = "node-busy";
        _registry.AddNode(MakeNode(nodeId));
        var svc = BuildService();
        svc.AddOrMergeMailboxDelivery(nodeId, "W1", "one");

        await _turnService.TurnLock.WaitAsync();
        try
        {
            svc.Poll();
            await Task.Delay(300);

            Assert.Single(_appSettings.Settings.Schedules);
            Assert.Empty(_turnService.Calls);
        }
        finally
        {
            _turnService.TurnLock.Release();
        }
    }

    [Fact]
    public async Task Poll_DueMailboxDelivery_TargetIdle_FiresOneTurnWithAllMergedResults()
    {
        const string nodeId = "node-merge-fire";
        _registry.AddNode(MakeNode(nodeId));
        var svc = BuildService();
        svc.AddOrMergeMailboxDelivery(nodeId, "W1", "one");
        svc.AddOrMergeMailboxDelivery(nodeId, "W2", "two");

        await _turnService.TurnLock.WaitAsync();
        svc.Poll();
        await Task.Delay(300);
        Assert.Empty(_turnService.Calls);
        Assert.Single(_appSettings.Settings.Schedules);
        _turnService.TurnLock.Release();
        svc.Poll();

        await WaitForAsync(() => _turnService.Calls.Count >= 1);
        await Task.Delay(200);

        var call = Assert.Single(_turnService.Calls);
        Assert.Contains("[Worker finished] W1\none", call.prompt);
        Assert.Contains("[Worker finished] W2\ntwo", call.prompt);
        Assert.True(call.prompt.IndexOf("W1", StringComparison.Ordinal) < call.prompt.IndexOf("W2", StringComparison.Ordinal));
        Assert.Contains("Worker results (2): W1, W2", call.prompt);
        Assert.Empty(_appSettings.Settings.Schedules);
    }

    [Fact]
    public async Task Poll_ResultArrivingAfterDeliveryFired_StartsNewDelivery()
    {
        const string nodeId = "node-after-fire";
        _registry.AddNode(MakeNode(nodeId));
        var svc = BuildService();
        svc.AddOrMergeMailboxDelivery(nodeId, "W1", "one");

        svc.Poll();
        await WaitForAsync(() => _turnService.Calls.Count >= 1);
        await WaitForAsync(() => _appSettings.Settings.Schedules.Count == 0);

        svc.AddOrMergeMailboxDelivery(nodeId, "W2", "two");

        var s = Assert.Single(_appSettings.Settings.Schedules);
        Assert.Equal("Worker 'W2' result", s.Note);
        Assert.DoesNotContain("W1", s.Prompt);
    }

    [Fact]
    public async Task Poll_OrdinaryDueSchedule_TargetBusy_StillFiresAndWaitsOnLock()
    {
        const string nodeId = "node-ordinary-busy";
        _registry.AddNode(MakeNode(nodeId));
        var svc = BuildService();
        var schedule = OneShotSchedule(nodeId, DateTimeOffset.UtcNow.AddMinutes(-1));
        _appSettings.Settings.Schedules.Add(schedule);

        await _turnService.TurnLock.WaitAsync();
        try
        {
            svc.Poll();
            // FR.14.9: a non-mailbox schedule is claimed (removed) immediately even though the target is busy.
            await WaitForAsync(() => _appSettings.Settings.Schedules.Count == 0);
            Assert.DoesNotContain(_appSettings.Settings.Schedules, s => s.ScheduleId == schedule.ScheduleId);
        }
        finally
        {
            _turnService.TurnLock.Release();
        }
    }

    [Fact]
    public void ScheduleModel_IsMailboxDelivery_PersistsThroughSaveAndLoad()
    {
        var svc = BuildService();
        svc.AddOrMergeMailboxDelivery("sup", "W1", "one");
        _appSettings.Save();

        var reloaded = new AppSettingsService(_tempDir);
        reloaded.Load();

        var s = Assert.Single(reloaded.Settings.Schedules);
        Assert.True(s.IsMailboxDelivery);
    }

    [Fact]
    public void ScheduleModel_LegacyScheduleWithoutField_LoadsAsOrdinarySchedule()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(OneShotSchedule("n", DateTimeOffset.UtcNow));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove(nameof(ScheduleModel.IsMailboxDelivery));

        var loaded = System.Text.Json.JsonSerializer.Deserialize<ScheduleModel>(node.ToJsonString());

        Assert.NotNull(loaded);
        Assert.False(loaded!.IsMailboxDelivery);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(25);
    }
}
