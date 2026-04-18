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
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionTreeViewModel : ViewModelBase
{
	private readonly IAppSettingsService _appSettings;
	private readonly IDirectoryLabelService _labelService;
	private readonly ISessionFileService _sessionFileService;
	private readonly IClaudeSessionStatusService _claudeSessionStatus;
	private readonly ISessionSearchService _searchService;
	private readonly IGitOriginService _gitOriginService;
	private readonly ITessynDaemonService? _daemonService;
	private string _searchText = string.Empty;
	private SessionNodeViewModel? _selectedSession;
	private Dictionary<int, string>? _daemonIdToExternalIdCache;
	private ViewModelBase? _selectedTreeItem;
	private CancellationTokenSource? _searchCts;

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
		IAppSettingsService appSettings,
		IDirectoryLabelService labelService,
		ISessionFileService sessionFileService,
		IClaudeSessionStatusService claudeSessionStatus,
		ISessionSearchService searchService,
		IGitOriginService gitOriginService,
		ITessynDaemonService? daemonService = null)
	{
		_appSettings = appSettings;
		_labelService = labelService;
		_sessionFileService = sessionFileService;
		_claudeSessionStatus = claudeSessionStatus;
		_searchService = searchService;
		_gitOriginService = gitOriginService;
		_daemonService = daemonService;

		AddDirectoryCommand = ReactiveCommand.Create(PromptAddDirectory);

		LoadFromSettings();
		RefreshGitOrigins();
		RefreshSessionResumability();
		RefreshLastPromptTimes();

		this.WhenAnyValue(x => x.SearchText)
			.Throttle(TimeSpan.FromMilliseconds(300))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => ApplySearchFilter());

		var timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(Constants.ClaudeSessions.StatusCheckIntervalSeconds),
		};
		timer.Tick += (_, _) => RefreshSessionResumability();
		timer.Start();
	}

	// --- Add operations ---

	public DirectoryNodeViewModel AddDirectory(string path)
	{
		var normalised = Path.GetFullPath(path);
		var existing = Directories.FirstOrDefault(d =>
			string.Equals(d.Path, normalised, StringComparison.OrdinalIgnoreCase));

		if (existing != null)
			return existing;

		var model = new DirectoryNodeModel { Path = normalised };
		var vm = new DirectoryNodeViewModel(model, _labelService);
		Directories.Add(vm);
		_appSettings.Settings.Tree.Add(model);
		_appSettings.Save();
		return vm;
	}

	public GroupNodeViewModel AddGroup(DirectoryNodeViewModel parent, string name)
	{
		var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.Path };
		var vm = new GroupNodeViewModel(model);
		parent.AddGroup(vm);
		_appSettings.Save();
		return vm;
	}

	public GroupNodeViewModel AddGroupToGroup(GroupNodeViewModel parent, string name)
	{
		var model = new GroupNodeModel { Name = name, WorkingDirectory = parent.WorkingDirectory };
		var vm = new GroupNodeViewModel(model);
		parent.AddGroup(vm);
		_appSettings.Save();
		return vm;
	}

	public SessionNodeViewModel AddSession(DirectoryNodeViewModel parent, string name)
	{
		var fileName = UseDaemon
			? $"daemon-{Guid.NewGuid():N}.pending"
			: _sessionFileService.CreateSessionFile();
		var model = new SessionNodeModel { Name = name, FileName = fileName, WorkingDirectory = parent.Path };
		var vm = new SessionNodeViewModel(model);
		parent.AddSession(vm);
		_appSettings.Save();
		return vm;
	}

	public SessionNodeViewModel AddSessionToGroup(GroupNodeViewModel parent, string name)
	{
		var fileName = UseDaemon
			? $"daemon-{Guid.NewGuid():N}.pending"
			: _sessionFileService.CreateSessionFile();
		var model = new SessionNodeModel { Name = name, FileName = fileName, WorkingDirectory = parent.WorkingDirectory };
		var vm = new SessionNodeViewModel(model);
		parent.AddSession(vm);
		_appSettings.Save();
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
		var model = new SessionNodeModel
		{
			Name = name,
			FileName = fileName,
			WorkingDirectory = parent.Path,
			ClaudeSessionId = claudeSessionId,
			ExternalId = claudeSessionId,
		};
		var vm = new SessionNodeViewModel(model);
		parent.AddSession(vm);
		_appSettings.Save();
		return vm;
	}

	/// <summary>
	/// Creates a session node from imported data and adds it to a group.
	/// </summary>
	public SessionNodeViewModel ImportSessionToGroup(
		GroupNodeViewModel parent, string name, string fileName, string claudeSessionId)
	{
		var model = new SessionNodeModel
		{
			Name = name,
			FileName = fileName,
			WorkingDirectory = parent.WorkingDirectory,
			ClaudeSessionId = claudeSessionId,
			ExternalId = claudeSessionId,
		};
		var vm = new SessionNodeViewModel(model);
		parent.AddSession(vm);
		_appSettings.Save();
		return vm;
	}

	/// <summary>
	/// Collects all session identifiers from the entire tree for duplicate detection.
	/// Includes both ExternalId and ClaudeSessionId for backward compatibility.
	/// </summary>
	public IReadOnlySet<string> CollectAllClaudeSessionIds()
	{
		var ids = new HashSet<string>();
		foreach (var dir in Directories)
			CollectIdsFromChildren(dir.Children, ids);
		return ids;
	}

	private static void CollectIdsFromChildren(ObservableCollection<ViewModelBase> children, HashSet<string> ids)
	{
		foreach (var child in children)
		{
			switch (child)
			{
				case SessionNodeViewModel session:
					if (session.Model.ExternalId is not null)
						ids.Add(session.Model.ExternalId);
					if (session.Model.ClaudeSessionId is not null)
						ids.Add(session.Model.ClaudeSessionId);
					break;
				case GroupNodeViewModel group:
					CollectIdsFromChildren(group.Children, ids);
					break;
			}
		}
	}

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
		group.Name = newName;
		_appSettings.Save();
	}

	public void RenameSession(SessionNodeViewModel session, string newName)
	{
		session.Name = newName;
		_appSettings.Save();
	}

	// --- Delete operations ---

	public bool TryDeleteDirectory(DirectoryNodeViewModel directory)
	{
		if (!directory.CanDelete)
			return false;

		Directories.Remove(directory);
		_appSettings.Settings.Tree.Remove(directory.Model);
		_appSettings.Save();
		return true;
	}

	public bool TryDeleteGroup(DirectoryNodeViewModel parent, GroupNodeViewModel group)
	{
		if (!group.CanDelete)
			return false;

		parent.Children.Remove(group);
		parent.Model.Groups.Remove(group.Model);
		_appSettings.Save();
		return true;
	}

	public bool TryDeleteGroupFromGroup(GroupNodeViewModel parent, GroupNodeViewModel group)
	{
		if (!group.CanDelete)
			return false;

		parent.Children.Remove(group);
		parent.Model.Groups.Remove(group.Model);
		_appSettings.Save();
		return true;
	}

	public bool TryDeleteSession(DirectoryNodeViewModel parent, SessionNodeViewModel session, bool forceDelete = false)
	{
		if (!forceDelete && _sessionFileService.SessionFileExists(session.FileName))
			return false;

		if (forceDelete)
			_sessionFileService.DeleteSessionFile(session.FileName);

		parent.Children.Remove(session);
		parent.Model.Sessions.Remove(session.Model);
		_appSettings.Save();
		return true;
	}

	public bool TryDeleteSessionFromGroup(GroupNodeViewModel parent, SessionNodeViewModel session, bool forceDelete = false)
	{
		if (!forceDelete && _sessionFileService.SessionFileExists(session.FileName))
			return false;

		if (forceDelete)
			_sessionFileService.DeleteSessionFile(session.FileName);

		parent.Children.Remove(session);
		parent.Model.Sessions.Remove(session.Model);
		_appSettings.Save();
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
	/// Returns the parent and index for undo capability.
	/// </summary>
	public (ViewModelBase? parent, int index) RemoveSessionFromTree(SessionNodeViewModel session)
	{
		foreach (var dir in Directories)
		{
			var idx = dir.Children.IndexOf(session);
			if (idx >= 0)
			{
				dir.Children.RemoveAt(idx);
				dir.Model.Sessions.Remove(session.Model);
				return (dir, idx);
			}

			var result = RemoveSessionFromGroupChildren(dir.Children, session);
			if (result.parent is not null)
				return result;
		}
		return (null, -1);
	}

	private static (ViewModelBase? parent, int index) RemoveSessionFromGroupChildren(
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
				grp.Model.Sessions.Remove(session.Model);
				return (grp, idx);
			}

			var result = RemoveSessionFromGroupChildren(grp.Children, session);
			if (result.parent is not null)
				return result;
		}
		return (null, -1);
	}

	/// <summary>
	/// Inserts a session into a target parent at the given index.
	/// </summary>
	public void InsertSessionAt(ViewModelBase parent, SessionNodeViewModel session, int index)
	{
		switch (parent)
		{
			case DirectoryNodeViewModel dir:
				index = Math.Clamp(index, 0, dir.Children.Count);
				dir.Children.Insert(index, session);
				dir.Model.Sessions.Add(session.Model);
				session.Model.WorkingDirectory = dir.Path;
				break;
			case GroupNodeViewModel grp:
				index = Math.Clamp(index, 0, grp.Children.Count);
				grp.Children.Insert(index, session);
				grp.Model.Sessions.Add(session.Model);
				session.Model.WorkingDirectory = grp.WorkingDirectory;
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

		InsertSessionAt(container, session, insertIndex);
		_appSettings.Save();
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
		InsertSessionAt(_moveOriginalParent!, MovingSession, _moveOriginalIndex);

		MovingSession.IsBeingMoved = false;
		MovingSession = null;
		_moveOriginalParent = null;
		IsMoveModeActive = false;
		_appSettings.Save();
	}

	public bool ConfirmMoveMode()
	{
		if (!IsMoveModeActive || MovingSession is null)
			return false;

		MovingSession.IsBeingMoved = false;
		MovingSession = null;
		_moveOriginalParent = null;
		IsMoveModeActive = false;
		_appSettings.Save();
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

	private void LoadFromSettings()
	{
		foreach (var dirModel in _appSettings.Settings.Tree)
			Directories.Add(new DirectoryNodeViewModel(dirModel, _labelService));
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
					break;
				case GroupNodeViewModel group:
					RefreshChildren(group.Children);
					break;
			}
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
					break;
				case GroupNodeViewModel group:
					group.IsVisible = true;
					ShowAllChildren(group.Children);
					break;
			}
		}
	}

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
					break;
				case GroupNodeViewModel group:
					RefreshLastPromptTimesInChildren(group.Children);
					break;
			}
		}
	}

	private void LoadLastPromptTime(SessionNodeViewModel session)
	{
		try
		{
			var entries = _sessionFileService.ReadEntries(session.FileName);
			for (var i = entries.Count - 1; i >= 0; i--)
			{
				if (entries[i].Role == Constants.SessionFile.RoleUser)
				{
					session.LastPromptTimestamp = entries[i].Timestamp;
					var local = entries[i].Timestamp.LocalDateTime;
					session.LastPromptTime = local.ToString("yyyy-MM-dd HH:mm");
					return;
				}
			}
		}
		catch
		{
			// Session file may not exist or be corrupted; leave LastPromptTime null.
		}

		session.LastPromptTime = null;
		session.LastPromptTimestamp = null;
	}

}
