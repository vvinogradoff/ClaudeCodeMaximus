using System.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Owns the session lists of the window and ties them to the shell: the Recent tab (built from the tree), the Recent dropdown
/// (<c>recent.list</c>) and the Tag search. It attaches them when the connection is ready, whether the host was created before or after that
/// (a client that is already there is attached at construction), opens a picked session through the tree and the pane, and sets favourites.
/// Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListsHost : ISessionListActions, IDisposable
{
	private readonly MainViewModel _main;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<SessionListsHost> _logger;
	private readonly CancellationTokenSource _lifetime = new();
	private ITessynClient? _client;
	private TreeViewModel? _tree;
	private SessionPaneViewModel? _observed;
	private TreeNodeViewModel? _observedNode;
	private readonly ListDisplayService? _display;

	public SessionListsHost(ConnectionViewModel connection, MainViewModel main, IUiDispatcher ui, ILoggerFactory loggers, ISystemUrlLauncher? launcher = null, ListDisplayService? display = null)
	{
		_main = main;
		_ui = ui;
		_logger = loggers.CreateLogger<SessionListsHost>();
		RecentTab = new RecentTabViewModel(this, ui, loggers.CreateLogger<RecentTabViewModel>());
		Recent = new RecentViewModel(this, ui, loggers.CreateLogger<RecentViewModel>());
		_display = display;
		if (display != null)
			Recent.ShowRecencyColour = display.ShowRecencyInRecentDropdown;
		Tags = new TagSearchViewModel(this, ui, loggers.CreateLogger<TagSearchViewModel>(), Constants.Timeouts.SearchDebounce, launcher);
		connection.ClientReady += OnClientReady;
		if (display != null)
			display.Changed += (_, _) => RefreshDisplay();
		main.PropertyChanged += OnMainChanged;

		// The order of construction and of the first ClientReady is not guaranteed: pick up what is already there.
		if (connection.Client != null)
			OnClientReady(connection.Client);

		AttachTree();
		ObserveSession();
		_ = RunClockAsync(_lifetime.Token);
	}

	/// <summary>The Recent tab: every top-level session per directory (FR.1.16).</summary>
	public RecentTabViewModel RecentTab { get; }

	/// <summary>The top-bar dropdown: the daemon's <c>recent.list</c>.</summary>
	public RecentViewModel Recent { get; }

	public TagSearchViewModel Tags { get; }

	private void OnClientReady(ITessynClient client)
	{
		if (ReferenceEquals(client, _client))
			return;

		_client = client;
		_ = Recent.AttachAsync(client);
		_ = Tags.AttachAsync(client);
	}

	private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainViewModel.Tree))
			AttachTree();
		else if (e.PropertyName == nameof(MainViewModel.Session))
			ObserveSession();
	}

	/// <summary>The Recent dropdown's button shows the name of the open session: follow the pane's title.</summary>
	private void ObserveSession()
	{
		if (_observed != null)
			_observed.PropertyChanged -= OnSessionChanged;

		_observed = _main.Session;
		if (_observed != null)
			_observed.PropertyChanged += OnSessionChanged;

		ShowSessionName();
	}

	private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SessionPaneViewModel.Title) or nameof(SessionPaneViewModel.NodeId))
			ShowSessionName();
	}

	private void ShowSessionName()
	{
		Recent.CurrentName = _observed is { NodeId: not null } pane ? pane.Title : string.Empty;
		ShowSessionRecency();
	}

	/// <summary>The closed dropdown button takes the recency tier of the open session, read from its tree node (the same level its row shows).</summary>
	private void ShowSessionRecency()
	{
		var node = _observed is { NodeId: { } id } ? _tree?.FindNode(id) : null;
		Recent.CurrentRecencyLevel = node?.RecencyLevel ?? 0;
		Recent.RunningWorkerCount = node?.RunningWorkerCount ?? 0;
		Recent.CurrentProfileName = node?.ProfileName ?? string.Empty;
		Recent.CurrentSwitchingToName = node?.SwitchingToProfileName ?? string.Empty;

		// The badge of the closed button is live: the open session's own node tells when its state (the running workers, the tier) changes.
		if (!ReferenceEquals(node, _observedNode))
		{
			if (_observedNode != null)
				_observedNode.PropertyChanged -= OnObservedNodeChanged;

			_observedNode = node;
			if (_observedNode != null)
				_observedNode.PropertyChanged += OnObservedNodeChanged;
		}
	}

	private void OnObservedNodeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(TreeNodeViewModel.RunningWorkerCount) or nameof(TreeNodeViewModel.RecencyLevel) or nameof(TreeNodeViewModel.ProfileName) or nameof(TreeNodeViewModel.SwitchingToProfileName) && sender is TreeNodeViewModel node)
		{
			Recent.CurrentRecencyLevel = node.RecencyLevel;
			Recent.RunningWorkerCount = node.RunningWorkerCount;
			Recent.CurrentProfileName = node.ProfileName;
			Recent.CurrentSwitchingToName = node.SwitchingToProfileName;
		}
	}

	private void AttachTree()
	{
		var tree = _main.Tree;
		if (tree == null || ReferenceEquals(tree, _tree))
			return;

		_tree = tree;
		tree.ListsChanged += (_, _) => ShowSessionRecency();   // a session's activity changed: its tier may have too
		ShowSessionRecency();
		RecentTab.Attach(tree);
		Tags.AttachTree(tree);
	}

	/// <summary>The recency of every row (the lists and the tree) ages with the clock.</summary>
	private void RefreshClock()
	{
		ShowSessionRecency();
		RecentTab.RefreshClock();
		_tree?.RefreshClock();
	}

	/// <summary>The date format changed: every row re-reads its date text.</summary>
	private void RefreshDisplay()
	{
		if (_display != null)
			Recent.ShowRecencyColour = _display.ShowRecencyInRecentDropdown;

		RecentTab.RefreshDisplay();
		Tags.RefreshDisplay();
		_tree?.RefreshDisplay();
	}

	/// <summary>The recency colours age with time (FR.9.5): re-read them every minute.</summary>
	private async Task RunClockAsync(CancellationToken token)
	{
		try
		{
			using var timer = new PeriodicTimer(Constants.Timeouts.RecencyRefresh);
			while (await timer.WaitForNextTickAsync(token))
				_ui.Post(RefreshClock);
		}
		catch (OperationCanceledException)
		{
			// shutting down
		}
	}

	public void Dispose()
	{
		if (_observed != null)
			_observed.PropertyChanged -= OnSessionChanged;

		if (_observedNode != null)
			_observedNode.PropertyChanged -= OnObservedNodeChanged;

		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	/// <inheritdoc />
	public void Open(string nodeId, string name)
	{
		var session = _main.Session;
		if (session == null)
		{
			_logger.LogWarning("Cannot open {NodeId} from a session list: the session pane does not exist yet", nodeId);
			return;
		}

		// The tree selection opens the session (and expands its parents); a session the tree does not know is opened directly.
		var selected = _main.Tree?.SelectNodeById(nodeId) == true;
		if (session.NodeId != nodeId)
			_ = session.OpenAsync(nodeId, name);

		_logger.LogInformation("Opened {NodeId} from a session list (tree row {InTree})", nodeId, selected);
	}

	/// <inheritdoc />
	public async Task<bool?> SetFavoriteAsync(string nodeId, bool value)
	{
		var client = _client;
		if (client == null)
			return null;

		try
		{
			var result = await client.SessionSetFavoriteAsync(nodeId, value);
			return result.Value;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "session.setFavorite failed for {NodeId}", nodeId);
			return null;
		}
	}
}
