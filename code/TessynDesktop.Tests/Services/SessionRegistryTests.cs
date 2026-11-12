using System;
using System.Collections.Generic;
using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeSessionFileService : ISessionFileService
{
    public event EventHandler<SessionFileRewrittenEventArgs>? SessionFileRewritten { add { } remove { } }
    public IReadOnlyList<SessionEntryModel> ReadTailEntries(string fileName, int maxBytes) => Array.Empty<SessionEntryModel>();
    public IReadOnlyList<SessionEntryModel> ReadEntriesFrom(string fileName, long startOffset) => Array.Empty<SessionEntryModel>();
    public SessionFileStampModel? GetFileStamp(string fileName) => null;
    public bool FileExists { get; set; } = true;
    private int _counter;

    /// <summary>Optional canned entries per file name, consumed by <see cref="ReadEntries"/> (FR.21.8 tests).</summary>
    public Dictionary<string, IReadOnlyList<SessionEntryModel>> Entries { get; } = new();

    /// <summary>Settable per-file last-write times for <see cref="GetLastWriteTimeUtc"/> (FR.15.13); a missing key means the file is missing.</summary>
    public Dictionary<string, DateTimeOffset> LastWriteTimes { get; } = new();

    /// <summary>Number of <see cref="ReadEntries"/> calls — the listing must never read session content (FR.15.13).</summary>
    public int ReadEntriesCalls { get; private set; }

    /// <summary>Optional hook run at the start of every <see cref="GetLastWriteTimeUtc"/> call, e.g. to block it in the lock test.</summary>
    public Action<string>? OnGetLastWriteTime { get; set; }

    public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }
    public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }
    public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText) { }

    public string CreateSessionFile() => $"session-{_counter++}.txt";
    public bool SessionFileExists(string fileName) => FileExists;
    public DateTimeOffset? GetLastWriteTimeUtc(string fileName)
    {
        OnGetLastWriteTime?.Invoke(fileName);
        return LastWriteTimes.TryGetValue(fileName, out var t) ? t : null;
    }
    public void DeleteSessionFile(string fileName) { }
    public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName)
    {
        ReadEntriesCalls++;
        return Entries.TryGetValue(fileName, out var entries) ? entries : Array.Empty<SessionEntryModel>();
    }
    public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null) { }
    public void AppendCompactionSeparator(string fileName) { }
    public string GetFullPath(string fileName) => fileName;
    public void RewriteSessionFile(string fileName, string content) { }
    public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) { }
    public int RepairCorruptedCompactions() => 0;
}

internal sealed class FakeDirectoryLabelService : IDirectoryLabelService
{
    public string GetLabel(string directoryPath) => directoryPath;
}

internal sealed class FakeClaudeSessionStatusService : IClaudeSessionStatusService
{
    /// <summary>Claude session ids reported resumable (FR.15.13); everything else is not.</summary>
    public HashSet<string> ResumableSessionIds { get; } = new();

    /// <summary>Claude session ids passed to <see cref="IsSessionResumable"/>, in call order.</summary>
    public List<string> ResumabilityChecks { get; } = new();

    /// <summary>The <c>projectsDirs</c> argument of each <see cref="IsSessionResumable"/> call, in call order.</summary>
    public List<IReadOnlyList<string>?> ResumabilityProjectsDirs { get; } = new();

    public int GetAllProjectsDirsCalls { get; private set; }

    /// <summary>Optional hook run at the start of every <see cref="IsSessionResumable"/> call, e.g. to probe the settings lock.</summary>
    public Action<string>? OnIsSessionResumable { get; set; }

    public IReadOnlyList<string> GetAllProjectsDirs()
    {
        GetAllProjectsDirsCalls++;
        return new[] { "projects-dir-1", "projects-dir-2" };
    }

    public bool IsSessionResumable(string workingDirectory, string claudeSessionId, IReadOnlyList<string>? projectsDirs = null)
    {
        OnIsSessionResumable?.Invoke(claudeSessionId);
        ResumabilityChecks.Add(claudeSessionId);
        ResumabilityProjectsDirs.Add(projectsDirs);
        return ResumableSessionIds.Contains(claudeSessionId);
    }

    public IReadOnlyList<bool> AreSessionsResumable(IReadOnlyList<SessionResumabilityRequest> requests, IReadOnlyList<string>? projectsDirs = null) =>
        requests.Select(r => r.ClaudeSessionId is not null && ResumableSessionIds.Contains(r.ClaudeSessionId)).ToList();

