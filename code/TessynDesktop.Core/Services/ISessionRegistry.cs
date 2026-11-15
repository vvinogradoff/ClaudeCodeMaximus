using System;
using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>Kind of change reported by <see cref="ISessionRegistry.TreeChanged"/>.</summary>
public enum TreeChangeKind
{
    /// <summary>A new session was added by the registry (e.g., orchestration spawn).</summary>
    SessionAdded,
    /// <summary>A session's IsRunning state changed.</summary>
    SessionIsRunningChanged,
    /// <summary>Tree structure or session data was modified and settings were saved.</summary>
    SettingsModified,
    /// <summary>The full tree was reloaded from settings on startup.</summary>
    Reloaded,
}

/// <summary>Carries the details of a change raised by <see cref="ISessionRegistry"/>.</summary>
public sealed class TreeChangedEventArgs : EventArgs
{
    public TreeChangeKind Kind { get; init; }

    /// <summary>The session involved in the change (if applicable).</summary>
    public SessionNodeModel? Session { get; init; }

    /// <summary>The parent directory of the new session (for <see cref="TreeChangeKind.SessionAdded"/>).</summary>
    public DirectoryNodeModel? ParentDirectory { get; init; }

    /// <summary>The parent group of the new session (for <see cref="TreeChangeKind.SessionAdded"/>), or null when under a directory.</summary>
    public GroupNodeModel? ParentGroup { get; init; }

    /// <summary>Index of the new session within the parent's <c>Sessions</c> list (for <see cref="TreeChangeKind.SessionAdded"/> raised by <see cref="ISessionRegistry.CreateSessionAfter"/>); null when the session was appended (agent spawns).</summary>
    public int? Index { get; init; }

    /// <summary>The session immediately before the new one in the parent's <c>Sessions</c> list, or null when it is first (only set together with <see cref="Index"/>).</summary>
    public SessionNodeModel? PrecedingSession { get; init; }
}

