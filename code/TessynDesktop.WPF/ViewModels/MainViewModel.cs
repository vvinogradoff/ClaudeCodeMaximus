using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Root view model of the main window: the connection banner, the session tree and the status bar. The tree is created when the
/// client is ready. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class MainViewModel : ObservableObject
{
	private readonly Func<ITessynClient, TreeViewModel> _treeFactory;
	private readonly Func<ITessynClient, SessionPaneViewModel> _sessionFactory;
	private readonly IClientStateStore _stateStore;
	private readonly ILogger<MainViewModel> _logger;

	[ObservableProperty]
	private TreeViewModel? _tree;

	[ObservableProperty]
	private SessionPaneViewModel? _session;

	[ObservableProperty]
	private string _statusText = "Not connected";

	/// <summary>A startup failure of the tree or the pane, shown in the status bar (not only logged).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _errorText;

	public bool HasError => !string.IsNullOrEmpty(ErrorText);

	public MainViewModel(ConnectionViewModel connection, Func<ITessynClient, TreeViewModel> treeFactory, Func<ITessynClient, SessionPaneViewModel> sessionFactory, IClientStateStore stateStore, ILogger<MainViewModel> logger, UpdateNoticeViewModel? updateNotice = null)
	{
		UpdateNotice = updateNotice ?? new UpdateNoticeViewModel();
		Connection = connection;
		_treeFactory = treeFactory;
		_sessionFactory = sessionFactory;
		_stateStore = stateStore;
		_logger = logger;
		Connection.ClientReady += OnClientReady;
		Connection.PropertyChanged += (_, _) => UpdateStatus();
	}

	public ConnectionViewModel Connection { get; }

	/// <summary>The "A newer build is ready" notice (hidden unless the live update check found one).</summary>
	public UpdateNoticeViewModel UpdateNotice { get; }

	/// <summary>Finds the daemon and connects; returns when connected or on a terminal state.</summary>
	public Task StartAsync(CancellationToken cancellationToken) => Connection.RunAsync(cancellationToken);

	private async void OnClientReady(ITessynClient client)
	{
		// The session pane and the tree are set up independently: a failure in one must never prevent the other, and is shown to the user.
		TreeViewModel? tree = null;
		try
		{
			tree = _treeFactory(client);
			tree.PropertyChanged += (_, _) => UpdateStatus();
			tree.SelectionChanged += OnTreeSelectionChanged;
			tree.SelectionLost += OnTreeSelectionLost;
			tree.ExpansionChanged += (_, _) => _stateStore.Update(state => state.ExpandedNodeIds = tree.ExpandedNodeIds.ToList());
			tree.RestoreExpansion(_stateStore.State.ExpandedNodeIds);
		}
		catch (Exception ex)
		{
			ReportError("The session tree could not be created", ex);
			tree = null;
		}

		try
		{
			Session = _sessionFactory(client);

			// A jump to another session's entry (or Back) opens it in the pane; the tree follows without reopening it.
			Session.Navigated += (_, nodeId) => Tree?.SelectNodeById(nodeId);
			_ = Session.InitializeAsync();
		}
		catch (Exception ex)
		{
			ReportError("The session pane could not be created", ex);
		}

		if (tree == null)
			return;

		try
		{
			Tree = tree;
			await tree.LoadAsync();
			if (tree.Version < 0)
				ReportError("Loading the session tree failed", new InvalidOperationException(tree.StatusText ?? "no answer from the daemon"));
			UpdateStatus();

			var last = _stateStore.State.LastSelectedNodeId;
			if (last != null)
				tree.SelectNodeById(last);
		}
		catch (Exception ex)
		{
			ReportError("Loading the session tree failed", ex);
		}
	}

	private void ReportError(string what, Exception ex)
	{
		_logger.LogError(ex, "{What}", what);
		ErrorText = $"{what}: {ex.Message}";
		UpdateStatus();
	}

	private void OnTreeSelectionChanged(object? sender, TreeNodeViewModel node)
	{
		_stateStore.Update(state => state.LastSelectedNodeId = node.Id);
		if (!node.IsSessionLike || Session == null)
			return;

		_ = Session.OpenAsync(node.Id, node.Label);
	}

	/// <summary>The selected session was deleted (here or by another client): the pane stops showing it, as Avalonia clears its selected session.</summary>
	private void OnTreeSelectionLost(object? sender, string nodeId)
	{
		if (Session?.NodeId == nodeId)
			Session.Close();

		if (_stateStore.State.LastSelectedNodeId == nodeId)
			_stateStore.Update(state => state.LastSelectedNodeId = null);
	}

	private void UpdateStatus()
	{
		if (Tree == null || Tree.Version < 0)
		{
			StatusText = Connection.BannerText;
			return;
		}

		StatusText = $"{Tree.SessionCount} sessions   tree v{Tree.Version}   {Connection.State}";
	}
}
