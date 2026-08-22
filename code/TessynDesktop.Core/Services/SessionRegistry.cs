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
/// Thread-safe: model mutations take a lock; the running-state dictionary uses concurrent primitives.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRegistry : ISessionRegistry
{
    private static readonly ILogger _log = Log.ForContext<SessionRegistry>();

    private readonly IAppSettingsService          _appSettings;
    private readonly ISessionFileService          _fileService;
    private readonly IDirectoryLabelService       _labelService;
    private readonly IClaudeSessionStatusService  _sessionStatus;

    // Running state: NodeId → bool (transient, never persisted).
    private readonly ConcurrentDictionary<string, bool> _runningNodes = new();

    private readonly object _lock = new();

    public event EventHandler<TreeChangedEventArgs>? TreeChanged;

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
        lock (_lock)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
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
        lock (_lock)
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
        lock (_lock)
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
        lock (_lock)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var found = FindInSessions(dir.Sessions, dir.Groups, s => s.FileName == fileName);
                if (found != null) return found;
            }
            return null;
        }
    }

    // ── CRUD – Directory ──────────────────────────────────────────────────────

    public DirectoryNodeModel AddDirectory(string path)
    {
        var normalised = Path.GetFullPath(path);

        lock (_lock)
        {
            var existing = _appSettings.Settings.Tree.FirstOrDefault(d =>
                string.Equals(d.Path, normalised, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var model = new DirectoryNodeModel { Path = normalised };
            _appSettings.Settings.Tree.Add(model);
            _appSettings.Save();
            Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
            return model;
        }
    }

    public bool TryDeleteDirectory(DirectoryNodeModel directory)
    {
        lock (_lock)
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
        var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.Path };
        lock (_lock)
        {
            parent.Groups.Add(model);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return model;
    }

    public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name)
    {
        var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.WorkingDirectory };
        lock (_lock)
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

        lock (_lock)
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

        lock (_lock)
        {
            parent.Groups.Remove(group);
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
        return true;
    }

    public void RenameGroup(GroupNodeModel group, string newName)
    {
        lock (_lock)
        {
            group.Name = newName;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    // ── CRUD – Session ────────────────────────────────────────────────────────

    public SessionNodeModel AddSession(DirectoryNodeModel parent, string name)
    {
        var model = CreateSessionModel(name, parent.Path);
        lock (_lock)
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
        lock (_lock)
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
        };
        lock (_lock)
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
        };
        lock (_lock)
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

        lock (_lock)
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

        lock (_lock)
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
        lock (_lock)
        {
            session.Name = newName;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    // ── Model-level move ──────────────────────────────────────────────────────

    public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session)
    {
        lock (_lock)
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
        lock (_lock)
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
        lock (_lock)
        {
            index = Math.Clamp(index, 0, parent.Sessions.Count);
            parent.Sessions.Insert(index, session);
            session.WorkingDirectory = parent.WorkingDirectory;
            _appSettings.Save();
        }

        Raise(new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
    }

    // ── Orchestration helpers ─────────────────────────────────────────────────

    public SessionNodeModel? CreateSessionForAgent(
        string workingDir, string name, string? group,
        int orchestrationDepth = 0, string? supervisorNodeId = null)
    {
        DirectoryNodeModel? dir;
        GroupNodeModel? targetGroup = null;

        lock (_lock)
        {
            dir = _appSettings.Settings.Tree.FirstOrDefault(d =>
                string.Equals(d.Path, workingDir, StringComparison.OrdinalIgnoreCase));

            if (dir == null)
            {
                dir = _appSettings.Settings.Tree.FirstOrDefault(d =>
                    workingDir.StartsWith(d.Path, StringComparison.OrdinalIgnoreCase));
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
        var result = new List<SessionSummaryModel>();

        lock (_lock)
        {
            foreach (var dir in _appSettings.Settings.Tree)
            {
                var label = _labelService.GetLabel(dir.Path);
                CollectSummaries(dir.Sessions, dir.Groups, label, result);
            }
        }

        return result;
    }

    public IReadOnlySet<string> CollectAllClaudeSessionIds()
    {
        var ids = new HashSet<string>();

        lock (_lock)
        {
            foreach (var dir in _appSettings.Settings.Tree)
                CollectIds(dir.Sessions, dir.Groups, ids);
        }

        return ids;
    }

    public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session)
    {
        lock (_lock)
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
        if (isRunning)
            _runningNodes[nodeId] = true;
        else
            _runningNodes.TryRemove(nodeId, out _);

        SessionNodeModel? session = FindModelByNodeId(nodeId);
        Raise(new TreeChangedEventArgs
        {
            Kind    = TreeChangeKind.SessionIsRunningChanged,
            Session = session,
        });
    }

    public bool IsSessionRunning(string nodeId) =>
        _runningNodes.TryGetValue(nodeId, out var v) && v;

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Raise(TreeChangedEventArgs args) => TreeChanged?.Invoke(this, args);

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
        var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.Path };
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
        var dirty = BackfillAgentIdentity(group.Sessions);
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

    private void CollectSummaries(
        List<SessionNodeModel> sessions,
        List<GroupNodeModel> groups,
        string directoryLabel,
        List<SessionSummaryModel> result)
    {
        foreach (var s in sessions)
        {
            result.Add(BuildSummary(s, directoryLabel));
        }

        foreach (var g in groups)
            CollectSummaries(g.Sessions, g.Groups, directoryLabel, result);
    }

    private SessionSummaryModel BuildSummary(SessionNodeModel s, string directoryLabel)
    {
        var isResumable = s.ClaudeSessionId != null
            && _sessionStatus.IsSessionResumable(s.WorkingDirectory, s.ClaudeSessionId);

        string? lastPrompt = null;
        DateTimeOffset? lastActivityUtc = null;
        try
        {
            var entries = _fileService.ReadEntries(s.FileName);
            var lastUser = entries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser);
            if (lastUser != null)
                lastPrompt = lastUser.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

            // Last entry of any role (FR.21.8) — used by the Telegram Bridge's /sessions command
            // as a cheap activity signal; not the same 24h-recency rule the UI dropdown uses.
            if (entries.Count > 0)
                lastActivityUtc = entries[^1].Timestamp;
        }
        catch { /* session file may not exist — omit */ }

        return new SessionSummaryModel(
            NodeId:           s.NodeId,
            Name:             s.Name,
            DirectoryLabel:   directoryLabel,
            WorkingDirectory: s.WorkingDirectory,
            IsRunning:        IsSessionRunning(s.NodeId),
            IsResumable:      isResumable,
            LastPrompt:       lastPrompt,
            IsFavorite:       s.IsFavorite,
            LastActivityUtc:  lastActivityUtc);
    }

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
