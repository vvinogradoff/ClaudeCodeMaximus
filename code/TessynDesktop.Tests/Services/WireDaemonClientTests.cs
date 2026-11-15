using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Wire fake ──────────────────────────────────────────────────────────────────
// Implements IDaemonClient server-side: records calls and allows raising events.

internal sealed class WireFakeDaemonClient : IDaemonClient
{
    public event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged { add { } remove { } }
    // ── Configurable results ───────────────────────────────────────────────────

    public IReadOnlyList<DirectoryNodeModel> DirectoriesValue { get; set; } = Array.Empty<DirectoryNodeModel>();
    public string? SelectedNodeIdValue { get; set; }
    public SessionNodeModel? FindByNodeIdResult { get; set; }
    public SessionNodeModel? FindByTokenResult { get; set; }
    public SessionNodeModel? FindByFileNameResult { get; set; }
    public (string directoryLabel, string treePath) LocationResult { get; set; } = ("dir", "path");
    public bool IsRunningResult { get; set; }
    public TurnResultModel RunResult { get; set; } = new(AssistantText: "ok", SessionId: null);
    public bool CancelResult { get; set; }
    public string? ResolveProfileResult { get; set; }
    public bool RemoveScheduleResult { get; set; } = true;
    public IReadOnlyList<SessionEntryModel> ReadEntriesResult { get; set; } = Array.Empty<SessionEntryModel>();
    public bool SessionFileExistsResult { get; set; }
    public string GetFullPathResult { get; set; } = "/path/to/file.txt";

    // ── Recorded calls ─────────────────────────────────────────────────────────

    public string? LastSetSelectedNodeId { get; private set; }
    public string? LastFindByNodeIdArg { get; private set; }
    public string? LastFindByTokenArg { get; private set; }
    public string? LastFindByFileNameArg { get; private set; }
    public SessionNodeModel? LastLocationNode { get; private set; }
    public string? LastIsRunningNodeId { get; private set; }
    public string? LastSetRunningNodeId { get; private set; }
    public bool? LastSetRunningValue { get; private set; }
    public SessionNodeModel? LastRunNode { get; private set; }
    public string? LastRunPrompt { get; private set; }
    public TurnSource? LastRunSource { get; private set; }
    public string? LastCancelNodeId { get; private set; }
    public SessionNodeModel? LastResolveNode { get; private set; }
    public string? LastGetSchedulesNodeId { get; private set; }
    public ScheduleModel? AddedSchedule { get; private set; }
    public string? RemovedScheduleId { get; private set; }
    public string? LastReadEntriesFile { get; private set; }
    public string? LastFileExistsFile { get; private set; }
    public string? LastGetPathFile { get; private set; }

    private readonly List<ScheduleModel> _schedules = new();

    // ── Event raising ──────────────────────────────────────────────────────────

    private EventHandler<TreeChangedEventArgs>? _treeChanged;
    private EventHandler?                       _scheduleChanged;

    /// <summary>Number of server-side SSE streams subscribed (a stream subscribes to ScheduleChanged last, before it starts writing).</summary>
    public int SseSubscribers
    {
        get { lock (this) return _scheduleChanged?.GetInvocationList().Length ?? 0; }
    }

    public event EventHandler<TreeChangedEventArgs>? TreeChanged
    {
        add { lock (this) _treeChanged += value; }
        remove { lock (this) _treeChanged -= value; }
    }