    public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir) => false;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class SessionRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettingsService _settings;
    private readonly FakeSessionFileService _fileService = new();
    private readonly SessionRegistry _registry;

    public SessionRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _settings = new AppSettingsService(_tempDir);
        _registry = new SessionRegistry(
            _settings,
            _fileService,
            new FakeDirectoryLabelService(),
            new FakeClaudeSessionStatusService());
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string AnyPath(string suffix = "") => Path.Combine(_tempDir, "dir" + suffix);

    // ── 1. LoadFromSettings ───────────────────────────────────────────────────

    [Fact]
    public void LoadFromSettings_BackfillsNodeIdAndAgentToken()
    {
        var session = new SessionNodeModel { Name = "Old", FileName = "old.txt", NodeId = "", AgentToken = "" };
        var dir = new DirectoryNodeModel { Path = AnyPath("a") };
        dir.Sessions.Add(session);
        _settings.Settings.Tree.Add(dir);

        var raised = false;
        _registry.TreeChanged += (_, e) => { if (e.Kind == TreeChangeKind.Reloaded) raised = true; };

        _registry.LoadFromSettings();

        Assert.NotEmpty(session.NodeId);
        Assert.NotEmpty(session.AgentToken);
        Assert.True(raised);
    }

    [Fact]
    public void LoadFromSettings_SavesWhenDirty()
    {
        var session = new SessionNodeModel { Name = "Old", FileName = "old.txt", NodeId = "", AgentToken = "" };
        var dir = new DirectoryNodeModel { Path = AnyPath("b") };
        dir.Sessions.Add(session);
        _settings.Settings.Tree.Add(dir);

        _registry.LoadFromSettings();

        var freshSettings = new AppSettingsService(_tempDir);
        freshSettings.Load();
        Assert.NotEmpty(freshSettings.Settings.Tree[0].Sessions[0].NodeId);
    }

    [Fact]
    public void LoadFromSettings_BackfillsSessionsInGroups()
    {
        var group = new GroupNodeModel { Name = "G" };
        var session = new SessionNodeModel { Name = "Grp", FileName = "g.txt", NodeId = "", AgentToken = "" };
        group.Sessions.Add(session);
        var dir = new DirectoryNodeModel { Path = AnyPath("c") };
        dir.Groups.Add(group);
        _settings.Settings.Tree.Add(dir);

        _registry.LoadFromSettings();

        Assert.NotEmpty(session.NodeId);
        Assert.NotEmpty(session.AgentToken);
    }

    // ── 2. AddDirectory ───────────────────────────────────────────────────────

    [Fact]
    public void AddDirectory_AddsDirAndRaisesSettingsModified()
    {
        var events = new List<TreeChangedEventArgs>();
        _registry.TreeChanged += (_, e) => events.Add(e);

        var path = AnyPath("d");
        var dir = _registry.AddDirectory(path);

        Assert.Single(_registry.Directories);
        Assert.Equal(Path.GetFullPath(path), dir.Path);
        Assert.Contains(events, e => e.Kind == TreeChangeKind.SettingsModified);
    }

    [Fact]
    public void AddDirectory_IdempotentOnDuplicatePath()
    {
        var path = AnyPath("e");
        var dir1 = _registry.AddDirectory(path);
        var dir2 = _registry.AddDirectory(path.ToLowerInvariant());

        Assert.Single(_registry.Directories);
        Assert.Same(dir1, dir2);
    }

    // ── 3. TryDeleteDirectory ────────────────────────────────────────────────

    [Fact]
    public void TryDeleteDirectory_FailsWhenNonEmpty()
    {
        var dir = _registry.AddDirectory(AnyPath("f"));
        _registry.AddSession(dir, "s");

        var result = _registry.TryDeleteDirectory(dir);

        Assert.False(result);
        Assert.Single(_registry.Directories);
    }

    [Fact]
    public void TryDeleteDirectory_SucceedsWhenEmptyAndRaisesEvent()
    {
        var dir = _registry.AddDirectory(AnyPath("g"));
        var events = new List<TreeChangedEventArgs>();
        _registry.TreeChanged += (_, e) => events.Add(e);

        var result = _registry.TryDeleteDirectory(dir);

        Assert.True(result);
        Assert.Empty(_registry.Directories);
        Assert.Contains(events, e => e.Kind == TreeChangeKind.SettingsModified);
    }

    // ── 4. AddSession / AddSessionToGroup ────────────────────────────────────

    [Fact]
    public void AddSession_PopulatesAllIdentityFields()
    {
        var dir = _registry.AddDirectory(AnyPath("h"));
        var session = _registry.AddSession(dir, "My Session");

        Assert.NotEmpty(session.NodeId);
        Assert.NotEmpty(session.AgentToken);
        Assert.NotEmpty(session.FileName);
        Assert.Equal(dir.Path, session.WorkingDirectory);
    }

    [Fact]
    public void AddSessionToGroup_PopulatesAllIdentityFields()
    {
        var dir = _registry.AddDirectory(AnyPath("i"));
        var group = _registry.AddGroup(dir, "My Group");
        var session = _registry.AddSessionToGroup(group, "Grouped Session");

        Assert.NotEmpty(session.NodeId);
        Assert.NotEmpty(session.AgentToken);
        Assert.NotEmpty(session.FileName);
        Assert.Equal(dir.Path, session.WorkingDirectory);
    }

    // ── 5. FindModelByNodeId ─────────────────────────────────────────────────

    [Fact]
    public void FindModelByNodeId_FindsSessionInDirectory()
    {
        var dir = _registry.AddDirectory(AnyPath("j"));
        var session = _registry.AddSession(dir, "Direct");

        var found = _registry.FindModelByNodeId(session.NodeId);

        Assert.Same(session, found);
    }

    [Fact]
    public void FindModelByNodeId_FindsSessionNestedInGroup()
    {
        var dir = _registry.AddDirectory(AnyPath("k"));
        var group = _registry.AddGroup(dir, "G");
        var session = _registry.AddSessionToGroup(group, "Nested");

        var found = _registry.FindModelByNodeId(session.NodeId);

        Assert.Same(session, found);
    }

    [Fact]
    public void FindModelByNodeId_ReturnsNullForUnknown()
    {
        var result = _registry.FindModelByNodeId("nonexistent-node-id");

        Assert.Null(result);
    }

    // ── 6. FindModelByAgentToken ──────────────────────────────────────────────

    [Fact]
    public void FindModelByAgentToken_FindsSessionInDirectory()
    {
        var dir = _registry.AddDirectory(AnyPath("l"));
        var session = _registry.AddSession(dir, "TokenSession");

        var found = _registry.FindModelByAgentToken(session.AgentToken);

        Assert.Same(session, found);
    }

    [Fact]
    public void FindModelByAgentToken_FindsSessionInGroup()
    {
        var dir = _registry.AddDirectory(AnyPath("m"));
        var group = _registry.AddGroup(dir, "G2");
        var session = _registry.AddSessionToGroup(group, "GrpTokenSession");

        var found = _registry.FindModelByAgentToken(session.AgentToken);

        Assert.Same(session, found);
    }

    // ── 6b. FindModelByFileName (FR.21.9) ────────────────────────────────────

    [Fact]
    public void FindModelByFileName_FindsSessionInDirectory()
    {
        var dir = _registry.AddDirectory(AnyPath("m2"));
        var session = _registry.AddSession(dir, "FileSession");

        var found = _registry.FindModelByFileName(session.FileName);

        Assert.Same(session, found);
    }

    [Fact]
    public void FindModelByFileName_FindsSessionNestedInGroup()
    {
        var dir = _registry.AddDirectory(AnyPath("m3"));
        var group = _registry.AddGroup(dir, "G3");
        var session = _registry.AddSessionToGroup(group, "GrpFileSession");

        var found = _registry.FindModelByFileName(session.FileName);

        Assert.Same(session, found);
    }

    [Fact]
    public void FindModelByFileName_ReturnsNullForUnknown()
    {
        var result = _registry.FindModelByFileName("nonexistent.txt");

        Assert.Null(result);
    }

    // ── 7. RenameSession ──────────────────────────────────────────────────────

    [Fact]
    public void RenameSession_UpdatesNameAndRaisesSettingsModified()
    {
        var dir = _registry.AddDirectory(AnyPath("n"));
        var session = _registry.AddSession(dir, "OldName");
        var events = new List<TreeChangedEventArgs>();
        _registry.TreeChanged += (_, e) => events.Add(e);

        _registry.RenameSession(session, "NewName");

        Assert.Equal("NewName", session.Name);
        Assert.Contains(events, e => e.Kind == TreeChangeKind.SettingsModified);
    }

    // ── 8. TryDeleteSession ───────────────────────────────────────────────────

    [Fact]
    public void TryDeleteSession_ReturnsFalseWhenFileExists()
    {
        _fileService.FileExists = true;
        var dir = _registry.AddDirectory(AnyPath("o"));
        var session = _registry.AddSession(dir, "ToDelete");

        var result = _registry.TryDeleteSession(dir, session);

        Assert.False(result);
        Assert.Single(dir.Sessions);
    }

    [Fact]
    public void TryDeleteSession_ReturnsTrueWhenForceDelete()
    {
        _fileService.FileExists = true;
        var dir = _registry.AddDirectory(AnyPath("p"));
        var session = _registry.AddSession(dir, "ForceDelete");

        var result = _registry.TryDeleteSession(dir, session, forceDelete: true);

        Assert.True(result);
        Assert.Empty(dir.Sessions);
    }

    // ── 9. SetSessionRunning / IsSessionRunning ───────────────────────────────

    [Fact]
    public void SetSessionRunning_True_SetsIsRunning()
    {
        var dir = _registry.AddDirectory(AnyPath("q"));
        var session = _registry.AddSession(dir, "Runner");

        _registry.SetSessionRunning(session.NodeId, true);

        Assert.True(_registry.IsSessionRunning(session.NodeId));
    }

    [Fact]
    public void SetSessionRunning_False_ClearsIsRunning()
    {
        var dir = _registry.AddDirectory(AnyPath("r"));
        var session = _registry.AddSession(dir, "Runner2");
        _registry.SetSessionRunning(session.NodeId, true);

        _registry.SetSessionRunning(session.NodeId, false);

        Assert.False(_registry.IsSessionRunning(session.NodeId));
    }

    [Fact]
    public void SetSessionRunning_RaisesSessionIsRunningChangedEvent()
    {
        var dir = _registry.AddDirectory(AnyPath("s"));
        var session = _registry.AddSession(dir, "Runner3");
        var events = new List<TreeChangedEventArgs>();
        _registry.TreeChanged += (_, e) => events.Add(e);

        _registry.SetSessionRunning(session.NodeId, true);

        Assert.Contains(events, e => e.Kind == TreeChangeKind.SessionIsRunningChanged);
    }

    // ── 10. CreateSessionForAgent ─────────────────────────────────────────────

    [Fact]
    public void CreateSessionForAgent_CreatesSessionWithCorrectFields()
    {
        var dir = _registry.AddDirectory(AnyPath("t"));
        SessionNodeModel? addedSession = null;
        DirectoryNodeModel? addedDir = null;
        _registry.TreeChanged += (_, e) =>
        {
            if (e.Kind == TreeChangeKind.SessionAdded)
            {
                addedSession = e.Session;
                addedDir = e.ParentDirectory;
            }
        };

        var result = _registry.CreateSessionForAgent(dir.Path, "agent", null, 2, "supervisor-id");

        Assert.NotNull(result);
        Assert.Equal(2, result.OrchestrationDepth);
        Assert.Equal("supervisor-id", result.SupervisorNodeId);
        Assert.NotEmpty(result.NodeId);
        Assert.NotEmpty(result.AgentToken);
        Assert.NotEmpty(result.FileName);
        Assert.Same(result, addedSession);
        Assert.Same(dir, addedDir);
    }

    [Fact]
    public void CreateSessionForAgent_ReturnsNullWhenNoDirectoryMatches()
    {
        var result = _registry.CreateSessionForAgent(@"C:\NonExistent\UnknownPath\XYZ", "agent", null);

        Assert.Null(result);
    }

    [Fact]
    public void CreateSessionForAgent_CreatesGroupWhenGroupNameProvided()
    {
        var dir = _registry.AddDirectory(AnyPath("u"));
        GroupNodeModel? parentGroup = null;
        _registry.TreeChanged += (_, e) =>
        {
            if (e.Kind == TreeChangeKind.SessionAdded)
                parentGroup = e.ParentGroup;
        };

        var result = _registry.CreateSessionForAgent(dir.Path, "agent", "AgentGroup", 1, null);

        Assert.NotNull(result);
        Assert.NotNull(parentGroup);
        Assert.Equal("AgentGroup", parentGroup!.Name);
    }

    // ── 11. CollectAllClaudeSessionIds ────────────────────────────────────────

    [Fact]
    public void CollectAllClaudeSessionIds_IncludesClaudeSessionIdAndExternalId()
    {
        var dir = _registry.AddDirectory(AnyPath("v"));
        var session = _registry.AddSession(dir, "IdSession");
        session.ClaudeSessionId = "claude-session-abc";
        session.ExternalId = "external-id-xyz";

        var ids = _registry.CollectAllClaudeSessionIds();

        Assert.Contains("claude-session-abc", ids);
        Assert.Contains("external-id-xyz", ids);
    }

    [Fact]
    public void CollectAllClaudeSessionIds_IncludesIdsFromGroups()
    {
        var dir = _registry.AddDirectory(AnyPath("w"));
        var group = _registry.AddGroup(dir, "G");
        var session = _registry.AddSessionToGroup(group, "GrpIdSession");
        session.ClaudeSessionId = "grp-claude-id";

        var ids = _registry.CollectAllClaudeSessionIds();

        Assert.Contains("grp-claude-id", ids);
    }

    // ── 12. BuildAgentSessionSummaries (FR.21.8) ──────────────────────────────

    // BuildAgentSessionSummaries_IncludesIsFavoriteAndLastActivityUtc was replaced by
    // BuildAgentSessionSummaries_LastActivityIsLastWriteTime_NoContentReads (SessionRegistryListingTests, TC.15.5).

    [Fact]
    public void BuildAgentSessionSummaries_NoEntries_LastActivityUtcIsNull()
    {
        var dir = _registry.AddDirectory(AnyPath("y"));
        _registry.AddSession(dir, "EmptySession");

        var summary = Assert.Single(_registry.BuildAgentSessionSummaries());

        Assert.False(summary.IsFavorite);
        Assert.Null(summary.LastActivityUtc);
    }

    // ── 13. SnapshotSessionsForTagIndex (FR.24.13) ────────────────────────────

    [Fact]
    public void SnapshotSessionsForTagIndex_EmptyTree_ReturnsEmptyList()   // TC.TG.32
    {
        Assert.Empty(_registry.SnapshotSessionsForTagIndex());
    }

    [Fact]
    public void SnapshotSessionsForTagIndex_IncludesDirectAndNestedGroupSessions_WithTheirDirectoryPath()   // TC.TG.32
    {
        var dir    = _registry.AddDirectory(AnyPath("tag1"));
        var direct = _registry.AddSession(dir, "Direct");
        var g1     = _registry.AddGroup(dir, "G1");
        var inG1   = _registry.AddSessionToGroup(g1, "InG1");
        var g2     = _registry.AddGroupToGroup(g1, "G2");
        var inG2   = _registry.AddSessionToGroup(g2, "InG2");
        var g3     = _registry.AddGroupToGroup(g2, "G3");
        var inG3   = _registry.AddSessionToGroup(g3, "InG3");

        var snapshot = _registry.SnapshotSessionsForTagIndex();

        Assert.Equal(4, snapshot.Count);
        foreach (var s in new[] { direct, inG1, inG2, inG3 })
        {
            var entry = Assert.Single(snapshot, e => e.NodeId == s.NodeId);
            Assert.Equal(s.Name, entry.Name);
            Assert.Equal(dir.Path, entry.DirectoryPath);
        }
    }

    [Fact]
    public void SnapshotSessionsForTagIndex_CopiesSupervisorNodeId_ForAWorkerStoredNextToItsSupervisor()   // TC.TG.33
    {
        var dir        = _registry.AddDirectory(AnyPath("tag2"));
        var supervisor = _registry.AddSession(dir, "Supervisor");
        var worker     = _registry.AddSession(dir, "Worker");
        worker.SupervisorNodeId = supervisor.NodeId;

        var snapshot = _registry.SnapshotSessionsForTagIndex();

        Assert.Null(Assert.Single(snapshot, e => e.NodeId == supervisor.NodeId).SupervisorNodeId);
        Assert.Equal(supervisor.NodeId, Assert.Single(snapshot, e => e.NodeId == worker.NodeId).SupervisorNodeId);
    }

    [Fact]
    public void SnapshotSessionsForTagIndex_SeveralDirectories_EachSessionMapsToItsOwnPath()   // TC.TG.34
    {
        var dirA = _registry.AddDirectory(AnyPath("tagA"));
        var dirB = _registry.AddDirectory(AnyPath("tagB"));
        var a    = _registry.AddSession(dirA, "A");
        var gB   = _registry.AddGroup(dirB, "GB");
        var b    = _registry.AddSessionToGroup(gB, "B");

        var snapshot = _registry.SnapshotSessionsForTagIndex();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(dirA.Path, Assert.Single(snapshot, e => e.NodeId == a.NodeId).DirectoryPath);
        Assert.Equal(dirB.Path, Assert.Single(snapshot, e => e.NodeId == b.NodeId).DirectoryPath);
    }
}
