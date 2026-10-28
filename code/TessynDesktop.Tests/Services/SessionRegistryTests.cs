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
    public bool FileExists { get; set; } = true;
    private int _counter;

    /// <summary>Optional canned entries per file name, consumed by <see cref="ReadEntries"/> (FR.21.8 tests).</summary>
    public Dictionary<string, IReadOnlyList<SessionEntryModel>> Entries { get; } = new();

    public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }
    public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }
    public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText) { }

    public string CreateSessionFile() => $"session-{_counter++}.txt";
    public bool SessionFileExists(string fileName) => FileExists;
    public void DeleteSessionFile(string fileName) { }
    public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) =>
        Entries.TryGetValue(fileName, out var entries) ? entries : Array.Empty<SessionEntryModel>();
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
    public bool IsSessionResumable(string workingDirectory, string claudeSessionId) => false;
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

    [Fact]
    public void BuildAgentSessionSummaries_IncludesIsFavoriteAndLastActivityUtc()
    {
        var dir = _registry.AddDirectory(AnyPath("x"));
        var session = _registry.AddSession(dir, "FavSession");
        session.IsFavorite = true;

        var lastTimestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        _fileService.Entries[session.FileName] = new List<SessionEntryModel>
        {
            new() { Timestamp = lastTimestamp.AddMinutes(-5), Role = Constants.SessionFile.RoleUser, Content = "first" },
            new() { Timestamp = lastTimestamp, Role = Constants.SessionFile.RoleAssistant, Content = "last" },
        };

        var summary = Assert.Single(_registry.BuildAgentSessionSummaries());

        Assert.True(summary.IsFavorite);
        Assert.Equal(lastTimestamp, summary.LastActivityUtc);
    }

    [Fact]
    public void BuildAgentSessionSummaries_NoEntries_LastActivityUtcIsNull()
    {
        var dir = _registry.AddDirectory(AnyPath("y"));
        _registry.AddSession(dir, "EmptySession");

        var summary = Assert.Single(_registry.BuildAgentSessionSummaries());

        Assert.False(summary.IsFavorite);
        Assert.Null(summary.LastActivityUtc);
    }
}