    public event EventHandler? ScheduleChanged
    {
        add { lock (this) _scheduleChanged += value; }
        remove { lock (this) _scheduleChanged -= value; }
    }
    public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;
    public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }
    public event EventHandler<SessionIndexedEventArgs>? SessionIndexed { add { } remove { } }
    public event EventHandler<AttentionChangedEventArgs>? AttentionChanged { add { } remove { } }
    public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged { add { } remove { } }

    public void RaiseTreeChanged(TreeChangedEventArgs e) => _treeChanged?.Invoke(this, e);
    public void RaiseScheduleChanged()                   => _scheduleChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseEntryAppended(SessionEntryAppendedEventArgs e) => EntryAppended?.Invoke(this, e);

    // ── IDaemonClient ──────────────────────────────────────────────────────────

    public IReadOnlyList<DirectoryNodeModel> Directories => DirectoriesValue;

    public string? SelectedNodeId
    {
        get => SelectedNodeIdValue;
        set { LastSetSelectedNodeId = value; SelectedNodeIdValue = value; }
    }

    public SessionNodeModel? FindSessionByNodeId(string nodeId)
    {
        LastFindByNodeIdArg = nodeId;
        return FindByNodeIdResult;
    }

    public SessionNodeModel? FindSessionByAgentToken(string token)
    {
        LastFindByTokenArg = token;
        return FindByTokenResult;
    }

    public SessionNodeModel? FindSessionByFileName(string fileName)
    {
        LastFindByFileNameArg = fileName;
        return FindByFileNameResult;
    }

    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session)
    {
        LastLocationNode = session;
        return LocationResult;
    }

    public bool IsSessionRunning(string nodeId)
    {
        LastIsRunningNodeId = nodeId;
        return IsRunningResult;
    }

    public void SetSessionRunning(string nodeId, bool isRunning)
    {
        LastSetRunningNodeId = nodeId;
        LastSetRunningValue  = isRunning;
    }

    public SemaphoreSlim GetTurnLock(string nodeId) =>
        throw new NotSupportedException("Fake does not implement GetTurnLock.");

    public Task<TurnResultModel> RunTurnAsync(
        SessionNodeModel  node,
        string            prompt,
        TurnSource        source,
        CancellationToken cancellationToken = default)
    {
        LastRunNode   = node;
        LastRunPrompt = prompt;
        LastRunSource = source;
        return Task.FromResult(RunResult);
    }

    public bool CancelTurn(string nodeId)
    {
        LastCancelNodeId = nodeId;
        return CancelResult;
    }

    public string? ResolveProfileName(SessionNodeModel node)
    {
        LastResolveNode = node;
        return ResolveProfileResult;
    }

    public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null)
    {
        LastGetSchedulesNodeId = targetNodeId;
        return _schedules;
    }

    public void AddSchedule(ScheduleModel schedule)
    {
        AddedSchedule = schedule;
        _schedules.Add(schedule);
    }

    public bool RemoveSchedule(string scheduleId)
    {
        RemovedScheduleId = scheduleId;
        return RemoveScheduleResult;
    }

    public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName)
    {
        LastReadEntriesFile = fileName;
        return ReadEntriesResult;
    }

    public bool SessionFileExists(string fileName)
    {
        LastFileExistsFile = fileName;
        return SessionFileExistsResult;
    }

    public string GetSessionFilePath(string fileName)
    {
        LastGetPathFile = fileName;
        return GetFullPathResult;
    }

    // ── Tree CRUD (Stage 5b) ───────────────────────────────────────────────────────
    // Not exercised over the wire yet (WireDaemonClient throws before reaching the server);
    // these only need to exist so the fake satisfies IDaemonClient.

    public void LoadFromSettings() => throw new NotSupportedException("Fake does not implement LoadFromSettings.");
    public DirectoryNodeModel AddDirectory(string path) => throw new NotSupportedException("Fake does not implement AddDirectory.");
    public bool TryDeleteDirectory(DirectoryNodeModel directory) => throw new NotSupportedException("Fake does not implement TryDeleteDirectory.");
    public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name) => throw new NotSupportedException("Fake does not implement AddGroup.");
    public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name) => throw new NotSupportedException("Fake does not implement AddGroupToGroup.");
    public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group) => throw new NotSupportedException("Fake does not implement TryDeleteGroup.");
    public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group) => throw new NotSupportedException("Fake does not implement TryDeleteGroupFromGroup.");
    public void RenameGroup(GroupNodeModel group, string newName) => throw new NotSupportedException("Fake does not implement RenameGroup.");
    public SessionNodeModel AddSession(DirectoryNodeModel parent, string name) => throw new NotSupportedException("Fake does not implement AddSession.");
    public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name) => throw new NotSupportedException("Fake does not implement AddSessionToGroup.");
    public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId) => throw new NotSupportedException("Fake does not implement ImportSession.");
    public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId) => throw new NotSupportedException("Fake does not implement ImportSessionToGroup.");
    public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false) => throw new NotSupportedException("Fake does not implement TryDeleteSession.");
    public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false) => throw new NotSupportedException("Fake does not implement TryDeleteSessionFromGroup.");
    public void RenameSession(SessionNodeModel session, string newName) => throw new NotSupportedException("Fake does not implement RenameSession.");
    public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session) => throw new NotSupportedException("Fake does not implement RemoveSessionFromModel.");
    public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index) => throw new NotSupportedException("Fake does not implement InsertSessionIntoDirectory.");
    public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index) => throw new NotSupportedException("Fake does not implement InsertSessionIntoGroup.");
    public List<SessionSummaryModel> BuildAgentSessionSummaries() => throw new NotSupportedException("Fake does not implement BuildAgentSessionSummaries.");

    // Session Tags (FR.24) have no TDCP methods either.
    public IReadOnlyList<SessionTagModel> GetSessionTags(string nodeId) => throw new NotSupportedException("Fake does not implement GetSessionTags.");
    public bool TakeTagGuidanceDue(string nodeId) => throw new NotSupportedException("Fake does not implement TakeTagGuidanceDue.");
    public IReadOnlyList<SessionTagUsageModel> GetSessionTagIndex(string? directoryPath) => throw new NotSupportedException("Fake does not implement GetSessionTagIndex.");

    // Attention Ledger (FR.23) has no TDCP methods; the wire client throws before reaching the server.
    public IReadOnlyList<AttentionItemModel> GetAttentionItems(string nodeId) => throw new NotSupportedException("Fake does not implement GetAttentionItems.");
    public AttentionSummaryModel GetAttentionSummary(string nodeId) => throw new NotSupportedException("Fake does not implement GetAttentionSummary.");
    public void MarkAttentionAnswered(string nodeId, IReadOnlyList<AttentionAnswerModel> answers) => throw new NotSupportedException("Fake does not implement MarkAttentionAnswered.");
    public void CloseAttentionByUser(string nodeId, string itemId) => throw new NotSupportedException("Fake does not implement CloseAttentionByUser.");
    public IReadOnlyList<AttentionItemModel> TakeUnreportedAttentionClosures(string nodeId) => throw new NotSupportedException("Fake does not implement TakeUnreportedAttentionClosures.");
    public IReadOnlySet<string> CollectAllClaudeSessionIds() => throw new NotSupportedException("Fake does not implement CollectAllClaudeSessionIds.");
    public Task<int> ReindexSessionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement ReindexSessionsAsync.");
    public Task<IReadOnlyList<SessionSearchResultModel>> SearchSessionsAsync(string query, string? projectSlug = null, string? role = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement SearchSessionsAsync.");
    public Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement SearchMatchingSessionIdsAsync.");
    public Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement SearchSessionScoresAsync.");
    public Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement IndexSessionAsync.");
    public Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(IReadOnlyList<ClaudeSessionSummaryModel> summaries, Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement EnsureTitlesAsync.");
    public Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default) => throw new NotSupportedException("Fake does not implement GetSessionSummariesAsync.");
}

