using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.Models.Agent;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionTreeViewModel : ViewModelBase
{
	private readonly IDaemonClient _daemon;
	private readonly IAppSettingsService _appSettings;
	private readonly IDirectoryLabelService _labelService;
	private readonly ISessionFileService _sessionFileService;
	private readonly IClaudeSessionStatusService _claudeSessionStatus;
	private readonly ISessionSearchService _searchService;
	private readonly IGitOriginService _gitOriginService;
	private readonly IDraftService _draftService;
	private readonly ITessynDaemonService? _daemonService;
	private string _searchText = string.Empty;
	private SessionNodeViewModel? _selectedSession;
	private Dictionary<int, string>? _daemonIdToExternalIdCache;
	private ViewModelBase? _selectedTreeItem;
	private CancellationTokenSource? _searchCts;

	// ── Sub-session expand/collapse state ───────────────────────────────────
	private SessionNodeViewModel? _expandedSupervisor;

	// ── Move mode state ──────────────────────────────────────────────────────
	private bool _isMoveModeActive;
	private SessionNodeViewModel? _movingSession;
	private ViewModelBase? _moveOriginalParent;
	private int _moveOriginalIndex;

	public bool IsMoveModeActive
	{
		get => _isMoveModeActive;
		private set => this.RaiseAndSetIfChanged(ref _isMoveModeActive, value);
	}

	public SessionNodeViewModel? MovingSession
	{
		get => _movingSession;
		private set => this.RaiseAndSetIfChanged(ref _movingSession, value);
	}

	public ObservableCollection<DirectoryNodeViewModel> Directories { get; } = [];

	public string SearchText
	{
		get => _searchText;
		set => this.RaiseAndSetIfChanged(ref _searchText, value);
	}

	public SessionNodeViewModel? SelectedSession
	{
		get => _selectedSession;
		set => this.RaiseAndSetIfChanged(ref _selectedSession, value);
	}

	/// <summary>Tracks the last selected tree item (directory, group, or session) for context-aware hotkeys.</summary>
	public ViewModelBase? SelectedTreeItem
	{
		get => _selectedTreeItem;
		set => this.RaiseAndSetIfChanged(ref _selectedTreeItem, value);
	}

	/// <summary>
	/// Resolves the working directory and import target key from the currently selected tree item.
	/// </summary>
	public (string? WorkingDirectory, string? TargetKey) GetSelectedImportContext()
	{
		return SelectedTreeItem switch
		{
			DirectoryNodeViewModel dir => (dir.Path, dir.Path),
			GroupNodeViewModel grp => (grp.WorkingDirectory, $"{grp.WorkingDirectory}|{grp.Name}"),
			SessionNodeViewModel session => (session.Model.WorkingDirectory, session.Model.WorkingDirectory),
			_ => (null, null),
		};
	}

	public ReactiveCommand<Unit, Unit> AddDirectoryCommand { get; }

	public SessionTreeViewModel(
		IDaemonClient daemon,
		IAppSettingsService appSettings,
		IDirectoryLabelService labelService,
		ISessionFileService sessionFileService,
		IClaudeSessionStatusService claudeSessionStatus,
		ISessionSearchService searchService,
		IGitOriginService gitOriginService,
		IDraftService draftService,
		ITessynDaemonService? daemonService = null)
	{
		_daemon = daemon;
		_appSettings = appSettings;
		_labelService = labelService;
		_sessionFileService = sessionFileService;
		_claudeSessionStatus = claudeSessionStatus;
		_searchService = searchService;
		_gitOriginService = gitOriginService;
		_draftService = draftService;
		_daemonService = daemonService;

		AddDirectoryCommand = ReactiveCommand.Create(PromptAddDirectory);

		// Registry backfills NodeId/AgentToken, then we project models → VMs.
		_daemon.LoadFromSettings();
		LoadFromSettings();
		RefreshGitOrigins();
		RefreshSessionResumability();
		RefreshLastPromptTimes();

		// Subscribe to registry changes so service-initiated mutations (orchestration spawn,
		// IsRunning changes) are reflected in the VM tree without the VM touching the model directly.
		_daemon.TreeChanged += OnRegistryTreeChanged;

		this.WhenAnyValue(x => x.SearchText)
			.Throttle(TimeSpan.FromMilliseconds(300))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => ApplySearchFilter());

		this.WhenAnyValue(x => x.SelectedSession)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(session =>
			{
				_daemon.SelectedNodeId = session?.Model.NodeId;
				UpdateSubSessionExpansion(session);
			});

		var timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(Constants.ClaudeSessions.StatusCheckIntervalSeconds),
		};
		timer.Tick += (_, _) => RefreshSessionResumability();
		timer.Start();
	}

	// ── Registry.TreeChanged handler ─────────────────────────────────────────

	private void OnRegistryTreeChanged(object? sender, TreeChangedEventArgs e)
	{
		switch (e.Kind)
		{
			case TreeChangeKind.SessionAdded when e.Session != null:
				Dispatcher.UIThread.Post(() => HandleRegistrySessionAdded(e.Session, e.ParentDirectory, e.ParentGroup));
				break;
			case TreeChangeKind.SessionIsRunningChanged when e.Session != null:
				Dispatcher.UIThread.Post(() => HandleRegistryIsRunningChanged(e.Session));
				break;
			// SettingsModified: VM was already updated surgically by SessionTreeViewModel methods.
			// Reloaded: handled by the constructor's LoadFromSettings() call.
		}
	}

	private void HandleRegistrySessionAdded(
		SessionNodeModel model, DirectoryNodeModel? parentDirectory, GroupNodeModel? parentGroup)
	{
		var dirVm = Directories.FirstOrDefault(d =>
			parentDirectory != null
				? d.Model == parentDirectory
				: string.Equals(d.Path, model.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
				  || model.WorkingDirectory.StartsWith(d.Path, StringComparison.OrdinalIgnoreCase));

		if (dirVm == null) return;

		var sessionVm = new SessionNodeViewModel(model);

		if (!string.IsNullOrEmpty(model.SupervisorNodeId))
		{
			var supervisorVm = FindNodeVmByNodeId(model.SupervisorNodeId);
			if (supervisorVm != null)
			{
				supervisorVm.SubSessions.Add(sessionVm);
				return;
			}
		}

		if (parentGroup != null)
		{
			// Find the GroupNodeViewModel matching the group model.
			var grpVm = FindGroupVmInDir(dirVm, parentGroup);
			if (grpVm != null)
			{
				grpVm.Children.Add(sessionVm);
				return;
			}
		}

		dirVm.Children.Add(sessionVm);
	}

	private static GroupNodeViewModel? FindGroupVmInDir(DirectoryNodeViewModel dir, GroupNodeModel groupModel)
	{
		foreach (var child in dir.Children)
		{
			if (child is GroupNodeViewModel grp)
			{
				if (grp.Model == groupModel) return grp;
				var nested = FindGroupVmInGroup(grp, groupModel);
				if (nested != null) return nested;
			}
		}
		return null;
	}

	private static GroupNodeViewModel? FindGroupVmInGroup(GroupNodeViewModel parent, GroupNodeModel groupModel)
	{
		foreach (var child in parent.Children)
		{
			if (child is GroupNodeViewModel grp)
			{
				if (grp.Model == groupModel) return grp;
				var nested = FindGroupVmInGroup(grp, groupModel);
				if (nested != null) return nested;
			}
		}
		return null;
	}

	private void HandleRegistryIsRunningChanged(SessionNodeModel model)
	{
		var vm = FindNodeVmByNodeId(model.NodeId);
		if (vm != null)
			vm.IsRunning = _daemon.IsSessionRunning(model.NodeId);
	}

	// --- Add operations ---

	public DirectoryNodeViewModel AddDirectory(string path)
	{
		var normalised = Path.GetFullPath(path);
		var existing = Directories.FirstOrDefault(d =>
			string.Equals(d.Path, normalised, StringComparison.OrdinalIgnoreCase));

		if (existing != null)
			return existing;

		var model = _daemon.AddDirectory(normalised); // creates model + saves
		var vm = new DirectoryNodeViewModel(model, _labelService);
		Directories.Add(vm);
		return vm;
	}

	public GroupNodeViewModel AddGroup(DirectoryNodeViewModel parent, string name)
	{
		var model = _daemon.AddGroup(parent.Model, name); // creates model + saves
		var vm = new GroupNodeViewModel(model);
		parent.Children.Add(vm); // VM-only; model was added to parent.Model.Groups by registry
		return vm;
	}

	public GroupNodeViewModel AddGroupToGroup(GroupNodeViewModel parent, string name)
	{
		var model = _daemon.AddGroupToGroup(parent.Model, name); // creates model + saves
		var vm = new GroupNodeViewModel(model);
		parent.Children.Add(vm); // VM-only
		return vm;
	}

	public SessionNodeViewModel AddSession(DirectoryNodeViewModel parent, string name)
	{
		var model = _daemon.AddSession(parent.Model, name); // creates file + model + saves
		var vm = new SessionNodeViewModel(model);
		parent.Children.Add(vm); // VM-only; model was added to parent.Model.Sessions by registry
		return vm;
	}

	public SessionNodeViewModel AddSessionToGroup(GroupNodeViewModel parent, string name)
	{
		var model = _daemon.AddSessionToGroup(parent.Model, name); // creates file + model + saves
		var vm = new SessionNodeViewModel(model);
		parent.Children.Add(vm); // VM-only
		return vm;
	}

	private bool UseDaemon => _appSettings.Settings.UseTessynDaemon && _daemonService != null;

	// --- Import operations ---

	/// <summary>
	/// Creates a session node from imported data and adds it to the tree.
	/// Used by session import (FR.13.11).
	/// </summary>
	public SessionNodeViewModel ImportSession(
		DirectoryNodeViewModel parent, string name, string fileName, string claudeSessionId)
	{
		var model = _daemon.ImportSession(parent.Model, name, fileName, claudeSessionId);
		var vm = new SessionNodeViewModel(model);
		parent.Children.Add(vm);
		return vm;
	}

	/// <summary>
	/// Creates a session node from imported data and adds it to a group.
	/// </summary>
	public SessionNodeViewModel ImportSessionToGroup(
		GroupNodeViewModel parent, string name, string fileName, string claudeSessionId)
	{
		var model = _daemon.ImportSessionToGroup(parent.Model, name, fileName, claudeSessionId);
		var vm = new SessionNodeViewModel(model);
		parent.Children.Add(vm);
		return vm;
	}

	/// <summary>
	/// Collects all session identifiers from the entire tree for duplicate detection.
	/// Includes both ExternalId and ClaudeSessionId for backward compatibility.
	/// </summary>
	public IReadOnlySet<string> CollectAllClaudeSessionIds() => _daemon.CollectAllClaudeSessionIds();

	/// <summary>
	/// Builds a flat list of all import targets (directories + groups) with hierarchy depth.
	/// </summary>
	public IReadOnlyList<ImportTargetModel> BuildImportTargets()
	{
		var targets = new List<ImportTargetModel>();
		foreach (var dir in Directories)
		{
			targets.Add(new ImportTargetModel
			{
				DisplayName = dir.Label,
				WorkingDirectory = dir.Path,
				Depth = 0,
				IsDirectory = true,
				Key = dir.Path,
			});
			AddGroupTargets(dir.Children, dir.Path, 1, targets);
		}
		return targets;
	}

	/// <summary>
	/// Builds a list of source directories only (for session discovery).
	/// </summary>
	public IReadOnlyList<ImportTargetModel> BuildSourceDirectories()
	{
		return Directories.Select(d => new ImportTargetModel
		{
			DisplayName = d.Label,
			WorkingDirectory = d.Path,
			Depth = 0,
			IsDirectory = true,
			Key = d.Path,
		}).ToList();
	}

	/// <summary>
	/// Finds the directory or group node matching an ImportTargetModel key.
	/// Returns (dirNode, grpNode) — one will be non-null.
	/// </summary>
	public (DirectoryNodeViewModel? Dir, GroupNodeViewModel? Grp) FindTargetByKey(string key)
	{
		foreach (var dir in Directories)
		{
			if (string.Equals(dir.Path, key, StringComparison.OrdinalIgnoreCase))
				return (dir, null);

			var grp = FindGroupByKey(dir.Children, key);
			if (grp != null)
				return (null, grp);
		}
		return (null, null);
	}

	private static void AddGroupTargets(
		ObservableCollection<ViewModelBase> children, string workingDir, int depth, List<ImportTargetModel> targets)
	{
		foreach (var child in children)
		{
			if (child is not GroupNodeViewModel grp)
				continue;

			targets.Add(new ImportTargetModel
			{
				DisplayName = grp.Name,
				WorkingDirectory = workingDir,
				Depth = depth,
				IsDirectory = false,
				Key = $"{workingDir}|{grp.Name}",
			});
			AddGroupTargets(grp.Children, workingDir, depth + 1, targets);
		}
	}

	private static GroupNodeViewModel? FindGroupByKey(ObservableCollection<ViewModelBase> children, string key)
	{
		foreach (var child in children)
		{
			if (child is not GroupNodeViewModel grp)
				continue;

			if (key == $"{grp.WorkingDirectory}|{grp.Name}")
				return grp;

			var nested = FindGroupByKey(grp.Children, key);
			if (nested != null)
				return nested;
		}
		return null;
	}

	// --- Rename operations ---

	public void RenameGroup(GroupNodeViewModel group, string newName)
	{
		_daemon.RenameGroup(group.Model, newName); // updates Model.Name + saves
		group.Name = newName; // refreshes VM binding (also sets Model.Name again, idempotent)
	}

	public void RenameSession(SessionNodeViewModel session, string newName)
	{
		_daemon.RenameSession(session.Model, newName); // updates Model.Name + saves
		session.Name = newName; // refreshes VM binding
	}

	// --- Delete operations ---

	public bool TryDeleteDirectory(DirectoryNodeViewModel directory)
	{
		if (!directory.CanDelete)
			return false;

		if (!_daemon.TryDeleteDirectory(directory.Model)) // removes from model tree + saves
			return false;

		Directories.Remove(directory);
		return true;
	}

	public bool TryDeleteGroup(DirectoryNodeViewModel parent, GroupNodeViewModel group)
	{
		if (!group.CanDelete)
			return false;

		if (!_daemon.TryDeleteGroup(parent.Model, group.Model)) // removes from model + saves
			return false;

		parent.Children.Remove(group);
		return true;
	}

	public bool TryDeleteGroupFromGroup(GroupNodeViewModel parent, GroupNodeViewModel group)
	{
		if (!group.CanDelete)
			return false;

		if (!_daemon.TryDeleteGroupFromGroup(parent.Model, group.Model)) // removes from model + saves
			return false;

		parent.Children.Remove(group);
		return true;
	}

	public bool TryDeleteSession(DirectoryNodeViewModel parent, SessionNodeViewModel session, bool forceDelete = false)
	{
		if (!_daemon.TryDeleteSession(parent.Model, session.Model, forceDelete)) // deletes file + removes model + saves
			return false;

		parent.Children.Remove(session);
		return true;
	}

	public bool TryDeleteSessionFromGroup(GroupNodeViewModel parent, SessionNodeViewModel session, bool forceDelete = false)
	{
		if (!_daemon.TryDeleteSessionFromGroup(parent.Model, session.Model, forceDelete)) // deletes file + removes model + saves
			return false;

		parent.Children.Remove(session);
		return true;
	}

	// --- Git origin ---

	public void RefreshGitOrigins()
	{
		foreach (var dir in Directories)
			dir.GitOrigin = _gitOriginService.GetOriginUrl(dir.Path);
	}

	/// <summary>
	/// Returns the git origin for the directory that owns the given node.
	/// Returns null if the session is not under a git-controlled directory.
	/// </summary>
	public string? GetGitOriginForNode(ViewModelBase node)
	{
		foreach (var dir in Directories)
		{
			if (dir == node || NodeExistsInChildren(dir.Children, node))
				return dir.GitOrigin;
		}
		return null;
	}

	private static bool NodeExistsInChildren(ObservableCollection<ViewModelBase> children, ViewModelBase target)
	{
		foreach (var child in children)
		{
			if (child == target)
				return true;
			if (child is GroupNodeViewModel grp && NodeExistsInChildren(grp.Children, target))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Checks whether a session with the given source git origin can be dropped/moved
	/// onto the given target node.
	/// </summary>
	public bool CanMoveSessionTo(string? sourceGitOrigin, ViewModelBase target)
	{
		// No git origin on source → unrestricted
		if (sourceGitOrigin is null)
			return target is DirectoryNodeViewModel or GroupNodeViewModel;

		if (target is not DirectoryNodeViewModel and not GroupNodeViewModel)
			return false;

		var targetOrigin = GetGitOriginForNode(target);
		// Target has no git origin → cannot accept git-origined sessions
		if (targetOrigin is null)
			return false;

		return string.Equals(sourceGitOrigin, targetOrigin, System.StringComparison.OrdinalIgnoreCase);
	}

	// --- Move operations ---

	/// <summary>
	/// Removes a session from its current parent (directory or group).
	/// Returns the parent VM and index for undo capability.
	/// </summary>
	public (ViewModelBase? parent, int index) RemoveSessionFromTree(SessionNodeViewModel session)
	{
		// Remove from model tree via registry.
		_daemon.RemoveSessionFromModel(session.Model);

		// Remove from VM collections and return the parent VM + index.
		foreach (var dir in Directories)
		{
			var idx = dir.Children.IndexOf(session);
			if (idx >= 0)
			{
				dir.Children.RemoveAt(idx);
				return (dir, idx);
			}

			var result = RemoveSessionFromGroupVmChildren(dir.Children, session);
			if (result.parent is not null)
				return result;
		}
		return (null, -1);
	}

	private static (ViewModelBase? parent, int index) RemoveSessionFromGroupVmChildren(
		ObservableCollection<ViewModelBase> children, SessionNodeViewModel session)
	{
		foreach (var child in children)
		{
			if (child is not GroupNodeViewModel grp)
				continue;

			var idx = grp.Children.IndexOf(session);
			if (idx >= 0)
			{
				grp.Children.RemoveAt(idx);
				return (grp, idx);
			}

			var result = RemoveSessionFromGroupVmChildren(grp.Children, session);
			if (result.parent is not null)
				return result;
		}
		return (null, -1);
	}

	/// <summary>
	/// Inserts a session into a target parent at the given index (VM + model).
	/// </summary>
	public void InsertSessionAt(ViewModelBase parent, SessionNodeViewModel session, int index)
	{
		switch (parent)
		{
			case DirectoryNodeViewModel dir:
				index = Math.Clamp(index, 0, dir.Children.Count);
				dir.Children.Insert(index, session);
				_daemon.InsertSessionIntoDirectory(dir.Model, session.Model, index);
				break;
			case GroupNodeViewModel grp:
				index = Math.Clamp(index, 0, grp.Children.Count);
				grp.Children.Insert(index, session);
				_daemon.InsertSessionIntoGroup(grp.Model, session.Model, index);
				break;
		}
	}

	/// <summary>
	/// Moves a session to a target position. Target can be a Directory, Group, or another Session.
	/// When target is a session, inserts below it. When target is a container, inserts at position 0.
	/// </summary>
	public bool MoveSessionTo(SessionNodeViewModel session, ViewModelBase target)
	{
		var sourceOrigin = GetGitOriginForNode(session);

		ViewModelBase container;
		int insertIndex;

		if (target is SessionNodeViewModel targetSession)
		{
			// Find the parent of the target session and insert below it
			var targetParentInfo = FindParentOf(targetSession);
			if (targetParentInfo.parent is null)
				return false;
			container = targetParentInfo.parent;
			insertIndex = targetParentInfo.index + 1;
		}
		else if (target is DirectoryNodeViewModel or GroupNodeViewModel)
		{
			container = target;
			insertIndex = 0;
		}
		else
			return false;

		if (!CanMoveSessionTo(sourceOrigin, container))
			return false;

		RemoveSessionFromTree(session);

		// Recalculate insert index after removal (might have shifted)
		if (target is SessionNodeViewModel ts)
		{
			var newParent = FindParentOf(ts);
			if (newParent.parent is null)
				return false;
			container = newParent.parent;
			insertIndex = newParent.index + 1;
		}

		InsertSessionAt(container, session, insertIndex); // saves via registry
		return true;
	}

	/// <summary>Finds the parent container and index of a given node.</summary>
	public (ViewModelBase? parent, int index) FindParentOf(ViewModelBase node)
	{
		foreach (var dir in Directories)
		{
			var idx = dir.Children.IndexOf(node);
			if (idx >= 0)
				return (dir, idx);

			var result = FindParentInGroupChildren(dir.Children, node);
			if (result.parent is not null)
				return result;
		}
		return (null, -1);
	}

	private static (ViewModelBase? parent, int index) FindParentInGroupChildren(
		ObservableCollection<ViewModelBase> children, ViewModelBase node)
	{
		foreach (var child in children)
		{
			if (child is not GroupNodeViewModel grp)
				continue;

			var idx = grp.Children.IndexOf(node);
			if (idx >= 0)
				return (grp, idx);

			var result = FindParentInGroupChildren(grp.Children, node);
			if (result.parent is not null)
				return result;
		}
		return (null, -1);
	}

	// --- Move mode ---

	public void StartMoveMode(SessionNodeViewModel session)
	{
		if (IsMoveModeActive)
			CancelMoveMode();

		var (parent, index) = FindParentOf(session);
		if (parent is null)
			return;

		MovingSession = session;
		_moveOriginalParent = parent;
		_moveOriginalIndex = index;
		session.IsBeingMoved = true;
		IsMoveModeActive = true;
	}

	public void CancelMoveMode()
	{
		if (!IsMoveModeActive || MovingSession is null)
			return;

		// Restore to original position
		RemoveSessionFromTree(MovingSession);
		InsertSessionAt(_moveOriginalParent!, MovingSession, _moveOriginalIndex); // saves via registry

		MovingSession.IsBeingMoved = false;
		MovingSession = null;
		_moveOriginalParent = null;
		IsMoveModeActive = false;
	}

	public bool ConfirmMoveMode()
	{
		if (!IsMoveModeActive || MovingSession is null)
			return false;

		MovingSession.IsBeingMoved = false;
		MovingSession = null;
		_moveOriginalParent = null;
		IsMoveModeActive = false;
		_appSettings.Save(); // no registry mutation here; just persist current position
		return true;
	}

	/// <summary>
	/// During move mode, relocates the moving session to be adjacent to the
	/// currently selected tree item. If the target is invalid, restores to original position.
	/// </summary>
	public void UpdateMovePosition(ViewModelBase? selectedItem)
	{
		if (!IsMoveModeActive || MovingSession is null || selectedItem is null)
			return;

		// Don't move relative to self
		if (selectedItem == MovingSession)
			return;

		var sourceOrigin = GetGitOriginForNode(_moveOriginalParent!);

		ViewModelBase targetContainer;
		int targetIndex;

		if (selectedItem is SessionNodeViewModel targetSession)
		{
			var parentInfo = FindParentOf(targetSession);
			if (parentInfo.parent is null)
				return;
			targetContainer = parentInfo.parent;
			targetIndex = parentInfo.index + 1;
		}
		else if (selectedItem is DirectoryNodeViewModel or GroupNodeViewModel)
		{
			targetContainer = selectedItem;
			targetIndex = 0;
		}
		else
			return;

		if (!CanMoveSessionTo(sourceOrigin, targetContainer))
		{
			// Invalid target: ensure session is at original position
			RemoveSessionFromTree(MovingSession);
			InsertSessionAt(_moveOriginalParent!, MovingSession, _moveOriginalIndex);
			return;
		}

		// Move to the new position
		RemoveSessionFromTree(MovingSession);

		// Recalculate index after removal
		if (selectedItem is SessionNodeViewModel ts2)
		{
			var newParent = FindParentOf(ts2);
			if (newParent.parent is null)
			{
				InsertSessionAt(_moveOriginalParent!, MovingSession, _moveOriginalIndex);
				return;
			}
			targetContainer = newParent.parent;
			targetIndex = newParent.index + 1;
		}

		InsertSessionAt(targetContainer, MovingSession, targetIndex);
	}

	// --- Private helpers ---

	// Backfill is now handled by ISessionRegistry.LoadFromSettings() called in the constructor.
	// This method only projects model nodes → ViewModels.
	private void LoadFromSettings()
	{
		foreach (var dirModel in _appSettings.Settings.Tree)
			Directories.Add(new DirectoryNodeViewModel(dirModel, _labelService));

		ReorganizeSubSessions();
	}

	/// <summary>
	/// Post-load pass: moves sub-sessions (those with SupervisorNodeId set) from the flat
	/// directory/group Children collection into their supervisor's SubSessions collection.
	/// The underlying model (and JSON persistence) is unchanged; this is purely a ViewModel reorganization.
	/// </summary>
	private void ReorganizeSubSessions()
	{
		// Collect all (sessionVm, container) pairs across the entire tree
		var all = new List<(SessionNodeViewModel Vm, ObservableCollection<ViewModelBase> Container)>();
		foreach (var dir in Directories)
			CollectAllSessions(dir.Children, all);

		// Build a NodeId → ViewModel lookup
		var byNodeId = new Dictionary<string, SessionNodeViewModel>(all.Count);
		foreach (var (vm, _) in all)
			if (!string.IsNullOrEmpty(vm.Model.NodeId))
				byNodeId[vm.Model.NodeId] = vm;

		// Move sub-sessions: remove from flat container, add to supervisor's SubSessions
		foreach (var (vm, container) in all)
		{
			if (string.IsNullOrEmpty(vm.Model.SupervisorNodeId)) continue;
			if (!byNodeId.TryGetValue(vm.Model.SupervisorNodeId, out var supervisor)) continue;

			container.Remove(vm);
			supervisor.SubSessions.Add(vm);
		}
	}

	private static void CollectAllSessions(
		ObservableCollection<ViewModelBase> children,
		List<(SessionNodeViewModel, ObservableCollection<ViewModelBase>)> result)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session)
				result.Add((session, children));
			else if (child is GroupNodeViewModel group)
				CollectAllSessions(group.Children, result);
		}
	}

	/// <summary>
	/// Expands the newly selected session's sub-sessions and collapses any previously expanded supervisor.
	/// </summary>
	private void UpdateSubSessionExpansion(SessionNodeViewModel? newSession)
	{
		if (newSession == null)
		{
			if (_expandedSupervisor != null)
			{
				_expandedSupervisor.IsExpanded = false;
				_expandedSupervisor = null;
			}
			return;
		}

		// New selection is already the expanded supervisor → leave as-is
		if (newSession == _expandedSupervisor) return;

		// New selection is a sub-session of the current expanded supervisor → keep it expanded
		if (_expandedSupervisor != null
			&& newSession.IsSubSession
			&& newSession.Model.SupervisorNodeId == _expandedSupervisor.Model.NodeId)
			return;

		// Different selection: collapse the previous supervisor
		if (_expandedSupervisor != null)
		{
			_expandedSupervisor.IsExpanded = false;
			_expandedSupervisor = null;
		}

		// Expand the new session if it has sub-sessions
		if (newSession.SubSessions.Count > 0)
		{
			newSession.IsExpanded = true;
			_expandedSupervisor = newSession;
		}
	}

	private void PromptAddDirectory()
	{
		// Folder picker is invoked from the view's code-behind (requires Window reference).
		// This command signals intent; the view handles the dialog.
	}

	private void RefreshSessionResumability()
	{
		foreach (var dir in Directories)
			RefreshChildren(dir.Children);
	}

	private void RefreshChildren(ObservableCollection<ViewModelBase> children)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					session.IsResumable = session.Model.ClaudeSessionId is not null
						&& _claudeSessionStatus.IsSessionResumable(
							session.Model.WorkingDirectory,
							session.Model.ClaudeSessionId);
					RefreshSubSessions(session.SubSessions);
					break;
				case GroupNodeViewModel group:
					RefreshChildren(group.Children);
					break;
			}
		}
	}

	private void RefreshSubSessions(IEnumerable<SessionNodeViewModel> sessions)
	{
		foreach (var session in sessions)
		{
			session.IsResumable = session.Model.ClaudeSessionId is not null
				&& _claudeSessionStatus.IsSessionResumable(
					session.Model.WorkingDirectory,
					session.Model.ClaudeSessionId);
			RefreshSubSessions(session.SubSessions);
		}
	}

	// --- Search ---

	private void ApplySearchFilter()
	{
		_searchCts?.Cancel();

		var query = SearchText?.Trim() ?? string.Empty;

		if (string.IsNullOrEmpty(query))
		{
			ShowAllNodes();
			return;
		}

		var cts = new CancellationTokenSource();
		_searchCts = cts;

		if (UseDaemon)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					var response = await _daemonService!.SearchAsync(query, cancellationToken: cts.Token);
					if (cts.Token.IsCancellationRequested) return;

					// Search returns numeric sessionIds — resolve to externalIds via sessions.list
					var numericIds = response.Results.Select(r => r.SessionId).ToHashSet();
					if (numericIds.Count == 0)
					{
						Dispatcher.UIThread.Post(() =>
						{
							foreach (var dir in Directories)
							{
								HideAllChildren(dir.Children);
								dir.IsVisible = false;
							}
						});
						return;
					}

					// Resolve numeric IDs to externalIds using cached map
					if (_daemonIdToExternalIdCache == null)
					{
						var allSessions = await _daemonService.SessionsListAsync(limit: 10000, cancellationToken: cts.Token);
						_daemonIdToExternalIdCache = new Dictionary<int, string>();
						foreach (var s in allSessions)
							_daemonIdToExternalIdCache[s.Id] = s.ExternalId;
					}

					var matchingExternalIds = new HashSet<string>();
					foreach (var nid in numericIds)
					{
						if (_daemonIdToExternalIdCache.TryGetValue(nid, out var eid))
							matchingExternalIds.Add(eid);
					}

					if (cts.Token.IsCancellationRequested) return;

					Dispatcher.UIThread.Post(() =>
					{
						if (cts.Token.IsCancellationRequested) return;

						foreach (var dir in Directories)
						{
							var dirHasMatch = ApplyFilterToChildrenByExternalId(dir.Children, matchingExternalIds);
							dir.IsVisible = dirHasMatch;
						}
					});
				}
				catch (OperationCanceledException) { /* expected */ }
				catch (Exception ex)
				{
					if (cts.Token.IsCancellationRequested) return;
					Serilog.Log.Warning(ex, "Daemon search failed, falling back to local");
					var matches = _searchService.FindMatchingSessionFiles(query);
					if (cts.Token.IsCancellationRequested) return;
					Dispatcher.UIThread.Post(() =>
					{
						if (cts.Token.IsCancellationRequested) return;
						foreach (var dir in Directories)
						{
							var dirHasMatch = ApplyFilterToChildren(dir.Children, matches);
							dir.IsVisible = dirHasMatch;
						}
					});
				}
			}, cts.Token);
		}
		else
		{
			Task.Run(() =>
			{
				if (cts.Token.IsCancellationRequested)
					return;

				var matches = _searchService.FindMatchingSessionFiles(query);

				if (cts.Token.IsCancellationRequested)
					return;

				Dispatcher.UIThread.Post(() =>
				{
					if (cts.Token.IsCancellationRequested)
						return;

					foreach (var dir in Directories)
					{
						var dirHasMatch = ApplyFilterToChildren(dir.Children, matches);
						dir.IsVisible = dirHasMatch;
					}
				});
			}, cts.Token);
		}
	}

	private static bool ApplyFilterToChildren(ObservableCollection<ViewModelBase> children, IReadOnlySet<string> matches)
	{
		var anyVisible = false;

		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					var visible = matches.Contains(session.FileName);
					session.IsVisible = visible;
					if (visible) anyVisible = true;
					break;
				case GroupNodeViewModel group:
					var groupHasMatch = ApplyFilterToChildren(group.Children, matches);
					group.IsVisible = groupHasMatch;
					if (groupHasMatch) anyVisible = true;
					break;
			}
		}

		return anyVisible;
	}

	private static bool ApplyFilterToChildrenByExternalId(ObservableCollection<ViewModelBase> children, HashSet<string> matchingExternalIds)
	{
		var anyVisible = false;

		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					var visible = session.ExternalId != null && matchingExternalIds.Contains(session.ExternalId);
					session.IsVisible = visible;
					if (visible) anyVisible = true;
					break;
				case GroupNodeViewModel group:
					var groupHasMatch = ApplyFilterToChildrenByExternalId(group.Children, matchingExternalIds);
					group.IsVisible = groupHasMatch;
					if (groupHasMatch) anyVisible = true;
					break;
			}
		}

		return anyVisible;
	}

	private static void HideAllChildren(ObservableCollection<ViewModelBase> children)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					session.IsVisible = false;
					foreach (var sub in session.SubSessions) sub.IsVisible = false;
					break;
				case GroupNodeViewModel group:
					HideAllChildren(group.Children);
					group.IsVisible = false;
					break;
			}
		}
	}

	private void ShowAllNodes()
	{
		foreach (var dir in Directories)
		{
			dir.IsVisible = true;
			ShowAllChildren(dir.Children);
		}
	}

	private static void ShowAllChildren(ObservableCollection<ViewModelBase> children)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					session.IsVisible = true;
					foreach (var sub in session.SubSessions) sub.IsVisible = true;
					break;
				case GroupNodeViewModel group:
					group.IsVisible = true;
					ShowAllChildren(group.Children);
					break;
			}
		}
	}

	// --- Agent orchestration helpers (FR.14, FR.15) ---

	/// <summary>
	/// Finds the <see cref="SessionNodeViewModel"/> whose model has <c>NodeId == nodeId</c>.
	/// Must be called on the UI thread (reads ObservableCollections).
	/// </summary>
	public SessionNodeViewModel? FindNodeVmByNodeId(string nodeId)
	{
		foreach (var dir in Directories)
		{
			var found = FindNodeVmInChildren(dir.Children, nodeId);
			if (found != null) return found;
		}
		return null;
	}

	private static SessionNodeViewModel? FindNodeVmInChildren(ObservableCollection<ViewModelBase> children, string nodeId)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel s:
					if (s.Model.NodeId == nodeId) return s;
					var subR = FindNodeVmInSubSessions(s.SubSessions, nodeId);
					if (subR != null) return subR;
					break;
				case GroupNodeViewModel g:
					var r = FindNodeVmInChildren(g.Children, nodeId);
					if (r != null) return r;
					break;
			}
		}
		return null;
	}

	private static SessionNodeViewModel? FindNodeVmInSubSessions(
		IEnumerable<SessionNodeViewModel> sessions, string nodeId)
	{
		foreach (var s in sessions)
		{
			if (s.Model.NodeId == nodeId) return s;
			var r = FindNodeVmInSubSessions(s.SubSessions, nodeId);
			if (r != null) return r;
		}
		return null;
	}

	/// <summary>
	/// Finds the raw <see cref="SessionNodeModel"/> whose <c>NodeId == nodeId</c>. Delegates to the registry.
	/// </summary>
	public SessionNodeModel? FindModelByNodeId(string nodeId) => _daemon.FindSessionByNodeId(nodeId);

	/// <summary>
	/// Finds the raw <see cref="SessionNodeModel"/> whose <c>AgentToken == token</c>. Delegates to the registry.
	/// </summary>
	public SessionNodeModel? FindModelByAgentToken(string token) => _daemon.FindSessionByAgentToken(token);

	/// <summary>
	/// Builds a flat list of all sessions for the <c>list_sessions</c> MCP tool. Delegates to the registry.
	/// </summary>
	public List<SessionSummaryModel> BuildAgentSessionSummaries() => _daemon.BuildAgentSessionSummaries();

	// --- Last prompt time ---

	private void RefreshLastPromptTimes()
	{
		foreach (var dir in Directories)
			RefreshLastPromptTimesInChildren(dir.Children);
	}

	private void RefreshLastPromptTimesInChildren(ObservableCollection<ViewModelBase> children)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					LoadLastPromptTime(session);
					foreach (var sub in session.SubSessions) LoadLastPromptTime(sub);
					break;
				case GroupNodeViewModel group:
					RefreshLastPromptTimesInChildren(group.Children);
					break;
			}
		}
	}

	/// <summary>
	/// Returns the directory label and the group path for a session node. Delegates to the registry.
	/// </summary>
	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeViewModel session)
		=> _daemon.GetSessionLocation(session.Model);

	private void LoadLastPromptTime(SessionNodeViewModel session)
	{
		SessionEntryModel? lastUserEntry = null;
		DateTimeOffset? lastAssistant = null;

		try
		{
			var entries = _sessionFileService.ReadEntries(session.FileName);
			for (var i = entries.Count - 1; i >= 0 && (lastUserEntry == null || lastAssistant == null); i--)
			{
				if (lastUserEntry == null && entries[i].Role == Constants.SessionFile.RoleUser)
					lastUserEntry = entries[i];
				else if (lastAssistant == null && entries[i].Role == Constants.SessionFile.RoleAssistant)
					lastAssistant = entries[i].Timestamp;
			}
		}
		catch
		{
			// Session file may not exist or be corrupted; leave timestamps null.
		}

		session.LastPromptTimestamp = lastUserEntry?.Timestamp;
		session.LastPromptTime = lastUserEntry?.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
		session.LastResponseTimestamp = lastAssistant;
		session.ProfileName = lastUserEntry?.ProfileName;

		// A draft file can persist with whitespace-only content (e.g. all text deleted without
		// clearing the draft); that isn't a real pending draft, so its timestamp must not count.
		var draftText = _draftService.LoadDraft(session.FileName);
		session.LastDraftEditTimestamp = !string.IsNullOrWhiteSpace(draftText)
			? _draftService.GetDraftTimestamp(session.FileName)
			: null;
	}

}
