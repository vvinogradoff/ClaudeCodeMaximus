using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITreeMutationService" />
/// <remarks>Created by Claude</remarks>
public sealed class TreeMutationService : ITreeMutationService
{
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _appSettings;
	private readonly IDirectoryLabelService _labels;
	private readonly IDirectoryGitOrigins _origins;
	private readonly TreeMoveRules _rules = new();
	private readonly BroadDirectoryRules _broad;

	public TreeMutationService(ISessionRegistry registry, IAppSettingsService appSettings, IDirectoryLabelService labels, IDirectoryGitOrigins origins, BroadDirectoryRules? broad = null)
	{
		_broad       = broad ?? new BroadDirectoryRules();
		_registry    = registry;
		_appSettings = appSettings;
		_labels      = labels;
		_origins     = origins;
	}

	/// <summary>Where a node sits: its kind, owning directory, parent group (null = directly under the directory) and tree path.</summary>
	private sealed record Located(string Kind, DirectoryNodeModel Directory, GroupNodeModel? ParentGroup, GroupNodeModel? Group, SessionNodeModel? Session, string TreePath);

	/// <inheritdoc />
	public TreeAddDirectoryResult AddDirectory(string path)
	{
		if (path.Trim().Length == 0)
			throw new TdcpInvalidParamsException("'path' must not be blank");

		// The shape and the breadth are judged on the text, before any disk access: a UNC path must not make the daemon open a connection.
		var full = _broad.Normalize(path);
		if (full == null || _broad.IsTooBroad(full) || IsNetworkDrive(full))
			throw new TdcpException(Constants.TreeMutation.FolderRefused);

		if (!Directory.Exists(full))
			throw new TdcpException(Constants.TreeMutation.PathNotFound);

		if (IsReparsePoint(full))
			throw new TdcpException(Constants.TreeMutation.FolderRefused);

		var directory = _registry.AddDirectory(full);
		_registry.RaiseDaemonTreeMutation();
		return new TreeAddDirectoryResult(directory.NodeId, _labels.GetLabel(directory.Path));
	}

	private static bool IsNetworkDrive(string full)
	{
		try
		{
			return new DriveInfo(full[..1]).DriveType == DriveType.Network;
		}
		catch (Exception ex) when (ex is ArgumentException or IOException)
		{
			return false;
		}
	}

	private static bool IsReparsePoint(string full)
	{
		try
		{
			return new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return true;
		}
	}

	/// <inheritdoc />
	public TreeAddGroupResult AddGroup(string parentId, string name)
	{
		name = RequireName(name);

		var directory = _registry.FindDirectoryByNodeId(parentId);
		string id;
		if (directory != null)
		{
			id = _registry.AddGroup(directory, name).NodeId;
		}
		else
		{
			var group = _registry.FindGroupByNodeId(parentId) ?? throw new TdcpException(Constants.TreeMutation.ParentNotFound);
			id = _registry.AddGroupToGroup(group, name).NodeId;
		}

		_registry.RaiseDaemonTreeMutation();
		return new TreeAddGroupResult(id);
	}

	/// <inheritdoc />
	public TreeRenameResult RenameNode(string nodeId, string name)
	{
		name = RequireName(name);

		var located = Find(nodeId);
		switch (located.Kind)
		{
			case RemoteProtocol.TreeMutation.NodeKinds.Session:
				_registry.RenameSession(located.Session!, name);
				break;
			case RemoteProtocol.TreeMutation.NodeKinds.Group:
				_registry.RenameGroup(located.Group!, name);
				break;
			default:
				throw new TdcpException(Constants.TreeMutation.DirectoryRenameRefused);
		}

		_registry.RaiseDaemonTreeMutation();
		return new TreeRenameResult(name);
	}

	/// <inheritdoc />
	public TreeDeleteResult DeleteNode(string nodeId, bool force)
	{
		var located = Find(nodeId);
		var blocked = RemoteProtocol.TreeMutation.BlockedBy.NotEmpty;
		bool deleted;

		switch (located.Kind)
		{
			case RemoteProtocol.TreeMutation.NodeKinds.Directory:
				deleted = _registry.TryDeleteDirectory(located.Directory);
				break;

			case RemoteProtocol.TreeMutation.NodeKinds.Group:
				deleted = located.ParentGroup == null
					? _registry.TryDeleteGroup(located.Directory, located.Group!)
					: _registry.TryDeleteGroupFromGroup(located.ParentGroup, located.Group!);
				break;

			default:
				var session = located.Session!;
				if (_registry.IsSessionRunning(session.NodeId))
					return Blocked(RemoteProtocol.TreeMutation.BlockedBy.Running);

				if (!force && HasWorkers(session.NodeId))
					return Blocked(RemoteProtocol.TreeMutation.BlockedBy.Workers);

				blocked = RemoteProtocol.TreeMutation.BlockedBy.SessionFile;
				deleted = located.ParentGroup == null
					? _registry.TryDeleteSession(located.Directory, session, force)
					: _registry.TryDeleteSessionFromGroup(located.ParentGroup, session, force);
				break;
		}

		if (!deleted)
			return Blocked(blocked);

		_registry.RaiseDaemonTreeMutation();
		return new TreeDeleteResult(true, null);

		static TreeDeleteResult Blocked(string reason) => new(false, reason);
	}

