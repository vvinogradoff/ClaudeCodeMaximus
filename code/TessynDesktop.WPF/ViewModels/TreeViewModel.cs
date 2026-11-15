using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The session tree: built from <c>tree.snapshot</c> (directories, groups, top-level sessions), workers loaded lazily with
/// <c>tree.workers</c> on expand, refreshed on <c>tree.changed</c> by version, searched with <c>tree.search</c> (debounced).
/// The visible rows are kept as a flat list so the list control can virtualize them. Selection and expansion are client state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TreeViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<TreeViewModel> _logger;
	private readonly TimeSpan _searchDebounce;
	private readonly TreeNodeInfoLoader? _infoLoader;

	private readonly Dictionary<string, TreeNodeViewModel> _byId = new();
	private readonly HashSet<string> _expanded = new();
	private List<TreeNodeViewModel> _roots = [];
	private List<TreeNodeViewModel>? _searchRoots;
	private bool _expansionSeeded;
	private bool _rebuilding;
	private string? _selectedId;
	private bool _selectedIsWorker;
	private long _version = -1;

	private CancellationTokenSource? _searchCts;
	private Task _searchTask = Task.CompletedTask;
	private Task _refreshTask = Task.CompletedTask;
	private Task _infoTask = Task.CompletedTask;
	private int _infoGeneration;
	private bool _refreshRunning;
	private bool _refreshPending;
	private bool _forceNext;

	[ObservableProperty]
	private string _searchText = string.Empty;

	[ObservableProperty]
	private TreeNodeViewModel? _selectedNode;

	[ObservableProperty]
	private string? _statusText;

	[ObservableProperty]
	private int _sessionCount;

	[ObservableProperty]
	private bool _isSearching;

	public TreeViewModel(ITessynClient client, IUiDispatcher ui, ILogger<TreeViewModel> logger)
		: this(client, ui, logger, Constants.Timeouts.SearchDebounce)
	{
	}

	public TreeViewModel(ITessynClient client, IUiDispatcher ui, ILogger<TreeViewModel> logger, TimeSpan searchDebounce, TreeOperations? operations = null, TreeNodeInfoLoader? infoLoader = null)
	{
		_client = client;
		_ui = ui;
		_logger = logger;
		_searchDebounce = searchDebounce;
		_infoLoader = infoLoader;
		Operations = operations;
		operations?.Attach(this);
		_client.NotificationReceived += OnNotification;
		_client.Resynced += OnResynced;
	}

	/// <summary>Add, rename, delete and move; null in a tree without the daemon's tree mutation methods wired.</summary>
	public TreeOperations? Operations { get; }

	/// <summary>The visible rows, flattened (bound to a virtualizing list).</summary>
	public FlatRowCollection Rows { get; } = new();

	/// <summary>Structural version of the tree the rows were built from; -1 before the first load.</summary>
	public long Version => _version;

	/// <summary>Raised on the UI thread when the user (or a restore) selects a node.</summary>
	public event EventHandler<TreeNodeViewModel>? SelectionChanged;

	/// <summary>
	/// Raised when the selected node is gone from the tree (deleted here or by another client); the argument is its id. The selection is cleared first.
	/// A selected worker whose supervisor is not expanded is not counted as gone: workers are only in the tree while loaded.
	/// </summary>
	public event EventHandler<string>? SelectionLost;

	/// <summary>Raised when the set of expanded nodes changed (for persistence).</summary>
	public event EventHandler? ExpansionChanged;

	/// <summary>Ids of the expanded directories, groups and sessions.</summary>
	public IReadOnlyCollection<string> ExpandedNodeIds => _expanded.ToArray();

	/// <summary>The id of the last selected node (kept when the selection is cleared by a rebuild).</summary>
	public string? SelectedNodeId => _selectedId;

	/// <summary>Seeds the expansion from client state; without it the daemon's flags are the initial state.</summary>
	public void RestoreExpansion(IEnumerable<string>? expandedIds)
	{
		if (expandedIds == null)
			return;

		_expanded.UnionWith(expandedIds);
		_expansionSeeded = true;
	}

	/// <summary>Loads the tree and waits until the rows are built.</summary>
	public async Task LoadAsync()
	{
		RequestRefresh(force: true);
		await WhenIdleAsync();
	}

	/// <summary>Completes when no refresh and no search is running.</summary>
	public async Task WhenIdleAsync()
	{
		await _refreshTask;
		await _searchTask;
		await _infoTask;
	}

	/// <summary>Asks the daemon for the snapshot now (the version check makes it cheap when nothing changed) and waits until the rows are rebuilt.</summary>
	public async Task RefreshAsync()
	{
		RequestRefresh(force: false);
		await WhenIdleAsync();
	}

	/// <summary>The node with this id, or null when it is not in the tree (a worker that was not loaded yet).</summary>
	public TreeNodeViewModel? FindNode(string nodeId) => _byId.GetValueOrDefault(nodeId);

	/// <summary>Selects a node by id (expanding its ancestors) and raises <see cref="SelectionChanged"/>. False when unknown (e.g. a worker not loaded yet).</summary>
	public bool SelectNodeById(string nodeId)
	{
		if (!RevealNode(nodeId))
			return false;

		SelectedNode = _byId[nodeId];
		return true;
	}

	/// <summary>Expands the ancestors of a node so its row is visible, without selecting it. False when the node is unknown.</summary>
	public bool RevealNode(string nodeId)
	{
		if (!_byId.TryGetValue(nodeId, out var node))
			return false;

		var changed = false;
		for (var parent = node.Parent; parent != null; parent = parent.Parent)
		{
			changed |= !parent.IsExpanded;
			parent.IsExpanded = true;
			_expanded.Add(parent.Id);
		}

		ApplyRows();
		if (changed)
			ExpansionChanged?.Invoke(this, EventArgs.Empty);

		return true;
	}

	/// <summary>
	/// Fills in the tooltip (path on disk, git origin) of a node from <c>tree.nodeInfo</c>, once. Directories get theirs with the tree; a group or a session asks
	/// when its tooltip opens, so a tree of thousands of sessions costs nothing until the user hovers.
	/// </summary>
	public async Task EnsureInfoAsync(TreeNodeViewModel node)
	{
		if (_infoLoader == null || node.InfoLoaded)
			return;

		node.InfoLoaded = true;
		var items = await _infoLoader.LoadAsync([node.Id]);
		if (items.Count == 0)
			node.InfoLoaded = false;

		ApplyInfo(items);
	}

	private void ApplyInfo(IReadOnlyList<TessynDesktop.Models.Remote.TreeNodeInfoItem> items)
	{
		foreach (var item in items)
		{
			if (!_byId.TryGetValue(item.NodeId, out var node))
				continue;

			node.InfoLoaded = true;
			if (node.Kind == TreeNodeKind.Directory)
				node.GitOrigin = item.GitOrigin;

			node.ToolTipText = BuildToolTip(node, item);
		}
	}

	private static string BuildToolTip(TreeNodeViewModel node, TessynDesktop.Models.Remote.TreeNodeInfoItem item)
	{
		var lines = new List<string>();
		if (!string.IsNullOrEmpty(item.Path))
			lines.Add(item.Path);
		else
			lines.Add(node.Label);

		if (!string.IsNullOrEmpty(item.WorkingDirectory))
			lines.Add(item.WorkingDirectory);

		if (!string.IsNullOrEmpty(item.GitOrigin))
			lines.Add(Constants.TreeTexts.GitOriginPrefix + item.GitOrigin);

		return string.Join(Environment.NewLine, lines);
	}

	/// <summary>Asks for the info of every directory (one call): their git-origin labels and tooltips. The groups and sessions follow lazily.</summary>
	private void LoadDirectoryInfo()
	{
		if (_infoLoader == null)
			return;

		var generation = ++_infoGeneration;
		var ids = _roots.Where(r => r.Kind == TreeNodeKind.Directory).Select(r => r.Id).ToList();
		_infoTask = LoadDirectoryInfoAsync(ids, generation);
	}

	private async Task LoadDirectoryInfoAsync(IReadOnlyList<string> ids, int generation)
	{
		if (ids.Count == 0)
			return;

		var items = await _infoLoader!.LoadAsync(ids);
		// A newer load (or a rebuild) replaced the nodes these answers belong to.
		if (generation == _infoGeneration)
			ApplyInfo(items);
	}

	[RelayCommand]
	private async Task ToggleExpandAsync(TreeNodeViewModel? node)
	{
		if (node == null || !node.CanExpand)
			return;

		var isSearchRow = node.WorkerFilter != null && node.Parent == null;
		if (node.IsExpanded)
		{
			node.IsExpanded = false;
			if (!isSearchRow)
				_expanded.Remove(node.Id);
		}
		else
		{
			if (node.IsSessionLike && !node.WorkersLoaded)
				await LoadWorkersAsync(node);

			node.IsExpanded = true;
			if (!isSearchRow)
				_expanded.Add(node.Id);
		}

		ApplyRows();
		ExpansionChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectedNodeChangedCore(TreeNodeViewModel? value)
	{
		if (_rebuilding || value == null)
			return;

		_selectedId = value.Id;
		_selectedIsWorker = value.Kind == TreeNodeKind.Worker;
		SelectionChanged?.Invoke(this, value);
	}

	partial void OnSearchTextChanged(string value)
	{
		_searchCts?.Cancel();
		_searchCts = new CancellationTokenSource();
		_searchTask = RunSearchAsync(value, _searchCts.Token);
	}

	private async Task RunSearchAsync(string text, CancellationToken cancellationToken)
	{
		var query = text.Trim();
		if (query.Length == 0)
		{
			ExitSearch();
			return;
		}

		try
		{
			await Task.Delay(_searchDebounce, cancellationToken);
			var result = await _client.TreeSearchAsync(query, cancellationToken: cancellationToken);
			if (cancellationToken.IsCancellationRequested)
				return;

			EnterSearch(result);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "tree.search failed");
			StatusText = "Search failed: " + ex.Message;
		}
	}

	private void EnterSearch(TreeSearchResult result)
	{
		var roots = new List<TreeNodeViewModel>();
		foreach (var match in result.Matches)
		{
			if (!_byId.TryGetValue(match.Id, out var source))
				continue;

			roots.Add(new TreeNodeViewModel(match.Id, TreeNodeKind.Session, source.Label, null, match.WorkerIds.Count)
			{
				WorkerFilter = new HashSet<string>(match.WorkerIds),
				State = source.State,
				MatchSummary = match.Strict > 0 ? $"{match.Strict} exact, {match.Loose} loose" : $"{match.Loose} loose",
			});
		}

		_searchRoots = roots;
		IsSearching = true;
		StatusText = $"{roots.Count} matching sessions";
		ApplyRows();
	}

	private void ExitSearch()
	{
		if (_searchRoots == null && !IsSearching)
			return;

		_searchRoots = null;
		IsSearching = false;
		StatusText = null;
		ApplyRows();
	}

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		switch (notification.Method)
		{
			case RemoteProtocol.Events.TreeChanged:
				var changed = notification.Params.Deserialize<TreeChangedPayload>(TdcpJsonOptions.Default);
				if (changed != null && changed.Version != _version)
					_ui.Post(() => RequestRefresh(force: false));
				else if (changed != null && _infoLoader != null)
					_ui.Post(LoadDirectoryInfo);
				break;
			case RemoteProtocol.Events.SessionChanged:
				var session = notification.Params.Deserialize<SessionChangedPayload>(TdcpJsonOptions.Default);
				if (session != null)
					_ui.Post(() => ApplyState(session.State));
				break;
		}
	}

	private void OnResynced(object? sender, ResyncedEventArgs e)
	{
		_ui.Post(() => RequestRefresh(force: true));
	}

	private void RequestRefresh(bool force)
	{
		_forceNext |= force;
		if (_refreshRunning)
		{
			_refreshPending = true;
			return;
		}

		_refreshRunning = true;
		_refreshTask = RunRefreshLoopAsync();
	}

	private async Task RunRefreshLoopAsync()
	{
		try
		{
			do
			{
				_refreshPending = false;
				var force = _forceNext;
				_forceNext = false;
				try
				{
					await RefreshAsync(force);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Tree refresh failed");
					StatusText = "Tree refresh failed: " + ex.Message;
				}
			}
			while (_refreshPending);
		}
		finally
		{
			_refreshRunning = false;
		}
	}

	private async Task RefreshAsync(bool force)
	{
		var snapshot = await _client.TreeSnapshotAsync(force || _version < 0 ? null : _version);
		if (snapshot.Unchanged == true || snapshot.Directories == null)
			return;

		Rebuild(snapshot);
		await ReloadExpandedWorkersAsync();
		ApplyRows();
		LoadDirectoryInfo();
	}

	private void Rebuild(TreeSnapshotResult snapshot)
	{
		var serverExpanded = new List<string>();
		_byId.Clear();
		var roots = new List<TreeNodeViewModel>();
		var sessions = 0;

		foreach (var directory in snapshot.Directories!)
		{
			var node = new TreeNodeViewModel(directory.Id, TreeNodeKind.Directory, directory.Label, null);
			Register(node);
			if (directory.IsExpanded)
				serverExpanded.Add(directory.Id);
			AddGroupsAndSessions(node, directory.Groups, directory.Sessions, serverExpanded, ref sessions);
			roots.Add(node);
		}

		if (!_expansionSeeded)
		{
			_expanded.UnionWith(serverExpanded);
			_expansionSeeded = true;
		}

		foreach (var node in _byId.Values)
			node.IsExpanded = _expanded.Contains(node.Id);

		_roots = roots;
		_version = snapshot.Version;
		SessionCount = sessions;
		_logger.LogInformation("Tree loaded: {Sessions} sessions, {Directories} directories, version {Version}", sessions, roots.Count, snapshot.Version);
	}

	private void AddGroupsAndSessions(TreeNodeViewModel parent, IReadOnlyList<TreeGroupDto> groups, IReadOnlyList<TreeSessionDto> sessions, List<string> serverExpanded, ref int sessionCount)
	{
		foreach (var group in groups)
		{
			var node = new TreeNodeViewModel(group.Id, TreeNodeKind.Group, group.Name, parent);
			Register(node);
			parent.Children.Add(node);
			if (group.IsExpanded)
				serverExpanded.Add(group.Id);
			AddGroupsAndSessions(node, group.Groups, group.Sessions, serverExpanded, ref sessionCount);
		}

		foreach (var session in sessions)
		{
			var node = new TreeNodeViewModel(session.Id, TreeNodeKind.Session, session.Name, parent, session.WorkerCount) { State = session.State };
			Register(node);
			parent.Children.Add(node);
			sessionCount++;
		}
	}

	private void Register(TreeNodeViewModel node) => _byId[node.Id] = node;

	private async Task ReloadExpandedWorkersAsync()
	{
		var queue = new Queue<TreeNodeViewModel>(_byId.Values.Where(n => n.IsSessionLike && n.IsExpanded && n.WorkerCount > 0));
		while (queue.Count > 0)
		{
			var node = queue.Dequeue();
			try
			{
				await LoadWorkersAsync(node);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "tree.workers failed for {NodeId}", node.Id);
				node.IsExpanded = false;
				continue;
			}

			foreach (var worker in node.Children.Where(c => c.IsExpanded && c.WorkerCount > 0))
				queue.Enqueue(worker);
		}
	}

	private async Task LoadWorkersAsync(TreeNodeViewModel node)
	{
		node.IsLoadingWorkers = true;
		try
		{
			var result = await _client.TreeWorkersAsync(node.Id);
			var isSearchRow = node.WorkerFilter != null && node.Parent == null;
			node.Children.Clear();
			foreach (var worker in result.Workers)
			{
				if (node.WorkerFilter != null && !node.WorkerFilter.Contains(worker.Id))
					continue;

				var child = new TreeNodeViewModel(worker.Id, TreeNodeKind.Worker, worker.Name, node, worker.WorkerCount) { State = worker.State };
				child.IsExpanded = !isSearchRow && _expanded.Contains(worker.Id);
				if (!isSearchRow)
					Register(child);
				node.Children.Add(child);
			}

			node.WorkerCount = Math.Max(node.WorkerCount, node.Children.Count);
			node.WorkersLoaded = true;
			node.RefreshExpander();
		}
		finally
		{
			node.IsLoadingWorkers = false;
		}
	}

	private void ApplyRows()
	{
		var visible = new List<TreeNodeViewModel>();
		foreach (var root in _searchRoots ?? _roots)
			Flatten(root, visible);

		var keep = _selectedId;
		_rebuilding = true;
		try
		{
			Rows.Apply(visible);
			if (keep != null)
				SelectedNode = visible.FirstOrDefault(n => n.Id == keep);
		}
		finally
		{
			_rebuilding = false;
		}

		if (keep != null && !_selectedIsWorker && !_byId.ContainsKey(keep))
		{
			_selectedId = null;
			SelectionLost?.Invoke(this, keep);
		}
	}

	private static void Flatten(TreeNodeViewModel node, List<TreeNodeViewModel> output)
	{
		output.Add(node);
		if (!node.IsExpanded)
			return;

		foreach (var child in node.Children)
			Flatten(child, output);
	}

	private void ApplyState(SessionStateDto state)
	{
		if (_byId.TryGetValue(state.Id, out var node))
			node.State = state;

		if (_searchRoots == null)
			return;

		foreach (var root in _searchRoots)
		{
			if (root.Id == state.Id)
				root.State = state;
			foreach (var child in root.Children.Where(c => c.Id == state.Id))
				child.State = state;
		}
	}

	public void Dispose()
	{
		_client.NotificationReceived -= OnNotification;
		_client.Resynced -= OnResynced;
		_searchCts?.Cancel();
		_searchCts?.Dispose();
	}
}