// ── Tests ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Round-trip tests: starts a real TdcpServer over a loopback port, points a WireDaemonClient
/// at it, and asserts every IDaemonClient method correctly round-trips params and results
/// through the wire. Mirrors InProcDaemonClientTests "through the wire."
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WireDaemonClientTests : IDisposable
{
    private const string TestToken = "test-token-round-trip";

    private readonly WireFakeDaemonClient _fake;
    private readonly TdcpServer           _server;
    private readonly WireDaemonClient     _client;
    private readonly string               _serverBaseUrl;

    public WireDaemonClientTests()
    {
        _fake  = new WireFakeDaemonClient();

        // A free port is only known to be free until it is released: a parallel test class may take it before the bind, so try again.
        TdcpServer? server = null;
        var port = 0;
        for (var attempt = 1; server == null; attempt++)
        {
            port = FindFreePort();
            var candidate = new TdcpServer(_fake, new TdcpDispatcher([new TdcpLoopbackRpcModule(_fake)]), TestToken);
            try
            {
                candidate.Start(port);
                server = candidate;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                candidate.Dispose();
            }
        }

        _server = server;
        _serverBaseUrl = $"http://127.0.0.1:{port}";

        _client = new WireDaemonClient(_serverBaseUrl, TestToken);
        _client.StartAsync().GetAwaiter().GetResult();

        // Wait until the server holds the SSE stream (it buffers events from then on) instead of sleeping and hoping.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_fake.SseSubscribers < 1)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The SSE stream was not established");
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    private static SessionNodeModel MakeNode(string nodeId = "node-1") =>
        new() { NodeId = nodeId, Name = "TestNode", FileName = $"{nodeId}.txt" };

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ── Tree read ────────────────────────────────────────────────────────────────

    [Fact]
    public void Directories_RoundTrips_FullTree()
    {
        _fake.DirectoriesValue = new List<DirectoryNodeModel>
        {
            new() { Path = "/workspace/alpha" },
            new() { Path = "/workspace/beta" },
        };

        var result = _client.Directories;

        Assert.Equal(2, result.Count);
        Assert.Equal("/workspace/alpha", result[0].Path);
        Assert.Equal("/workspace/beta",  result[1].Path);
    }

    [Fact]
    public async Task Directories_CachedUntilTreeChangedInvalidates()
    {
        _fake.DirectoriesValue = new List<DirectoryNodeModel> { new() { Path = "/first" } };
        var first = _client.Directories; // fetches and caches

        _fake.DirectoriesValue = new List<DirectoryNodeModel> { new() { Path = "/second" } };
        var cached = _client.Directories; // still the cached copy
        Assert.Same(first, cached);

        // Raise TreeChanged to invalidate cache via SSE.
        _fake.RaiseTreeChanged(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        await Task.Delay(400); // allow SSE frame to propagate and cache to clear

        var refreshed = _client.Directories; // re-fetches
        Assert.Equal("/second", refreshed[0].Path);
    }

    [Fact]
    public void SelectedNodeId_Get_RoundTrips()
    {
        _fake.SelectedNodeIdValue = "abc-123";
        Assert.Equal("abc-123", _client.SelectedNodeId);
    }

    [Fact]
    public void SelectedNodeId_GetNull_RoundTrips()
    {
        _fake.SelectedNodeIdValue = null;
        Assert.Null(_client.SelectedNodeId);
    }

    [Fact]
    public void SelectedNodeId_Set_RoundTrips()
    {
        _client.SelectedNodeId = "xyz-789";

        Assert.Equal("xyz-789", _fake.LastSetSelectedNodeId);
        Assert.Equal("xyz-789", _fake.SelectedNodeIdValue);
    }

    [Fact]
    public void FindSessionByNodeId_RoundTrips_WhenFound()
    {
        var node = MakeNode("n-42");
        _fake.FindByNodeIdResult = node;

        var result = _client.FindSessionByNodeId("n-42");

        Assert.Equal("n-42", _fake.LastFindByNodeIdArg);
        Assert.NotNull(result);
        Assert.Equal("n-42",     result!.NodeId);
        Assert.Equal("TestNode", result.Name);
    }

    [Fact]
    public void FindSessionByNodeId_RoundTrips_WhenNotFound()
    {
        _fake.FindByNodeIdResult = null;

        var result = _client.FindSessionByNodeId("missing");

        Assert.Null(result);
    }

    [Fact]
    public void FindSessionByAgentToken_RoundTrips_WhenFound()
    {
        var node = MakeNode("tok-node");
        _fake.FindByTokenResult = node;

        var result = _client.FindSessionByAgentToken("my-secret-token");

        Assert.Equal("my-secret-token", _fake.LastFindByTokenArg);
        Assert.NotNull(result);
        Assert.Equal("tok-node", result!.NodeId);
    }

    [Fact]
    public void FindSessionByAgentToken_RoundTrips_WhenNotFound()
    {
        _fake.FindByTokenResult = null;

        Assert.Null(_client.FindSessionByAgentToken("bad-token"));
    }

    [Fact]
    public void GetSessionLocation_RoundTrips()
    {
        var node = MakeNode("loc-node");
        // Server resolves node from nodeId, so set fake to return it.
        _fake.FindByNodeIdResult = node;
        _fake.LocationResult     = ("My Dir", "My Dir / sub / session");

        var (label, path) = _client.GetSessionLocation(node);

        Assert.Equal("My Dir",               label);
        Assert.Equal("My Dir / sub / session", path);
    }

    // ── Running state ────────────────────────────────────────────────────────────

    [Fact]
    public void IsSessionRunning_RoundTrips_True()
    {
        _fake.IsRunningResult = true;

        var result = _client.IsSessionRunning("run-node");

        Assert.Equal("run-node", _fake.LastIsRunningNodeId);
        Assert.True(result);
    }

    [Fact]
    public void IsSessionRunning_RoundTrips_False()
    {
        _fake.IsRunningResult = false;

        Assert.False(_client.IsSessionRunning("idle-node"));
    }

    [Fact]
    public void SetSessionRunning_RoundTrips()
    {
        _client.SetSessionRunning("run-node", true);

        Assert.Equal("run-node", _fake.LastSetRunningNodeId);
        Assert.True(_fake.LastSetRunningValue);
    }

    // ── Turns ────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetTurnLock_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _client.GetTurnLock("any-node"));
    }

    [Fact]
    public async Task RunTurnAsync_RoundTrips_ParamsAndResult()
    {
        var node     = MakeNode("turn-node");
        var expected = new TurnResultModel(
            AssistantText: "The answer is 42.",
            SessionId:     "sess-99",
            InputTokens:   100,
            OutputTokens:  50);
        _fake.FindByNodeIdResult = node;
        _fake.RunResult          = expected;

        var result = await _client.RunTurnAsync(node, "What is 6×7?", TurnSource.Scheduled);

        Assert.Equal("turn-node",          _fake.LastRunNode?.NodeId);
        Assert.Equal("What is 6×7?",       _fake.LastRunPrompt);
        Assert.Equal(TurnSource.Scheduled, _fake.LastRunSource);
        Assert.Equal("The answer is 42.",  result.AssistantText);
        Assert.Equal("sess-99",            result.SessionId);
        Assert.Equal(100,                  result.InputTokens);
        Assert.Equal(50,                   result.OutputTokens);
    }

    [Fact]
    public void CancelTurn_RoundTrips_WhenRunning()
    {
        _fake.CancelResult = true;

        var result = _client.CancelTurn("cancel-node");

        Assert.True(result);
        Assert.Equal("cancel-node", _fake.LastCancelNodeId);
    }

    [Fact]
    public void CancelTurn_RoundTrips_WhenNotRunning()
    {
        _fake.CancelResult = false;

        Assert.False(_client.CancelTurn("idle-node"));
    }

    [Fact]
    public void ResolveProfileName_RoundTrips_WithProfile()
    {
        var node = MakeNode("prof-node");
        _fake.FindByNodeIdResult   = node;
        _fake.ResolveProfileResult = "MyProfile";

        var result = _client.ResolveProfileName(node);

        Assert.Equal("prof-node", _fake.LastResolveNode?.NodeId);
        Assert.Equal("MyProfile", result);
    }

    [Fact]
    public void ResolveProfileName_RoundTrips_WhenNull()
    {
        var node = MakeNode("prof-node-2");
        _fake.FindByNodeIdResult   = node;
        _fake.ResolveProfileResult = null;

        Assert.Null(_client.ResolveProfileName(node));
    }

    // ── Schedules ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddSchedule_RoundTrips()
    {
        var schedule = new ScheduleModel
        {
            ScheduleId   = "sched-1",
            TargetNodeId = "node-sched",
            Prompt       = "Wake up!",
            Note         = "Daily reminder",
            Kind         = ScheduleKind.At,
            FireAtUtc    = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
        };

        _client.AddSchedule(schedule);

        Assert.NotNull(_fake.AddedSchedule);
        Assert.Equal("sched-1",      _fake.AddedSchedule!.ScheduleId);
        Assert.Equal("node-sched",   _fake.AddedSchedule.TargetNodeId);
        Assert.Equal("Wake up!",     _fake.AddedSchedule.Prompt);
        Assert.Equal(ScheduleKind.At, _fake.AddedSchedule.Kind);
    }

    [Fact]
    public void RemoveSchedule_RoundTrips_WhenFound()
    {
        _fake.RemoveScheduleResult = true;

        var result = _client.RemoveSchedule("sched-x");

        Assert.True(result);
        Assert.Equal("sched-x", _fake.RemovedScheduleId);
    }

    [Fact]
    public void RemoveSchedule_RoundTrips_WhenNotFound()
    {
        _fake.RemoveScheduleResult = false;

        Assert.False(_client.RemoveSchedule("no-such-sched"));
    }

    [Fact]
    public void GetSchedules_RoundTrips_WithNodeIdFilter()
    {
        var schedule = new ScheduleModel { ScheduleId = "gs-1", TargetNodeId = "gs-node" };
        _fake.AddSchedule(schedule);

        var result = _client.GetSchedules("gs-node");

        Assert.Equal("gs-node", _fake.LastGetSchedulesNodeId);
        Assert.Single(result);
        Assert.Equal("gs-1", result[0].ScheduleId);
    }

    [Fact]
    public void GetSchedules_RoundTrips_WithNullFilter()
    {
        _client.GetSchedules();

        Assert.Null(_fake.LastGetSchedulesNodeId);
    }

    // ── Session content ──────────────────────────────────────────────────────────

    [Fact]
    public void ReadEntries_RoundTrips()
    {
        var ts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _fake.ReadEntriesResult = new List<SessionEntryModel>
        {
            new() { Timestamp = ts, Role = "USER",      Content = "Hello" },
            new() { Timestamp = ts, Role = "ASSISTANT", Content = "Hi there" },
        };

        var result = _client.ReadEntries("session.txt");

        Assert.Equal("session.txt", _fake.LastReadEntriesFile);
        Assert.Equal(2,          result.Count);
        Assert.Equal("Hello",    result[0].Content);
        Assert.Equal("Hi there", result[1].Content);
    }

    [Fact]
    public void SessionFileExists_RoundTrips_True()
    {
        _fake.SessionFileExistsResult = true;

        var result = _client.SessionFileExists("present.txt");

        Assert.True(result);
        Assert.Equal("present.txt", _fake.LastFileExistsFile);
    }

    [Fact]
    public void SessionFileExists_RoundTrips_False()
    {
        _fake.SessionFileExistsResult = false;

        Assert.False(_client.SessionFileExists("absent.txt"));
    }

    [Fact]
    public void GetSessionFilePath_RoundTrips()
    {
        _fake.GetFullPathResult = @"C:\sessions\data.txt";

        var result = _client.GetSessionFilePath("data.txt");

        Assert.Equal("data.txt",              _fake.LastGetPathFile);
        Assert.Equal(@"C:\sessions\data.txt", result);
    }

    // ── Event propagation (SSE) ───────────────────────────────────────────────────

    [Fact]
    public async Task TreeChanged_PropagatesOverSse()
    {
        var tcs = new TaskCompletionSource<TreeChangedEventArgs>();
        _client.TreeChanged += (_, e) => tcs.TrySetResult(e);

        _fake.RaiseTreeChanged(new TreeChangedEventArgs
        {
            Kind    = TreeChangeKind.SessionAdded,
            Session = MakeNode("sse-node"),
        });

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(TreeChangeKind.SessionAdded, received.Kind);
        Assert.Equal("sse-node",                 received.Session?.NodeId);
    }

    [Fact]
    public async Task ScheduleChanged_PropagatesOverSse()
    {
        var tcs = new TaskCompletionSource<bool>();
        _client.ScheduleChanged += (_, _) => tcs.TrySetResult(true);

        _fake.RaiseScheduleChanged();

        var fired = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(fired);
    }

    [Fact]
    public async Task TreeChanged_PropagatesMultipleTimes()
    {
        var received = new List<TreeChangedEventArgs>();
        var tcs      = new TaskCompletionSource<bool>();

        _client.TreeChanged += (_, e) =>
        {
            lock (received)
            {
                received.Add(e);
                if (received.Count >= 2)
                    tcs.TrySetResult(true);
            }
        };

        _fake.RaiseTreeChanged(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        _fake.RaiseTreeChanged(new TreeChangedEventArgs { Kind = TreeChangeKind.Reloaded });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, received.Count);
    }

    // ── Auth rejection ────────────────────────────────────────────────────────────

    [Fact]
    public void WrongToken_PostRequest_ThrowsTdcpCallException()
    {
        using var badClient = new WireDaemonClient(_serverBaseUrl, "wrong-token");
        Assert.Throws<TdcpCallException>(() => badClient.FindSessionByNodeId("any"));
    }

    // ── Tree CRUD (Stage 5b) — not wired over TDCP yet ────────────────────────────

    [Fact]
    public void LoadFromSettings_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _client.LoadFromSettings());
    }

    [Fact]
    public void AddDirectory_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _client.AddDirectory("/any"));
    }

    [Fact]
    public void TryDeleteDirectory_ThrowsNotSupportedException()
    {
        var dir = new DirectoryNodeModel { Path = "/any" };
        Assert.Throws<NotSupportedException>(() => _client.TryDeleteDirectory(dir));
    }

    [Fact]
    public void AddGroup_ThrowsNotSupportedException()
    {
        var parent = new DirectoryNodeModel { Path = "/any" };
        Assert.Throws<NotSupportedException>(() => _client.AddGroup(parent, "name"));
    }

    [Fact]
    public void AddGroupToGroup_ThrowsNotSupportedException()
    {
        var parent = new GroupNodeModel { Name = "parent" };
        Assert.Throws<NotSupportedException>(() => _client.AddGroupToGroup(parent, "name"));
    }

    [Fact]
    public void TryDeleteGroup_ThrowsNotSupportedException()
    {
        var parent = new DirectoryNodeModel { Path = "/any" };
        var group  = new GroupNodeModel { Name = "group" };
        Assert.Throws<NotSupportedException>(() => _client.TryDeleteGroup(parent, group));
    }

    [Fact]
    public void TryDeleteGroupFromGroup_ThrowsNotSupportedException()
    {
        var parent = new GroupNodeModel { Name = "parent" };
        var group  = new GroupNodeModel { Name = "group" };
        Assert.Throws<NotSupportedException>(() => _client.TryDeleteGroupFromGroup(parent, group));
    }

    [Fact]
    public void RenameGroup_ThrowsNotSupportedException()
    {
        var group = new GroupNodeModel { Name = "group" };
        Assert.Throws<NotSupportedException>(() => _client.RenameGroup(group, "newName"));
    }

    [Fact]
    public void AddSession_ThrowsNotSupportedException()
    {
        var parent = new DirectoryNodeModel { Path = "/any" };
        Assert.Throws<NotSupportedException>(() => _client.AddSession(parent, "name"));
    }

    [Fact]
    public void AddSessionToGroup_ThrowsNotSupportedException()
    {
        var parent = new GroupNodeModel { Name = "parent" };
        Assert.Throws<NotSupportedException>(() => _client.AddSessionToGroup(parent, "name"));
    }

    [Fact]
    public void ImportSession_ThrowsNotSupportedException()
    {
        var parent = new DirectoryNodeModel { Path = "/any" };
        Assert.Throws<NotSupportedException>(() => _client.ImportSession(parent, "name", "file.txt", "claude-id"));
    }

    [Fact]
    public void ImportSessionToGroup_ThrowsNotSupportedException()
    {
        var parent = new GroupNodeModel { Name = "parent" };
        Assert.Throws<NotSupportedException>(() => _client.ImportSessionToGroup(parent, "name", "file.txt", "claude-id"));
    }

    [Fact]
    public void TryDeleteSession_ThrowsNotSupportedException()
    {
        var parent  = new DirectoryNodeModel { Path = "/any" };
        var session = MakeNode();
        Assert.Throws<NotSupportedException>(() => _client.TryDeleteSession(parent, session));
    }

    [Fact]
    public void TryDeleteSessionFromGroup_ThrowsNotSupportedException()
    {
        var parent  = new GroupNodeModel { Name = "parent" };
        var session = MakeNode();
        Assert.Throws<NotSupportedException>(() => _client.TryDeleteSessionFromGroup(parent, session));
    }

    [Fact]
    public void RenameSession_ThrowsNotSupportedException()
    {
        var session = MakeNode();
        Assert.Throws<NotSupportedException>(() => _client.RenameSession(session, "newName"));
    }

    [Fact]
    public void RemoveSessionFromModel_ThrowsNotSupportedException()
    {
        var session = MakeNode();
        Assert.Throws<NotSupportedException>(() => _client.RemoveSessionFromModel(session));
    }

    [Fact]
    public void InsertSessionIntoDirectory_ThrowsNotSupportedException()
    {
        var parent  = new DirectoryNodeModel { Path = "/any" };
        var session = MakeNode();
        Assert.Throws<NotSupportedException>(() => _client.InsertSessionIntoDirectory(parent, session, 0));
    }

    [Fact]
    public void InsertSessionIntoGroup_ThrowsNotSupportedException()
    {
        var parent  = new GroupNodeModel { Name = "parent" };
        var session = MakeNode();
        Assert.Throws<NotSupportedException>(() => _client.InsertSessionIntoGroup(parent, session, 0));
    }

    [Fact]
    public void BuildAgentSessionSummaries_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _client.BuildAgentSessionSummaries());
    }

    [Fact]
    public async Task ReindexSessionsAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.ReindexSessionsAsync());
    }

    [Fact]
    public async Task SearchSessionsAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.SearchSessionsAsync("query"));
    }

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.SearchMatchingSessionIdsAsync("query"));
    }

    [Fact]
    public async Task SearchSessionScoresAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.SearchSessionScoresAsync("query"));
    }

    [Fact]
    public async Task IndexSessionAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.IndexSessionAsync("ext-1", "/Users/x/project-a"));
    }

    [Fact]
    public async Task EnsureTitlesAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.EnsureTitlesAsync(Array.Empty<ClaudeSessionSummaryModel>()));
    }

    [Fact]
    public async Task GetSessionSummariesAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.GetSessionSummariesAsync(Array.Empty<string>()));
    }

    [Fact]
    public void CollectAllClaudeSessionIds_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _client.CollectAllClaudeSessionIds());
    }

    // ── Session Tags (FR.24.8) — not wired over TDCP ───────────────────────────────────

    [Fact]
    public void SessionTags_AreNotAvailableOverTheWire_AndTheEventNeverFires()   // TC.TG.18-6
    {
        var fired = false;
        _client.SessionTagsChanged += (_, _) => fired = true;

        Assert.Throws<NotSupportedException>(() => _client.GetSessionTags("node-1"));
        Assert.Throws<NotSupportedException>(() => _client.TakeTagGuidanceDue("node-1"));
        Assert.False(fired);
    }

    [Fact]
    public void GetSessionTagIndex_ThrowsNotSupportedException()   // TC.TG.36
    {
        Assert.Throws<NotSupportedException>(() => _client.GetSessionTagIndex(null));
        Assert.Throws<NotSupportedException>(() => _client.GetSessionTagIndex(@"C:\Proj"));
    }
}