	/// <inheritdoc />
	public TreeMoveResult MoveNode(string nodeId, string parentId, int? index)
	{
		if (index is < 0)
			throw new TdcpInvalidParamsException("'index' must not be negative");

		var located = Find(nodeId);
		if (located.Kind == RemoteProtocol.TreeMutation.NodeKinds.Group)
			return ReorderGroup(located, parentId, index);

		if (located.Kind != RemoteProtocol.TreeMutation.NodeKinds.Session)
			throw new TdcpException(Constants.TreeMutation.OnlySessionsMove);

		var session = located.Session!;

		// A move changes the working directory the next --resume (and a correction) uses: not while a turn runs.
		if (_registry.IsSessionRunning(session.NodeId))
			throw new TdcpException(Constants.TreeMutation.SessionRunning);

		DirectoryNodeModel targetDirectory;
		GroupNodeModel? targetGroup = null;
		string sourcePath;
		string targetPath;
		int rawIndex;

		lock (_appSettings.SyncRoot)
		{
			if (IsWorker(session))
				throw new TdcpException(Constants.TreeMutation.WorkerMoveRefused);

			var target = Locate(parentId);
			if (target == null)
				throw new TdcpException(Constants.TreeMutation.ParentNotFound);
			if (target.Kind == RemoteProtocol.TreeMutation.NodeKinds.Session)
				throw new TdcpException(Constants.TreeMutation.TargetMustBeContainer);

			targetDirectory = target.Directory;
			targetGroup     = target.Group;
			sourcePath      = located.Directory.Path;
			targetPath      = target.Directory.Path;
			rawIndex        = RawIndex(targetGroup?.Sessions ?? targetDirectory.Sessions, session, index ?? 0);
		}

		// The origins are read outside the lock (file access).
		if (!_rules.CanMove(_origins.GetOrigin(sourcePath), _origins.GetOrigin(targetPath)))
			throw new TdcpException(Constants.TreeMutation.GitOriginRefused);

		if (!_registry.MoveSession(session, targetDirectory, targetGroup, rawIndex))
			throw new TdcpException(Constants.TreeMutation.NodeNotFound);

		_registry.RaiseDaemonTreeMutation();
		return new TreeMoveResult(true);
	}

	/// <summary>FR.1.32 / TDCP 10.A8: a group moves only within its current parent; the index counts the parent's groups once it has left the list.</summary>
	private TreeMoveResult ReorderGroup(Located located, string parentId, int? index)
	{
		var group = located.Group!;
		int rawIndex;

		lock (_appSettings.SyncRoot)
		{
			var currentParentId = located.ParentGroup?.NodeId ?? located.Directory.NodeId;
			if (parentId != currentParentId)
			{
				if (Locate(parentId) == null)
					throw new TdcpException(Constants.TreeMutation.ParentNotFound);
				throw new TdcpException(Constants.TreeMutation.GroupMoveRefused);
			}

			var siblings = located.ParentGroup?.Groups ?? located.Directory.Groups;
			rawIndex = Math.Clamp(index ?? 0, 0, siblings.Count(g => g != group));
		}

		if (!_registry.ReorderGroup(located.Directory, located.ParentGroup, group, rawIndex))
			throw new TdcpException(Constants.TreeMutation.NodeNotFound);

		_registry.RaiseDaemonTreeMutation();
		return new TreeMoveResult(true);
	}

	/// <inheritdoc />
	public TreeMoveTargetsResult MoveTargets(string nodeId)
	{
		var located = FindSession(nodeId);

		string sourcePath;
		var candidates = new List<(string Id, string DirectoryPath)>();
		lock (_appSettings.SyncRoot)
		{
			if (IsWorker(located.Session!))
				return new TreeMoveTargetsResult([]);

			sourcePath = located.Directory.Path;
			foreach (var directory in _registry.Directories)
			{
				candidates.Add((directory.NodeId, directory.Path));
				CollectGroups(directory.Groups, directory.Path, candidates);
			}
		}

		var sourceOrigin = _origins.GetOrigin(sourcePath);
		var parentIds    = candidates
			.Where(c => _rules.CanMove(sourceOrigin, _origins.GetOrigin(c.DirectoryPath)))
			.Select(c => c.Id)
			.ToList();
		return new TreeMoveTargetsResult(parentIds);
	}

	/// <inheritdoc />
	public TreeNodeInfoResult NodeInfo(IReadOnlyList<string> nodeIds)
	{
		if (nodeIds.Count > Constants.TreeMutation.NodeInfoMaxIds)
			throw new TdcpInvalidParamsException(Constants.TreeMutation.TooManyNodeIds);

		var found = new List<(string Id, Located Node)>(nodeIds.Count);
		lock (_appSettings.SyncRoot)
		{
			foreach (var id in nodeIds)
			{
				var node = Locate(id);
				if (node != null)
					found.Add((id, node));
			}
		}

		var items = new List<TreeNodeInfoItem>(found.Count);
		foreach (var (id, node) in found)
		{
			var workingDirectory = node.Kind switch
			{
				RemoteProtocol.TreeMutation.NodeKinds.Directory => node.Directory.Path,
				RemoteProtocol.TreeMutation.NodeKinds.Group     => node.Group!.WorkingDirectory,
				_                                               => node.Session!.WorkingDirectory,
			};

			items.Add(new TreeNodeInfoItem(id, node.Kind, node.TreePath, workingDirectory, _origins.GetOrigin(node.Directory.Path), _labels.GetLabel(node.Directory.Path)));
		}

		return new TreeNodeInfoResult(items);
	}