/// <summary>
/// Owns the authoritative tree of <see cref="DirectoryNodeModel"/>/<see cref="GroupNodeModel"/>/<see cref="SessionNodeModel"/>.
/// Persists via <see cref="IAppSettingsService"/>. Raises <see cref="TreeChanged"/> when the tree changes.
/// Services depend on this interface; ViewModels subscribe to <see cref="TreeChanged"/> and re-project.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionRegistry
{
    // ── Change notification ───────────────────────────────────────────────────

    event EventHandler<TreeChangedEventArgs> TreeChanged;

    /// <summary>Raised by <see cref="RaiseSessionSettingsChanged"/> after a daemon method changed a session's settings (favourite, streaming mode).</summary>
    event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged;

    // ── Tree read ─────────────────────────────────────────────────────────────

    /// <summary>The authoritative directory list (same as AppSettings.Settings.Tree).</summary>
    IReadOnlyList<DirectoryNodeModel> Directories { get; }

    // ── UI selection (set by SessionTreeViewModel, read by SchedulerService) ─

    /// <summary>NodeId of the currently selected session in the UI, or null.</summary>
    string? SelectedNodeId { get; set; }

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>Backfills NodeId/AgentToken on any sessions missing them, then saves if dirty. Raises <see cref="TreeChangeKind.Reloaded"/>.</summary>
    void LoadFromSettings();

    // ── Lookups ───────────────────────────────────────────────────────────────

    SessionNodeModel? FindModelByNodeId(string nodeId);
    SessionNodeModel? FindModelByAgentToken(string token);

    /// <summary>Finds the session owning a given session file name (FR.21.9 — resolves an appended entry's session).</summary>
    SessionNodeModel? FindModelByFileName(string fileName);

    /// <summary>Finds a directory node by its stable <see cref="DirectoryNodeModel.NodeId"/>; the search runs under the settings lock. Callers that then read the node must hold <see cref="IAppSettingsService.SyncRoot"/>.</summary>
    DirectoryNodeModel? FindDirectoryByNodeId(string nodeId);

    /// <summary>Finds a group node (at any depth) by its stable <see cref="GroupNodeModel.NodeId"/>; the search runs under the settings lock. Callers that then read the node must hold <see cref="IAppSettingsService.SyncRoot"/>.</summary>
    GroupNodeModel? FindGroupByNodeId(string nodeId);

    // ── CRUD – Directory ──────────────────────────────────────────────────────

    DirectoryNodeModel AddDirectory(string path);
    bool TryDeleteDirectory(DirectoryNodeModel directory);

    // ── CRUD – Group ──────────────────────────────────────────────────────────

    GroupNodeModel AddGroup(DirectoryNodeModel parent, string name);
    GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name);
    bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group);
    bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group);
    void RenameGroup(GroupNodeModel group, string newName);

    // ── CRUD – Session ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a session positioned relative to <paramref name="anchorNodeId"/> in ONE step (one lock, one <c>Save()</c>):
    /// a session anchor (a worker resolves to its top-level supervisor) puts the new session right after it in the same
    /// container's <c>Sessions</c>; a group or directory anchor puts it at index 0 of that container. Raises
    /// <see cref="TreeChangeKind.SessionAdded"/> carrying the parent and the index. Returns null when the anchor is not found.
    /// </summary>
    SessionPlacementModel? CreateSessionAfter(string anchorNodeId, string name);

    SessionNodeModel AddSession(DirectoryNodeModel parent, string name);
    SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name);
    SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId);
    SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId);
    bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false);
    bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false);
    void RenameSession(SessionNodeModel session, string newName);

    // ── Model-level move ──────────────────────────────────────────────────────

    /// <summary>Removes the session from its current model container. Returns the parent directory (or null if not found).</summary>
    DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session);
    void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index);
    void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index);

    /// <summary>
    /// Moves <paramref name="session"/> in ONE step (one lock, one <c>Save()</c>, one <see cref="TreeChangeKind.SettingsModified"/>) into
    /// <paramref name="targetGroup"/>, or into <paramref name="targetDirectory"/> when the group is null, at <paramref name="index"/> of the
    /// target's <c>Sessions</c> list as it is once the session has left its old container (clamped). The session's working directory follows
    /// the target. Returns false, changing nothing, when the session is not in the tree. The git-origin rule is the caller's (<c>TreeMoveRules</c>).
    /// </summary>
    bool MoveSession(SessionNodeModel session, DirectoryNodeModel targetDirectory, GroupNodeModel? targetGroup, int index);

    /// <summary>Raises <see cref="TreeChangeKind.SettingsModified"/> after a change to tree data that bypassed the registry (a directory's local tool profile).</summary>
    void RaiseTreeModified();

    // ── Orchestration helpers ─────────────────────────────────────────────────

    /// <summary>Creates a new session node for agent orchestration (spawn_session). Thread-safe. Raises <see cref="TreeChangeKind.SessionAdded"/>.</summary>
    SessionNodeModel? CreateSessionForAgent(string workingDir, string name, string? group,
        int orchestrationDepth = 0, string? supervisorNodeId = null);

    /// <summary>
    /// Every session with its Last Activity (session file last-write time) and resumability (FR.21.8, FR.15.13).
    /// The settings lock is held only while session fields are copied.
    /// </summary>
    List<SessionSummaryModel> BuildAgentSessionSummaries();

    /// <summary>
    /// Every session (Worker Sessions included) as a flat snapshot for the Session Tag Index (FR.24.13).
    /// The settings lock is held only while fields are copied.
    /// </summary>
    List<SessionTagIndexSessionModel> SnapshotSessionsForTagIndex();

    /// <summary>
    /// Every session (Worker Sessions included) as the lock-free snapshot the <c>list_sessions</c> listing uses
    /// (FR.15.13), for tools that need Short Node Ids and the <c>You:</c> line (FR.24.15). The settings lock is
    /// held only while fields are copied; no file is touched.
    /// </summary>
    IReadOnlyList<SessionSnapshotModel> SnapshotSessionsForListing();

    /// <summary>One bounded page of the <c>list_sessions</c> listing for <paramref name="callerNodeId"/> (FR.15.13).</summary>
    SessionListPageModel QueryAgentSessions(SessionListQueryModel query, string callerNodeId);

    /// <summary>Resolves a full NodeId or a prefix of at least <see cref="Constants.Agent.NodeIdPrefixMinLength"/> characters to one session (FR.15.13).</summary>
    NodeIdResolutionModel ResolveNodeId(string idOrPrefix);
    IReadOnlySet<string> CollectAllClaudeSessionIds();
    (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session);

    // ── Running state ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reference-counted: <c>true</c> adds one running activity, <c>false</c> removes one (never below zero). The session
    /// counts as running while any activity is open, and <see cref="TreeChangeKind.SessionIsRunningChanged"/> is raised
    /// only when the count moves between 0 and 1. Thread-safe.
    /// </summary>
    void SetSessionRunning(string nodeId, bool isRunning);

    /// <summary>Raises <see cref="SessionSettingsChanged"/> for <paramref name="nodeId"/>. Thread-safe.</summary>
    void RaiseSessionSettingsChanged(string nodeId);

    /// <summary>Returns true while a claude process is running for the given node.</summary>
    bool IsSessionRunning(string nodeId);
}
