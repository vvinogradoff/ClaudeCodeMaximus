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

	public SessionListsHost(ConnectionViewModel connection, MainViewModel main, IUiDispatcher ui, ILoggerFactory loggers, IUrlLauncher? launcher = null)
	{
		_main = main;
		_ui = ui;
		_logger = loggers.CreateLogger<SessionListsHost>();
		RecentTab = new RecentTabViewModel(this, ui, loggers.CreateLogger<RecentTabViewModel>());
		Recent = new RecentViewModel(this, ui, loggers.CreateLogger<RecentViewModel>());
		Tags = new TagSearchViewModel(this, ui, loggers.CreateLogger<TagSearchViewModel>(), Constants.Timeouts.SearchDebounce, launcher);
		connection.ClientReady += OnClientReady;
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

	private void ShowSessionName() => Recent.CurrentName = _observed is { NodeId: not null } pane ? pane.Title : string.Empty;

	private void AttachTree()
	{
		var tree = _main.Tree;
		if (tree == null || ReferenceEquals(tree, _tree))
			return;

		_tree = tree;
		RecentTab.Attach(tree);
		Tags.AttachTree(tree);
	}

	/// <summary>The recency colours age with time (FR.9.5): re-read them every minute.</summary>
	private async Task RunClockAsync(CancellationToken token)
	{
		try
		{
			using var timer = new PeriodicTimer(Constants.Timeouts.RecencyRefresh);
			while (await timer.WaitForNextTickAsync(token))
				_ui.Post(RecentTab.RefreshClock);
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
