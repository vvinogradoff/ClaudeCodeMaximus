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

    public SemaphoreSlim GetTurnLock(string nodeId) => _lock;

    public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt,
        TurnSource source, CancellationToken cancellationToken = default)
    {
        lock (Calls) Calls.Add((node.NodeId, prompt));
        return Task.FromResult(new TurnResultModel(AssistantText: "ok", SessionId: null));
    }

    public bool CancelTurn(string nodeId) => false;
    public string? ResolveProfileName(SessionNodeModel node) => null;
}

internal sealed class FakeSessionRegistry : ISessionRegistry
{
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
    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => ("", "");
    public void SetSessionRunning(string nodeId, bool isRunning) { }
    public bool IsSessionRunning(string nodeId) => false;
}

internal sealed class FakeNotificationService : INotificationService
{
    public void ShowResult(string nodeId, string sessionName, string assistantText, string? note) { }
    public void RegisterActivationHandler(Action<string> onNodeIdActivated) { }
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class SchedulerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettingsService _appSettings;
    private readonly FakeTurnService _turnService;
    private readonly FakeSessionRegistry _registry;
    private readonly FakeNotificationService _notifications;
    private SchedulerService? _service;

    public SchedulerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cmx-sched-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _appSettings   = new AppSettingsService(_tempDir);
        _turnService   = new FakeTurnService();
        _registry      = new FakeSessionRegistry();
        _notifications = new FakeNotificationService();
    }

    private SchedulerService BuildService() =>
        _service = new SchedulerService(_appSettings, _turnService, _registry, _notifications);

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
        var schedule = CronSchedule(nodeId, "* * * * *", pastFireAt, MissedFirePolicy.Skip);
        _appSettings.Settings.Schedules.Add(schedule);

        svc.Start();
        await Task.Delay(200);

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
}
