using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionTreeViewModel : ViewModelBase
{
	private static readonly ILogger _log = Log.ForContext<SessionTreeViewModel>();

	private readonly IDaemonClient _daemon;
	private readonly IAppSettingsService _appSettings;
	private readonly IDirectoryLabelService _labelService;
	private readonly ISessionFileService _sessionFileService;
	private readonly IClaudeSessionStatusService _claudeSessionStatus;
	private readonly ISessionSearchService _searchService;
	private readonly IGitOriginService _gitOriginService;
	private readonly IDraftService _draftService;
	private readonly IOperationTimer _timer;
	private int _resumabilitySweepInFlight;
	private string _searchText = string.Empty;
	private SessionNodeViewModel? _selectedSession;
	private ViewModelBase? _selectedTreeItem;
	private CancellationTokenSource? _searchCts;
	private bool _isScopedToProject = true;
	private bool _manualExactSearch;
	private string? _autoExactSuppressedFor;
	private string _exactEvaluatedFor = string.Empty;
	private bool _isProjectScopeEnabled;
	private bool _isSearching;
	private bool _isSearchViewApplied;
	private readonly Dictionary<object, Action> _orderRestorers = new(ReferenceEqualityComparer.Instance);

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
		set
		{
			this.RaiseAndSetIfChanged(ref _searchText, value);
			this.RaisePropertyChanged(nameof(IsSearchActive));
		}
	}

	/// <summary>
	/// True while the trimmed search query is non-empty. The tree is then reordered by relevance
	/// (view-only, FR.1.12), so drag, drop and move mode are disabled.
	/// </summary>
	public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

	/// <summary>
	/// When true (the default whenever a session is selected), the search box's content search
	/// is limited to the selected session's project. Forced to false and disabled (see
	/// <see cref="IsProjectScopeEnabled"/>) when no session is selected.
	/// </summary>
	public bool IsScopedToProject
	{
		get => _isScopedToProject;
		set
		{
			this.RaiseAndSetIfChanged(ref _isScopedToProject, value);
			ApplySearchFilterIfActive();
		}
	}

	/// <summary>
	/// When true, only sessions containing the query as an exact phrase match (FR.1.14). Switched on
	/// automatically for links and short queries (see <see cref="IsAutoExactQuery"/>) when the search runs;
	/// unchecking it then holds until the search text changes. A manual choice for other queries persists.
	/// </summary>
	public bool IsExactSearch
	{
		get => IsAutoExactQuery(_exactEvaluatedFor)
			? !string.Equals(_autoExactSuppressedFor, _exactEvaluatedFor, StringComparison.Ordinal)
			: _manualExactSearch;
		set
		{
			if (IsAutoExactQuery(_exactEvaluatedFor))
				_autoExactSuppressedFor = value ? null : _exactEvaluatedFor;
			else
				_manualExactSearch = value;
			this.RaisePropertyChanged();
			ApplySearchFilterIfActive();
		}
	}

	/// <summary>Whether the project-scope toggle is interactive — only when a session is selected.</summary>
	public bool IsProjectScopeEnabled
	{
		get => _isProjectScopeEnabled;
		private set => this.RaiseAndSetIfChanged(ref _isProjectScopeEnabled, value);
	}

	/// <summary>Whether a content search is currently running against the index, for the search box's busy indicator.</summary>
	public bool IsSearching
	{
		get => _isSearching;
		private set => this.RaiseAndSetIfChanged(ref _isSearching, value);
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
		IOperationTimer? opTimer = null)
	{
		_timer = opTimer ?? new OperationTimer(new UiThreadTracker());
		_daemon = daemon;
		_appSettings = appSettings;
		_labelService = labelService;
		_sessionFileService = sessionFileService;
		_claudeSessionStatus = claudeSessionStatus;
		_searchService = searchService;
		_gitOriginService = gitOriginService;
		_draftService = draftService;

		AddDirectoryCommand = ReactiveCommand.Create(PromptAddDirectory);

		// Registry backfills NodeId/AgentToken, then we project models → VMs.
		_daemon.LoadFromSettings();
		LoadFromSettings();
		using (_timer.Measure(Constants.Diagnostics.Operation.RefreshGitOrigins, Constants.Diagnostics.WarnThresholds.UiLoadMs))
			RefreshGitOrigins();
		_ = RefreshSessionResumabilityAsync();
		using (_timer.Measure(Constants.Diagnostics.Operation.RefreshLastPromptTimes, Constants.Diagnostics.WarnThresholds.UiLoadMs))
			RefreshLastPromptTimes();
		using (_timer.Measure(Constants.Diagnostics.Operation.ApplyAttentionSummaries, Constants.Diagnostics.WarnThresholds.UiLoadMs))
		{
			foreach (var dir in Directories)
				ApplyAttentionSummaries(dir.Children);
		}

		// Subscribe to registry changes so service-initiated mutations (orchestration spawn,
		// IsRunning changes) are reflected in the VM tree without the VM touching the model directly.
		_daemon.TreeChanged += OnRegistryTreeChanged;
		_daemon.AttentionChanged += OnAttentionChanged;
		_daemon.SessionIndexed += (_, _) => Dispatcher.UIThread.Post(ApplySearchFilterIfActive);

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
				UpdateProjectScopeAvailability(session);
			});

		var timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(Constants.ClaudeSessions.StatusCheckIntervalSeconds),
		};
		timer.Tick += (_, _) => _ = RefreshSessionResumabilityAsync();
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

	private void OnAttentionChanged(object? sender, AttentionChangedEventArgs e)
	{
		Dispatcher.UIThread.Post(() =>
		{
			var node = FindNodeVmByNodeId(e.NodeId);
			if (node != null)
				node.ApplyAttentionSummary(_daemon.GetAttentionSummary(e.NodeId));
		});
	}

	private void ApplyAttentionSummaries(IEnumerable<ViewModelBase> children)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					ApplyAttentionSummary(session);
					break;
				case GroupNodeViewModel group:
					ApplyAttentionSummaries(group.Children);
					break;
			}
		}
	}

	private void ApplyAttentionSummary(SessionNodeViewModel session)
	{
		session.ApplyAttentionSummary(_daemon.GetAttentionSummary(session.Model.NodeId));
		foreach (var sub in session.SubSessions)
			ApplyAttentionSummary(sub);
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
		if (IsSearchActive)
			return false;

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
		if (IsSearchActive)
			return;

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
		if (IsSearchActive)
			return;

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

		// Move sub-sessions: remove from flat container, add to supervisor's SubSessions.
		// Track which containers lose a session so we can prune groups emptied by the move.
		var containersThatLostSessions = new HashSet<ObservableCollection<ViewModelBase>>();
		foreach (var (vm, container) in all)
		{
			if (string.IsNullOrEmpty(vm.Model.SupervisorNodeId)) continue;
			if (!byNodeId.TryGetValue(vm.Model.SupervisorNodeId, out var supervisor)) continue;

			container.Remove(vm);
			supervisor.SubSessions.Add(vm);
			containersThatLostSessions.Add(container);
		}

		// Remove any group left empty purely because the reorg re-parented its sessions under a
		// supervisor. This makes a restarted tree match the live/runtime view: when orchestration
		// spawns a child with a group argument, the registry persists it inside an auto-created
		// group, but at runtime HandleRegistrySessionAdded renders that child nested under its
		// supervisor and never projects the group. On reload the group VM is built from the model,
		// so without this its re-parented children leave behind a visible empty husk. The group
		// survives in the model, so the orchestrator (which resolves sessions by NodeId, not tree
		// position) is unaffected.
		foreach (var dir in Directories)
			PruneGroupsEmptiedByReorg(dir.Children, containersThatLostSessions);
	}

	/// <summary>
	/// Removes group VMs emptied purely by <see cref="ReorganizeSubSessions"/> re-parenting their
	/// sessions under a supervisor. Only groups whose own child collection lost a session are pruned,
	/// so a genuinely empty user-created group (which never held a session) is left intact.
	/// </summary>
	private static void PruneGroupsEmptiedByReorg(
		ObservableCollection<ViewModelBase> children,
		HashSet<ObservableCollection<ViewModelBase>> containersThatLostSessions)
	{
		var toRemove = new List<GroupNodeViewModel>();
		foreach (var child in children)
		{
			if (child is not GroupNodeViewModel group) continue;

			PruneGroupsEmptiedByReorg(group.Children, containersThatLostSessions);

			if (group.Children.Count == 0 && containersThatLostSessions.Contains(group.Children))
				toRemove.Add(group);
		}

		foreach (var group in toRemove)
			children.Remove(group);
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

	/// <summary>
	/// Re-checks every session's resumability. Only the snapshot of what the check needs is taken on the UI
	/// thread; the file system work runs as one batch on a background thread and only changed flags are
	/// applied back in a single UI pass. A sweep still in flight makes the next tick a no-op.
	/// </summary>
	private async Task RefreshSessionResumabilityAsync()
	{
		if (Interlocked.CompareExchange(ref _resumabilitySweepInFlight, 1, 0) != 0)
			return;

		try
		{
			var stopwatch = Stopwatch.StartNew();
			var sessions = new List<SessionNodeViewModel>();
			foreach (var dir in Directories)
				CollectSessions(dir.Children, sessions);

			var requests = sessions
				.Select(s => new SessionResumabilityRequest(s.Model.WorkingDirectory, s.Model.ClaudeSessionId))
				.ToList();

			var results = await Task.Run(() => _claudeSessionStatus.AreSessionsResumable(requests)).ConfigureAwait(false);

			var changed = 0;
			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				using var applyScope = _timer.Measure(Constants.Diagnostics.Operation.ResumabilityApply, Constants.Diagnostics.WarnThresholds.UiOperationMs);
				for (var i = 0; i < sessions.Count; i++)
				{
					if (sessions[i].IsResumable == results[i])
						continue;

					sessions[i].IsResumable = results[i];
					changed++;
				}
			});

			_log.Information("Resumability sweep: {Checked} sessions checked, {Changed} changed, {ElapsedMs} ms",
				sessions.Count, changed, stopwatch.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Resumability sweep failed");
		}
		finally
		{
			Volatile.Write(ref _resumabilitySweepInFlight, 0);
		}
	}

	private static void CollectSessions(IEnumerable<ViewModelBase> children, List<SessionNodeViewModel> sessions)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					CollectSession(session, sessions);
					break;
				case GroupNodeViewModel group:
					CollectSessions(group.Children, sessions);
					break;
			}
		}
	}

	private static void CollectSession(SessionNodeViewModel session, List<SessionNodeViewModel> sessions)
	{
		sessions.Add(session);
		foreach (var sub in session.SubSessions)
			CollectSession(sub, sessions);
	}

	// --- Search ---

	private static bool IsAutoExactQuery(string query) =>
		query.Length > 0
		&& (query.Length < Constants.TreeSearch.AutoExactBelowLength
			|| query.StartsWith(Constants.TreeSearch.AutoExactPrefix, StringComparison.OrdinalIgnoreCase));

	/// <summary>On a changed query, drops any "unchecked for this text" override so the auto rule applies afresh.</summary>
	private void EvaluateAutoExact(string query)
	{
		if (string.Equals(query, _exactEvaluatedFor, StringComparison.Ordinal))
			return;
		_exactEvaluatedFor = query;
		_autoExactSuppressedFor = null;
		this.RaisePropertyChanged(nameof(IsExactSearch));
	}

	private void ApplySearchFilterIfActive()
	{
		if (!string.IsNullOrWhiteSpace(SearchText))
			ApplySearchFilter();
	}

	/// <summary>
	/// Updates whether the project-scope toggle is interactive, and its default value, when the
	/// selected session changes. Defaults to ON whenever a session becomes selected (per request),
	/// and is forced off/disabled while no session is selected.
	/// </summary>
	private void UpdateProjectScopeAvailability(SessionNodeViewModel? session)
	{
		var wasEnabled = IsProjectScopeEnabled;
		IsProjectScopeEnabled = session != null;

		if (session == null)
			IsScopedToProject = false;
		else if (!wasEnabled)
			IsScopedToProject = true;
		else
			ApplySearchFilterIfActive(); // scope stays enabled, but the selected project may have changed
	}

	private void ApplySearchFilter()
	{
		_searchCts?.Cancel();

		var query = SearchText?.Trim() ?? string.Empty;

		if (string.IsNullOrEmpty(query))
		{
			_searchCts = null;
			IsSearching = false;
			EvaluateAutoExact(query);
			ShowAllNodes();
			EndSearchView();
			return;
		}

		BeginSearchView();
		EvaluateAutoExact(query);
		var exact = IsExactSearch;

		var projectSlug = IsScopedToProject && SelectedSession != null
			? Constants.ClaudeSessions.BuildProjectSlug(SelectedSession.Model.WorkingDirectory)
			: null;

		var cts = new CancellationTokenSource();
		_searchCts = cts;
		IsSearching = true;

		_ = Task.Run(() => RunSearchAsync(query, projectSlug, exact, cts), cts.Token);
	}

	/// <summary>
	/// Resolves the matching sessions (index first, local file fallback second) and posts the result to the UI
	/// thread. Never throws: every outcome other than cancellation ends in <see cref="FinishSearch"/>, so
	/// the busy indicator cannot get stuck (ISSUE.015).
	/// </summary>
	private async Task RunSearchAsync(string query, string? projectSlug, bool exact, CancellationTokenSource cts)
	{
		Func<SessionNodeViewModel, SessionSearchScoreModel?>? scoreOf;
		try
		{
			scoreOf = await QueryIndexAsync(query, projectSlug, exact, cts.Token);
		}
		catch (OperationCanceledException)
		{
			return; // a newer search supersedes this one and owns the busy indicator
		}
		catch (Exception ex)
		{
			if (cts.IsCancellationRequested) return;
			Serilog.Log.Warning(ex, "Session search failed, falling back to local substring match");
			scoreOf = TryFallbackSearch(query);
		}

		if (cts.IsCancellationRequested) return;
		Dispatcher.UIThread.Post(() => FinishSearch(cts, scoreOf));
	}

	private async Task<Func<SessionNodeViewModel, SessionSearchScoreModel?>> QueryIndexAsync(
		string query, string? projectSlug, bool exact, CancellationToken ct)
	{
		// Unranked, uncapped membership test with per-session match counts — not SearchSessionsAsync's
		// ranked top-N, which would silently drop genuine matches in this tree whenever other projects on
		// the machine happen to score higher on the same terms (see ISSUE.007).
		var scores = await _daemon.SearchSessionScoresAsync(query, projectSlug, ct);
		var byExternalId = new Dictionary<string, SessionSearchScoreModel>(scores.Count);
		foreach (var score in scores.Where(score => !exact || score.StrictMatchCount > 0))
			byExternalId[score.ExternalId] = score;

		return session =>
		{
			var key = session.ExternalId ?? session.Model.ClaudeSessionId;
			return key != null && byExternalId.TryGetValue(key, out var score) ? score : null;
		};
	}

	/// <summary>File-name match without scores (counts as a loose match). Returns null — show everything — on failure.</summary>
	private Func<SessionNodeViewModel, SessionSearchScoreModel?>? TryFallbackSearch(string query)
	{
		try
		{
			var matches = _searchService.FindMatchingSessionFiles(query);
			return session => matches.Contains(session.FileName)
				? new SessionSearchScoreModel { ExternalId = session.FileName, StrictMatchCount = 0, LooseMatchCount = 1 }
				: null;
		}
		catch (Exception ex)
		{
			Serilog.Log.Warning(ex, "Session search fallback failed; showing all sessions");
			return null;
		}
	}

	/// <summary>UI-thread completion of a search; only the latest search applies its result or clears the busy indicator.</summary>
	private void FinishSearch(CancellationTokenSource cts, Func<SessionNodeViewModel, SessionSearchScoreModel?>? scoreOf)
	{
		if (!ReferenceEquals(_searchCts, cts)) return;

		try
		{
			if (scoreOf == null)
				ShowAllNodes();
			else
				ApplyScores(scoreOf);
		}
		catch (Exception ex)
		{
			Serilog.Log.Warning(ex, "Applying session search results failed; showing all sessions");
			ShowAllNodes();
		}
		finally
		{
			IsSearching = false;
		}
	}

	private void ApplyScores(Func<SessionNodeViewModel, SessionSearchScoreModel?> scoreOf)
	{
		using var scope = _timer.Measure(Constants.Diagnostics.Operation.TreeSearchApply, Constants.Diagnostics.WarnThresholds.UiLoadMs);
		var keys = new Dictionary<DirectoryNodeViewModel, SearchRelevanceKeyModel?>();
		foreach (var dir in Directories)
		{
			var key = ApplyToChildren(dir.Children, scoreOf);
			dir.IsVisible = key != null;
			keys[dir] = key;
		}
		ReorderByKey(Directories, d => keys.GetValueOrDefault(d));
	}

	/// <summary>Applies visibility and relevance order to a Directory's/Group's children; returns the best key among them.</summary>
	private SearchRelevanceKeyModel? ApplyToChildren(
		ObservableCollection<ViewModelBase> children, Func<SessionNodeViewModel, SessionSearchScoreModel?> scoreOf)
	{
		var keys = new Dictionary<ViewModelBase, SearchRelevanceKeyModel?>();
		foreach (var child in children.ToList())
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					keys[child] = ApplyToSession(session, scoreOf);
					break;
				case GroupNodeViewModel group:
					var groupKey = ApplyToChildren(group.Children, scoreOf);
					group.IsVisible = groupKey != null;
					keys[child] = groupKey;
					break;
			}
		}
		ReorderByKey(children, c => keys.GetValueOrDefault(c));
		return BestKey(keys.Values);
	}

	/// <summary>A session's effective key is the best of its own and its sub-sessions' keys (FR.1.9).</summary>
	private SearchRelevanceKeyModel? ApplyToSession(
		SessionNodeViewModel session, Func<SessionNodeViewModel, SessionSearchScoreModel?> scoreOf)
	{
		var subKeys = new Dictionary<SessionNodeViewModel, SearchRelevanceKeyModel?>();
		foreach (var sub in session.SubSessions.ToList())
			subKeys[sub] = ApplyToSession(sub, scoreOf);
		ReorderByKey(session.SubSessions, s => subKeys.GetValueOrDefault(s));

		var best = BestKey(subKeys.Values.Append(KeyFor(session, scoreOf)));
		session.IsVisible = best != null;
		return best;
	}

	private static SearchRelevanceKeyModel? KeyFor(
		SessionNodeViewModel session, Func<SessionNodeViewModel, SessionSearchScoreModel?> scoreOf)
	{
		var score = scoreOf(session);
		return score == null
			? null
			: new SearchRelevanceKeyModel(score.StrictMatchCount, score.LooseMatchCount, session.LastPromptTimestamp);
	}

	private static readonly SearchRelevanceComparer RelevanceComparer = new();

	private static SearchRelevanceKeyModel? BestKey(IEnumerable<SearchRelevanceKeyModel?> keys)
		=> keys.Order(RelevanceComparer).FirstOrDefault();

	/// <summary>
	/// Reorders a collection most-relevant first with <see cref="ObservableCollection{T}.Move"/> (keeps
	/// TreeViewItem state); non-matching items follow in their current relative order. View-only.
	/// </summary>
	private void ReorderByKey<T>(ObservableCollection<T> collection, Func<T, SearchRelevanceKeyModel?> keyOf)
	{
		EnsureOrderSnapshot(collection);
		ApplyOrder(collection, collection.OrderBy(keyOf, RelevanceComparer).ToList());
	}

	private static void ApplyOrder<T>(ObservableCollection<T> collection, IReadOnlyList<T> target)
	{
		for (var i = 0; i < target.Count; i++)
		{
			var current = collection.IndexOf(target[i]);
			if (current != i)
				collection.Move(current, i);
		}
	}

	// ── View-only order snapshot (FR.1.12): restored when the search is cleared ──

	private void BeginSearchView()
	{
		if (_isSearchViewApplied)
			return;

		CancelMoveMode(); // before snapshotting, so the original position is already restored
		_isSearchViewApplied = true;

		EnsureOrderSnapshot(Directories);
		foreach (var dir in Directories)
			SnapshotChildren(dir.Children);
	}

	private void SnapshotChildren(ObservableCollection<ViewModelBase> children)
	{
		EnsureOrderSnapshot(children);
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					SnapshotSubSessions(session);
					break;
				case GroupNodeViewModel group:
					SnapshotChildren(group.Children);
					break;
			}
		}
	}

	private void SnapshotSubSessions(SessionNodeViewModel session)
	{
		EnsureOrderSnapshot(session.SubSessions);
		foreach (var sub in session.SubSessions)
			SnapshotSubSessions(sub);
	}

	private void EnsureOrderSnapshot<T>(ObservableCollection<T> collection) where T : notnull
	{
		if (!_isSearchViewApplied || _orderRestorers.ContainsKey(collection))
			return;

		var order = collection.ToList();
		_orderRestorers[collection] = () => RestoreOrder(collection, order);
	}

	/// <summary>Snapshot order first; items added during the search follow in their current order; removed items are ignored.</summary>
	private static void RestoreOrder<T>(ObservableCollection<T> collection, List<T> snapshot) where T : notnull
	{
		var inSnapshot = new HashSet<T>(snapshot);
		var target = snapshot.Where(collection.Contains).Concat(collection.Where(x => !inSnapshot.Contains(x))).ToList();
		ApplyOrder(collection, target);
	}

	private void EndSearchView()
	{
		if (!_isSearchViewApplied)
			return;

		using var scope = _timer.Measure(Constants.Diagnostics.Operation.TreeSearchRestore, Constants.Diagnostics.WarnThresholds.UiLoadMs);
		foreach (var restore in _orderRestorers.Values)
			restore();
		_orderRestorers.Clear();
		_isSearchViewApplied = false;
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
