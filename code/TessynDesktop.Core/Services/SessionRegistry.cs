using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Owns the authoritative <see cref="DirectoryNodeModel"/> / <see cref="GroupNodeModel"/> /
/// <see cref="SessionNodeModel"/> tree. Persists via <see cref="IAppSettingsService"/> and raises
/// <see cref="TreeChanged"/> when the tree changes, so <c>SessionTreeViewModel</c> can re-project.
/// Thread-safe: model mutations take <see cref="IAppSettingsService.SyncRoot"/> — the SAME lock
/// <see cref="IAppSettingsService.Save"/>/<see cref="IAppSettingsService.Load"/> take internally —
/// rather than a private lock of its own, so a tree mutation and its subsequent Save can never be
/// split by a concurrent Save from another thread (ISSUE concurrent-settings-save-lost-update).
/// The running-state dictionary uses concurrent primitives instead, since it's transient/never persisted.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRegistry : ISessionRegistry
{
    private static readonly ILogger _log = Log.ForContext<SessionRegistry>();

    private readonly IAppSettingsService          _appSettings;
    private readonly ISessionFileService          _fileService;
    private readonly IDirectoryLabelService       _labelService;
    private readonly IClaudeSessionStatusService  _sessionStatus;

    // Running state: NodeId → number of open running activities (transient, never persisted). Guarded by _runningLock.
    private readonly Dictionary<string, int> _runningNodes = new();
    private readonly object _runningLock = new();
    // Supervisor NodeId → running workers below it (direct and nested); null = stale. The generation stops a count computed before an invalidation from being cached. Guarded by _runningLock.
    private Dictionary<string, int>? _runningWorkerCounts;
    private long _runningWorkerGeneration;

    public event EventHandler<TreeChangedEventArgs>? TreeChanged;

    public event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged;

    public string? SelectedNodeId { get; set; }

    public IReadOnlyList<DirectoryNodeModel> Directories => _appSettings.Settings.Tree;

    public SessionRegistry(
        IAppSettingsService appSettings,
        ISessionFileService fileService,
        IDirectoryLabelService labelService,
        IClaudeSessionStatusService sessionStatus)
    {
        _appSettings   = appSettings;
        _fileService   = fileService;
        _labelService  = labelService;
        _sessionStatus = sessionStatus;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public void LoadFromSettings()
    {
        var dirty = false;
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                if (string.IsNullOrEmpty(dir.NodeId)) { dir.NodeId = NewNodeId(); dirty = true; }
                dirty |= BackfillAgentIdentity(dir.Sessions);
                foreach (var grp in dir.Groups)
                    dirty |= BackfillAgentIdentityInGroups(grp);
            }

            if (dirty)
                _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.Reloaded });
        _log.Debug("SessionRegistry loaded {DirCount} directories", _appSettings.Settings.Tree.Count);
    }

    // ── Lookups ───────────────────────────────────────────────────────────────

    public SessionNodeModel? FindModelByNodeId(string nodeId)
    {
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var found = FindInSessions(dir.Sessions, dir.Groups, s => s.NodeId == nodeId);
                if (found != null) return found;
            }
            return null;
        }
    }

    public SessionNodeModel? FindModelByAgentToken(string token)
    {
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var found = FindInSessions(dir.Sessions, dir.Groups, s => s.AgentToken == token);
                if (found != null) return found;
            }
            return null;
        }
    }

    public SessionNodeModel? FindModelByFileName(string fileName)
    {
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var found = FindInSessions(dir.Sessions, dir.Groups, s => s.FileName == fileName);
                if (found != null) return found;
            }
            return null;
        }
    }

    public DirectoryNodeModel? FindDirectoryByNodeId(string nodeId)
    {
        lock (_appSettings.SyncRoot)
            return _appSettings.Settings.Tree.FirstOrDefault(d => d.NodeId == nodeId);
    }

    public GroupNodeModel? FindGroupByNodeId(string nodeId)
    {
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var found = FindGroup(dir.Groups, nodeId);
                if (found != null) return found;
            }
            return null;
        }
    }

    // ── CRUD – Directory ──────────────────────────────────────────────────────

    public DirectoryNodeModel AddDirectory(string path)
    {
        // No trailing separator (except a drive root): "C:\proj\" and "C:\proj" are one directory.
        var normalised = TrimTrailingSeparator(Path.GetFullPath(path));

        lock (_appSettings.SyncRoot)
        {
            var existing = _appSettings.Settings.Tree.FirstOrDefault(d =>
                string.Equals(TrimTrailingSeparator(d.Path), normalised, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var model = new DirectoryNodeModel { Path = normalised, NodeId = NewNodeId() };
            _appSettings.Settings.Tree.Add(model);
            _appSettings.Save();
            Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
            return model;
        }
    }

    public bool TryDeleteDirectory(DirectoryNodeModel directory)
    {
        lock (_appSettings.SyncRoot)
        {
            if (directory.Groups.Count != 0 || directory.Sessions.Count != 0)
                return false;

            _appSettings.Settings.Tree.Remove(directory);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    // ── CRUD – Group ──────────────────────────────────────────────────────────

    public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name)
    {
        var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.Path, NodeId = NewNodeId() };
        lock (_appSettings.SyncRoot)
        {
            parent.Groups.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name)
    {
        var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.WorkingDirectory, NodeId = NewNodeId() };
        lock (_appSettings.SyncRoot)
        {
            parent.Groups.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group)
    {
        if (!CanDeleteGroup(group)) return false;

        lock (_appSettings.SyncRoot)
        {
            parent.Groups.Remove(group);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group)
    {
        if (!CanDeleteGroup(group)) return false;

        lock (_appSettings.SyncRoot)
        {
            parent.Groups.Remove(group);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public void RenameGroup(GroupNodeModel group, string newName)
    {
        lock (_appSettings.SyncRoot)
        {
            group.Name = newName;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    // ── CRUD – Session ────────────────────────────────────────────────────────

    public SessionPlacementModel? CreateSessionAfter(string anchorNodeId, string name)
    {
        // The session file is created before the lock (file I/O), like every other creator; an unknown anchor leaves one empty file behind only
        // when the caller passed a bad id, which the remote module rejects first.
        lock (_appSettings.SyncRoot)
        {
            if (!TryFindContainerFor(anchorNodeId, out var directory, out var group, out var sessions, out var index))
                return null;

            var workingDir = group?.WorkingDirectory ?? directory.Path;
            var model      = CreateSessionModel(name, workingDir);
            var preceding  = index > 0 ? sessions[index - 1] : null;

            sessions.Insert(index, model);
            _appSettings.Save();

            Raise(new TreeChangedEventArgs
            {
                Kind             = TreeChangeKind.SessionAdded,
                Session          = model,
                ParentDirectory  = directory,
                ParentGroup      = group,
                Index            = index,
                PrecedingSession = preceding,
            });

            return new SessionPlacementModel(model, directory, group, index);
        }
    }

    /// <summary>
    /// Resolves the insertion point for a new session (settings lock held by the caller). A group or directory NodeId means
    /// the end of its Sessions (FR.1.30; the list also holds worker models, so the end is after the last of them too); a session NodeId means
    /// right after it, or after its top-level supervisor when it is a worker.
    /// </summary>
    private bool TryFindContainerFor(
        string anchorNodeId, out DirectoryNodeModel directory, out GroupNodeModel? group, out List<SessionNodeModel> sessions, out int index)
    {
        directory = null!;
        group     = null;
        sessions  = null!;
        index     = 0;

        var tree = _appSettings.Settings.Tree;

        var dir = tree.FirstOrDefault(d => d.NodeId == anchorNodeId);
        if (dir != null)
        {
            directory = dir;
            sessions  = dir.Sessions;
            index     = sessions.Count;
            return true;
        }

        foreach (var d in tree)
        {
            var grp = FindGroup(d.Groups, anchorNodeId);
            if (grp == null) continue;

            directory = d;
            group     = grp;
            sessions  = grp.Sessions;
            index     = sessions.Count;
            return true;
        }

        var anchorId = ResolveTopLevelSessionId(anchorNodeId);
        if (anchorId == null) return false;

        foreach (var d in tree)
        {
            if (TryLocateSession(d.Groups, d.Sessions, null, anchorId, out group, out sessions, out index))
            {
                directory = d;
                index++;
                return true;
            }
        }

        return false;
    }

    /// <summary>NodeId of the top-level session <paramref name="nodeId"/> belongs to (itself when it is no worker, or its supervisor is missing); null when the session does not exist. Cycle-safe.</summary>
    private string? ResolveTopLevelSessionId(string nodeId)
    {
        var byId = new Dictionary<string, SessionNodeModel>(StringComparer.Ordinal);
        foreach (var d in _appSettings.Settings.Tree)
            IndexSessions(d.Sessions, d.Groups, byId);

        if (!byId.TryGetValue(nodeId, out var current))
            return null;

        var visited = new HashSet<string>(StringComparer.Ordinal) { current.NodeId };
        while (!string.IsNullOrEmpty(current.SupervisorNodeId) && byId.TryGetValue(current.SupervisorNodeId, out var supervisor) && visited.Add(supervisor.NodeId))
            current = supervisor;

        return current.NodeId;
    }

    private static void IndexSessions(List<SessionNodeModel> sessions, List<GroupNodeModel> groups, Dictionary<string, SessionNodeModel> byId)
    {
        foreach (var s in sessions) byId.TryAdd(s.NodeId, s);
        foreach (var g in groups) IndexSessions(g.Sessions, g.Groups, byId);
    }

    private static bool TryLocateSession(
        List<GroupNodeModel> groups, List<SessionNodeModel> sessions, GroupNodeModel? owner,
        string nodeId, out GroupNodeModel? group, out List<SessionNodeModel> container, out int index)
    {
        index = sessions.FindIndex(s => s.NodeId == nodeId);
        if (index >= 0)
        {
            group     = owner;
            container = sessions;
            return true;
        }

        foreach (var g in groups)
            if (TryLocateSession(g.Groups, g.Sessions, g, nodeId, out group, out container, out index))
                return true;

        group     = null;
        container = null!;
        index     = 0;
        return false;
    }

    public SessionNodeModel AddSession(DirectoryNodeModel parent, string name)
    {
        var model = CreateSessionModel(name, parent.Path);
        lock (_appSettings.SyncRoot)
        {
            parent.Sessions.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name)
    {
        var model = CreateSessionModel(name, parent.WorkingDirectory);
        lock (_appSettings.SyncRoot)
        {
            parent.Sessions.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId)
    {
        var model = new SessionNodeModel
        {
            Name = name, FileName = fileName,
            WorkingDirectory = parent.Path,
            ClaudeSessionId  = claudeSessionId,
            ExternalId       = claudeSessionId,
            NodeId           = Guid.NewGuid().ToString("N"),   // an imported session is addressable at once (it used to wait for the next load)
            AgentToken       = Guid.NewGuid().ToString("N"),
        };
        lock (_appSettings.SyncRoot)
        {
            parent.Sessions.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId)
    {
        var model = new SessionNodeModel
        {
            Name = name, FileName = fileName,
            WorkingDirectory = parent.WorkingDirectory,
            ClaudeSessionId  = claudeSessionId,
            ExternalId       = claudeSessionId,
            NodeId           = Guid.NewGuid().ToString("N"),   // an imported session is addressable at once (it used to wait for the next load)
            AgentToken       = Guid.NewGuid().ToString("N"),
        };
        lock (_appSettings.SyncRoot)
        {
            parent.Sessions.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false)
    {
        if (!forceDelete && _fileService.SessionFileExists(session.FileName))
            return false;

        lock (_appSettings.SyncRoot)
        {
            if (forceDelete)
                _fileService.DeleteSessionFile(session.FileName);

            parent.Sessions.Remove(session);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false)
    {
        if (!forceDelete && _fileService.SessionFileExists(session.FileName))
            return false;

        lock (_appSettings.SyncRoot)
        {
            if (forceDelete)
                _fileService.DeleteSessionFile(session.FileName);

            parent.Sessions.Remove(session);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public void RenameSession(SessionNodeModel session, string newName)
    {
        lock (_appSettings.SyncRoot)
        {
            session.Name = newName;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    // ── Model-level move ──────────────────────────────────────────────────────

    public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session)
    {
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                if (dir.Sessions.Remove(session))
                    return dir;

                if (RemoveSessionFromGroupModel(dir.Groups, session))
                    return dir;
            }
            return null;
        }
    }

    public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index)
    {
        lock (_appSettings.SyncRoot)
        {
            index = Math.Clamp(index, 0, parent.Sessions.Count);
            parent.Sessions.Insert(index, session);
            session.WorkingDirectory = parent.Path;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index)
    {
        lock (_appSettings.SyncRoot)
        {
            index = Math.Clamp(index, 0, parent.Sessions.Count);
            parent.Sessions.Insert(index, session);
            session.WorkingDirectory = parent.WorkingDirectory;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    public bool MoveSession(SessionNodeModel session, DirectoryNodeModel targetDirectory, GroupNodeModel? targetGroup, int index)
    {
        lock (_appSettings.SyncRoot)
        {
            // The target may have been deleted since the caller looked it up: moving into a detached node would lose the session.
            if (!_appSettings.Settings.Tree.Contains(targetDirectory) || (targetGroup != null && !ContainsGroup(targetDirectory.Groups, targetGroup)))
                return false;

            var removed = false;
            foreach (var dir in _appSettings.Settings.Tree)
            {
                if (dir.Sessions.Remove(session) || RemoveSessionFromGroupModel(dir.Groups, session))
                {
                    removed = true;
                    break;
                }
            }

            if (!removed)
                return false;

            var list = targetGroup?.Sessions ?? targetDirectory.Sessions;
            list.Insert(Math.Clamp(index, 0, list.Count), session);
            session.WorkingDirectory = targetGroup?.WorkingDirectory ?? targetDirectory.Path;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public bool ReorderGroup(DirectoryNodeModel directory, GroupNodeModel? parentGroup, GroupNodeModel group, int index)
    {
        lock (_appSettings.SyncRoot)
        {
            if (!_appSettings.Settings.Tree.Contains(directory) || (parentGroup != null && !ContainsGroup(directory.Groups, parentGroup)))
                return false;

            var list = parentGroup?.Groups ?? directory.Groups;
            if (!list.Remove(group))
                return false;

            list.Insert(Math.Clamp(index, 0, list.Count), group);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public void RaiseTreeModified() => Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });

    public void RaiseDaemonTreeMutation() => Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.DaemonMutation });

    private static bool ContainsGroup(List<GroupNodeModel> groups, GroupNodeModel target)
    {
        foreach (var g in groups)
        {
            if (ReferenceEquals(g, target) || ContainsGroup(g.Groups, target)) return true;
        }
        return false;
    }

    private static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length > root.Length ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }

    // ── Orchestration helpers ─────────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="workingDir"/> is the registered directory or lies below it. The match respects path separators:
    /// <c>C:\a\bc</c> is not under <c>C:\a\b</c> (ISSUE.013 R9).
    /// </summary>
    internal static bool IsUnderDirectory(string workingDir, string directoryPath)
    {
        var dir  = Path.TrimEndingDirectorySeparator(directoryPath);
        var work = Path.TrimEndingDirectorySeparator(workingDir);
        if (work.Length < dir.Length || !work.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            return false;

        return work.Length == dir.Length || work[dir.Length] is '\\' or '/';
    }

    public SessionNodeModel? CreateSessionForAgent(
        string workingDir, string name, string? group,
        int orchestrationDepth = 0, string? supervisorNodeId = null)
    {
        DirectoryNodeModel? dir;
        GroupNodeModel? targetGroup = null;

        lock (_appSettings.SyncRoot)
        {
            dir = _appSettings.Settings.Tree.FirstOrDefault(d =>
                string.Equals(d.Path, workingDir, StringComparison.OrdinalIgnoreCase));

            if (dir == null)
            {
                dir = _appSettings.Settings.Tree.FirstOrDefault(d =>
                    IsUnderDirectory(workingDir, d.Path));
            }

            if (dir == null)
                return null;

            if (!string.IsNullOrEmpty(group))
            {
                targetGroup = dir.Groups.FirstOrDefault(g =>
                    string.Equals(g.Name, group, StringComparison.OrdinalIgnoreCase))
                    ?? AddGroupModel(dir, group);
            }

            var model = CreateSessionModel(name, dir.Path);
            model.OrchestrationDepth = orchestrationDepth;
            model.SupervisorNodeId   = supervisorNodeId;

            if (targetGroup != null)
                targetGroup.Sessions.Add(model);
            else
                dir.Sessions.Add(model);

            _appSettings.Save();

            Raise(new TreeChangedEventArgs
            {
                Kind            = TreeChangeKind.SessionAdded,
                Session         = model,
                ParentDirectory = dir,
                ParentGroup     = targetGroup,
            });

            return model;
        }
    }

    public List<SessionSummaryModel> BuildAgentSessionSummaries()
    {
        var snapshots   = SnapshotSessions();
        var projectsDirs = new Lazy<IReadOnlyList<string>>(_sessionStatus.GetAllProjectsDirs);

        return snapshots.Select(s => new SessionSummaryModel(
            NodeId:             s.NodeId,
            Name:               s.Name,
            DirectoryLabel:     s.DirectoryLabel,
            WorkingDirectory:   s.WorkingDirectory,
            IsRunning:          s.IsRunning,
            IsResumable:        IsResumable(s, projectsDirs),
            IsFavorite:         s.IsFavorite,
            LastActivityUtc:    _fileService.GetLastWriteTimeUtc(s.FileName),
            OrchestrationDepth: s.OrchestrationDepth)).ToList();
    }

    public List<SessionTagIndexSessionModel> SnapshotSessionsForTagIndex()
    {
        var result = new List<SessionTagIndexSessionModel>();

        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
                CollectTagIndexSessions(dir.Sessions, dir.Groups, dir.Path, result);
        }

        return result;
    }

    private static void CollectTagIndexSessions(
        List<SessionNodeModel> sessions, List<GroupNodeModel> groups, string directoryPath, List<SessionTagIndexSessionModel> result)
    {
        foreach (var s in sessions)
            result.Add(new SessionTagIndexSessionModel(s.NodeId, s.Name, s.SupervisorNodeId, directoryPath));

        foreach (var g in groups)
            CollectTagIndexSessions(g.Sessions, g.Groups, directoryPath, result);
    }

    public IReadOnlyList<SessionSnapshotModel> SnapshotSessionsForListing() => SnapshotSessions();

    public SessionListPageModel QueryAgentSessions(SessionListQueryModel query, string callerNodeId)
    {
        var snapshots    = SnapshotSessions();
        var projectsDirs = new Lazy<IReadOnlyList<string>>(_sessionStatus.GetAllProjectsDirs);

        return new SessionListPageBuilder().Build(
            snapshots,
            callerNodeId,
            query,
            s => _fileService.GetLastWriteTimeUtc(s.FileName),
            s => IsResumable(s, projectsDirs),
            DateTimeOffset.UtcNow);
    }

    public NodeIdResolutionModel ResolveNodeId(string idOrPrefix)
    {
        var snapshots = SnapshotSessions();

        var exact = snapshots.FirstOrDefault(s => string.Equals(s.NodeId, idOrPrefix, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return new NodeIdResolutionModel { Status = NodeIdResolutionStatus.Resolved, NodeId = exact.NodeId };

        if (idOrPrefix.Length < Constants.Agent.NodeIdPrefixMinLength)
            return new NodeIdResolutionModel { Status = NodeIdResolutionStatus.TooShort };

        var matches = snapshots
            .Where(s => s.NodeId.StartsWith(idOrPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => new NodeIdResolutionModel { Status = NodeIdResolutionStatus.NotFound },
            1 => new NodeIdResolutionModel { Status = NodeIdResolutionStatus.Resolved, NodeId = matches[0].NodeId },
            _ => new NodeIdResolutionModel
            {
                Status     = NodeIdResolutionStatus.Ambiguous,
                Matches    = matches.Take(Constants.Agent.AmbiguousNodeIdMaxMatches).ToList(),
                MatchCount = matches.Count,
            },
        };
    }

    public IReadOnlySet<string> CollectAllClaudeSessionIds()
    {
        var ids = new HashSet<string>();

        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
                CollectIds(dir.Sessions, dir.Groups, ids);
        }

        return ids;
    }

    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session)
    {
        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var path = new List<string>();
                if (FindSessionPath(dir.Sessions, dir.Groups, session, path))
                {
                    var dirLabel = _labelService.GetLabel(dir.Path);
                    var treePath = path.Count > 0 ? string.Join(" / ", path) : string.Empty;
                    return (dirLabel, treePath);
                }
            }
        }

        return (string.Empty, string.Empty);
    }

    // ── Running state ─────────────────────────────────────────────────────────

    public void SetSessionRunning(string nodeId, bool isRunning)
    {
        bool changed;
        lock (_runningLock)
        {
            _runningNodes.TryGetValue(nodeId, out var count);
            var next = isRunning ? count + 1 : Math.Max(0, count - 1);

            if (next == 0)
                _runningNodes.Remove(nodeId);
            else
                _runningNodes[nodeId] = next;

            changed = (count == 0) != (next == 0);
            if (changed)
                InvalidateRunningWorkerCounts();
        }

        if (!changed)
            return;

        SessionNodeModel? session = FindModelByNodeId(nodeId);
        Raise(new TreeChangedEventArgs
        {
            Kind    = TreeChangeKind.SessionIsRunningChanged,
            Session = session,
        });
    }

    public bool IsSessionRunning(string nodeId)
    {
        lock (_runningLock)
            return _runningNodes.TryGetValue(nodeId, out var count) && count > 0;
    }

    /// <inheritdoc />
    public int CountRunningWorkers(string nodeId)
    {
        Dictionary<string, int>? cached;
        long generation;
        List<string> running;
        lock (_runningLock)
        {
            cached = _runningWorkerCounts;
            generation = _runningWorkerGeneration;
            running = cached == null ? [.. _runningNodes.Keys] : [];
        }

        if (cached != null)
            return cached.GetValueOrDefault(nodeId);

        // One pass for every supervisor: each running node adds one to all of its supervisors (direct and nested), so the per-row state builds of a tree snapshot cost a lookup each.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (running.Count > 0)
        {
            var byId = new Dictionary<string, SessionNodeModel>(StringComparer.Ordinal);
            lock (_appSettings.SyncRoot)
            {
                foreach (var d in _appSettings.Settings.Tree)
                    IndexSessions(d.Sessions, d.Groups, byId);
            }

            foreach (var id in running)
            {
                if (!byId.TryGetValue(id, out var current))
                    continue;

                var visited = new HashSet<string>(StringComparer.Ordinal) { current.NodeId };
                while (!string.IsNullOrEmpty(current.SupervisorNodeId) && byId.TryGetValue(current.SupervisorNodeId, out var supervisor) && visited.Add(supervisor.NodeId))
                {
                    counts[supervisor.NodeId] = counts.GetValueOrDefault(supervisor.NodeId) + 1;
                    current = supervisor;
                }
            }
        }

        lock (_runningLock)
        {
            if (generation == _runningWorkerGeneration)
                _runningWorkerCounts = counts;
        }

        return counts.GetValueOrDefault(nodeId);
    }

    private void InvalidateRunningWorkerCounts()
    {
        lock (_runningLock)
        {
            _runningWorkerCounts = null;
            _runningWorkerGeneration++;
        }
    }

    public void RaiseSessionSettingsChanged(string nodeId) =>
        SessionSettingsChanged?.Invoke(this, new SessionSettingsChangedEventArgs { NodeId = nodeId });

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Raise(TreeChangedEventArgs args)
    {
        InvalidateRunningWorkerCounts();   // a moved, added or removed worker changes who supervises the running ones
        TreeChanged?.Invoke(this, args);
    }

    private static string NewNodeId() => Guid.NewGuid().ToString("N");

    private static GroupNodeModel? FindGroup(List<GroupNodeModel> groups, string nodeId)
    {
        foreach (var g in groups)
        {
            if (g.NodeId == nodeId) return g;
            var found = FindGroup(g.Groups, nodeId);
            if (found != null) return found;
        }
        return null;
    }

    private SessionNodeModel CreateSessionModel(string name, string workingDir)
    {
        var fileName = _fileService.CreateSessionFile();
        return new SessionNodeModel
        {
            Name             = name,
            FileName         = fileName,
            WorkingDirectory = workingDir,
            NodeId           = Guid.NewGuid().ToString("N"),
            AgentToken       = Guid.NewGuid().ToString("N"),
        };
    }

    private GroupNodeModel AddGroupModel(DirectoryNodeModel parent, string name)
    {
        var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.Path, NodeId = NewNodeId() };
        parent.Groups.Add(model);
        return model;
    }

    private static bool BackfillAgentIdentity(IEnumerable<SessionNodeModel> sessions)
    {
        var dirty = false;
        foreach (var s in sessions)
        {
            if (string.IsNullOrEmpty(s.NodeId))     { s.NodeId     = Guid.NewGuid().ToString("N"); dirty = true; }
            if (string.IsNullOrEmpty(s.AgentToken)) { s.AgentToken = Guid.NewGuid().ToString("N"); dirty = true; }
        }
        return dirty;
    }

    private static bool BackfillAgentIdentityInGroups(GroupNodeModel group)
    {
        var dirty = false;
        if (string.IsNullOrEmpty(group.NodeId)) { group.NodeId = NewNodeId(); dirty = true; }
        dirty |= BackfillAgentIdentity(group.Sessions);
        foreach (var child in group.Groups)
            dirty |= BackfillAgentIdentityInGroups(child);
        return dirty;
    }

    private static SessionNodeModel? FindInSessions(
        List<SessionNodeModel> sessions,
        List<GroupNodeModel> groups,
        Func<SessionNodeModel, bool> predicate)
    {
        foreach (var s in sessions)
        {
            if (predicate(s)) return s;
        }

        foreach (var g in groups)
        {
            var found = FindInSessions(g.Sessions, g.Groups, predicate);
            if (found != null) return found;
        }

        return null;
    }

    private static bool RemoveSessionFromGroupModel(List<GroupNodeModel> groups, SessionNodeModel session)
    {
        foreach (var g in groups)
        {
            if (g.Sessions.Remove(session)) return true;
            if (RemoveSessionFromGroupModel(g.Groups, session)) return true;
        }
        return false;
    }

    private static bool CanDeleteGroup(GroupNodeModel group) =>
        group.Sessions.Count == 0 && group.Groups.Count == 0;

    /// <summary>
    /// Copies the listing fields of every session under the settings lock and does nothing else
    /// there — no file or profile-directory I/O (FR.15.13).
    /// </summary>
    private List<SessionSnapshotModel> SnapshotSessions()
    {
        var result = new List<SessionSnapshotModel>();

        lock (_appSettings.SyncRoot)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var label = _labelService.GetLabel(dir.Path);
                CollectSnapshots(dir.Sessions, dir.Groups, dir.Path, label, new List<string>(), result);
            }
        }

        return result;
    }

    private void CollectSnapshots(
        List<SessionNodeModel> sessions,
        List<GroupNodeModel> groups,
        string directoryPath,
        string directoryLabel,
        List<string> groupPath,
        List<SessionSnapshotModel> result)
    {
        foreach (var s in sessions)
        {
            result.Add(new SessionSnapshotModel
            {
                NodeId             = s.NodeId,
                Name               = s.Name,
                FileName           = s.FileName,
                WorkingDirectory   = s.WorkingDirectory,
                ClaudeSessionId    = s.ClaudeSessionId,
                IsFavorite         = s.IsFavorite,
                OrchestrationDepth = s.OrchestrationDepth,
                SupervisorNodeId   = s.SupervisorNodeId,
                IsRunning          = IsSessionRunning(s.NodeId),
                ProfileLabel       = ProfileLabelOf(s),
                SwitchingToLabel   = string.IsNullOrEmpty(s.PendingProfileId) ? null : ProfileLabelOfId(s.PendingProfileId),
                DirectoryPath      = directoryPath,
                DirectoryLabel     = directoryLabel,
                GroupPath          = groupPath.ToArray(),
            });
        }

        foreach (var g in groups)
        {
            groupPath.Add(g.Name);
            CollectSnapshots(g.Sessions, g.Groups, directoryPath, directoryLabel, groupPath, result);
            groupPath.RemoveAt(groupPath.Count - 1);
        }
    }

    /// <summary>The profile label of a session for listings (ISSUE.013 R5). The caller holds the settings lock; nothing is read from disk.</summary>
    private string ProfileLabelOf(SessionNodeModel s)
    {
        if (!string.IsNullOrEmpty(s.ProfileId))
            return ProfileLabelOfId(s.ProfileId, s.ProfileName);

        if (string.IsNullOrEmpty(s.ProfileName) || s.ProfileName.StartsWith(Constants.Remote.Profiles.SystemPrefix, StringComparison.Ordinal))
            return Constants.ProfileSwitch.DefaultProfileName;

        return s.ProfileName;
    }

    private string ProfileLabelOfId(string profileId, string? fallbackName = null)
    {
        if (string.Equals(profileId, Constants.ProfileSwitch.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            return Constants.ProfileSwitch.DefaultProfileName;

        var stored = _appSettings.Settings.Profiles.FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        return stored?.DisplayName ?? (fallbackName ?? profileId) + Constants.ProfileSwitch.MissingProfileSuffix;
    }

    private bool IsResumable(SessionSnapshotModel s, Lazy<IReadOnlyList<string>> projectsDirs) =>
        s.ClaudeSessionId != null
        && _sessionStatus.IsSessionResumable(s.WorkingDirectory, s.ClaudeSessionId, projectsDirs.Value);

    private static void CollectIds(
        List<SessionNodeModel> sessions, List<GroupNodeModel> groups, HashSet<string> ids)
    {
        foreach (var s in sessions)
        {
            if (s.ExternalId != null)      ids.Add(s.ExternalId);
            if (s.ClaudeSessionId != null) ids.Add(s.ClaudeSessionId);
        }

        foreach (var g in groups)
            CollectIds(g.Sessions, g.Groups, ids);
    }

    private static bool FindSessionPath(
        List<SessionNodeModel> sessions, List<GroupNodeModel> groups,
        SessionNodeModel target, List<string> path)
    {
        foreach (var s in sessions)
        {
            if (s == target) return true;
        }

        foreach (var g in groups)
        {
            path.Add(g.Name);
            if (FindSessionPath(g.Sessions, g.Groups, target, path))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }
}