	/// <inheritdoc />
	public Task? RefreshGitOrigins()
	{
		string[] paths;
		lock (_appSettings.SyncRoot)
			paths = [.. _registry.Directories.Select(d => d.Path)];

		return _origins.StartRefresh(paths, _registry.RaiseTreeModified);
	}

	// ── lookup ────────────────────────────────────────────────────────────────

	private static string RequireName(string name)
	{
		name = name.Trim();
		return name.Length == 0 ? throw new TdcpInvalidParamsException(Constants.TreeMutation.NameRequired) : name;
	}

	private Located Find(string nodeId)
	{
		lock (_appSettings.SyncRoot)
			return Locate(nodeId) ?? throw new TdcpException(Constants.TreeMutation.NodeNotFound);
	}

	private Located FindSession(string nodeId)
	{
		var located = Find(nodeId);
		return located.Kind == RemoteProtocol.TreeMutation.NodeKinds.Session
			? located
			: throw new TdcpException(Constants.TreeMutation.OnlySessionsMove);
	}

	/// <summary>Finds a directory, group or session by NodeId; the caller holds the settings lock.</summary>
	private Located? Locate(string nodeId)
	{
		foreach (var directory in _registry.Directories)
		{
			if (directory.NodeId == nodeId)
				return new Located(RemoteProtocol.TreeMutation.NodeKinds.Directory, directory, null, null, null, string.Empty);

			var found = LocateIn(directory, null, directory.Groups, directory.Sessions, nodeId, string.Empty);
			if (found != null)
				return found;
		}

		return null;
	}

	private static Located? LocateIn(DirectoryNodeModel directory, GroupNodeModel? parent, List<GroupNodeModel> groups, List<SessionNodeModel> sessions, string nodeId, string prefix)
	{
		foreach (var session in sessions)
		{
			if (session.NodeId == nodeId)
				return new Located(RemoteProtocol.TreeMutation.NodeKinds.Session, directory, parent, null, session, prefix + session.Name);
		}

		foreach (var group in groups)
		{
			var path = prefix + group.Name;
			if (group.NodeId == nodeId)
				return new Located(RemoteProtocol.TreeMutation.NodeKinds.Group, directory, parent, group, null, path);

			var found = LocateIn(directory, group, group.Groups, group.Sessions, nodeId, path + " / ");
			if (found != null)
				return found;
		}

		return null;
	}

	private static void CollectGroups(List<GroupNodeModel> groups, string directoryPath, List<(string Id, string DirectoryPath)> into)
	{
		foreach (var group in groups)
		{
			into.Add((group.NodeId, directoryPath));
			CollectGroups(group.Groups, directoryPath, into);
		}
	}

	// ── workers and positions ─────────────────────────────────────────────────

	/// <summary>A worker is a session whose supervisor is still in the tree (as <c>tree.snapshot</c> counts it); the caller holds the settings lock.</summary>
	private bool IsWorker(SessionNodeModel session) =>
		!string.IsNullOrEmpty(session.SupervisorNodeId) && _registry.FindModelByNodeId(session.SupervisorNodeId) != null;

	private bool HasWorkers(string nodeId)
	{
		lock (_appSettings.SyncRoot)
			return AnyWorkerOf(nodeId);
	}

	private bool AnyWorkerOf(string supervisorId)
	{
		foreach (var directory in _registry.Directories)
		{
			if (directory.Sessions.Any(s => s.SupervisorNodeId == supervisorId) || GroupsHaveWorkerOf(directory.Groups, supervisorId))
				return true;
		}

		return false;
	}

	private static bool GroupsHaveWorkerOf(List<GroupNodeModel> groups, string supervisorId) =>
		groups.Any(g => g.Sessions.Any(s => s.SupervisorNodeId == supervisorId) || GroupsHaveWorkerOf(g.Groups, supervisorId));

	/// <summary>
	/// Turns a top-level position (workers are not counted, the moved session is not in the list) into a position of the raw
	/// <paramref name="siblings"/> list: the slot just before the top-level sibling at <paramref name="topLevelIndex"/>, or the end.
	/// </summary>
	private int RawIndex(List<SessionNodeModel> siblings, SessionNodeModel moved, int topLevelIndex)
	{
		var rest     = siblings.Where(s => s != moved).ToList();
		var topLevel = 0;
		for (var i = 0; i < rest.Count; i++)
		{
			if (IsWorker(rest[i]))
				continue;

			if (topLevel == topLevelIndex)
				return i;
			topLevel++;
		}

		return rest.Count;
	}
}
