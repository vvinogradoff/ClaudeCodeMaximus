using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The Recent tab (FR.1.16): every top-level session grouped per directory with collapsible headers, drafts first, then newest last activity
/// first. It is built on the client from the tree's snapshot and pushed states (no extra call), rebuilt when the tree or a state changes
/// (throttled while sessions stream) and each time the tab is selected, when every group except the selected session's directory collapses.
/// Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentTabViewModel : IDisposable
{
	private readonly ISessionListActions _actions;
	private readonly Throttle _throttle;
	private readonly ILogger<RecentTabViewModel> _logger;
	private TreeViewModel? _tree;

	public RecentTabViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<RecentTabViewModel> logger)
		: this(actions, ui, logger, Constants.Timeouts.ListRebuildThrottle)
	{
	}

	public RecentTabViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<RecentTabViewModel> logger, TimeSpan throttle)
	{
		_actions = actions;
		_logger = logger;
		_throttle = new Throttle(throttle, ui);
		OpenCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<SessionRowViewModel>(row =>
		{
			if (row != null)
				_actions.Open(row.Id, row.Name);
		});
		ToggleFavoriteCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<SessionRowViewModel>(ToggleFavoriteAsync);
		List = new GroupedSessionListViewModel(OpenCommand, ToggleFavoriteCommand);
	}

	public GroupedSessionListViewModel List { get; }

	public System.Windows.Input.ICommand OpenCommand { get; }

	public System.Windows.Input.ICommand ToggleFavoriteCommand { get; }

	public bool IsEmpty => List.IsEmpty;

	/// <summary>Listens to <paramref name="tree"/> and builds the list from it; replaces a previous tree.</summary>
	public void Attach(TreeViewModel tree)
	{
		Detach();
		_tree = tree;
		tree.ListsChanged += OnListsChanged;
		Rebuild();
	}

	private void Detach()
	{
		if (_tree != null)
			_tree.ListsChanged -= OnListsChanged;

		_tree = null;
	}

	public void Dispose() => Detach();

	private void OnListsChanged(object? sender, EventArgs e) => _throttle.Request(Rebuild);

	/// <summary>The tab was selected: rebuild, then collapse every group except the selected session's directory (all expanded with no selection).</summary>
	public void Activate()
	{
		Rebuild();
		var selected = _tree?.SelectedNodeId;
		List.CollapseAllExcept(selected == null ? null : _tree?.DirectoryIdOf(selected));
	}

	/// <summary>Re-reads the time-dependent marks of the rows (called every 60 seconds).</summary>
	public void RefreshClock()
	{
		foreach (var row in List.Items.OfType<SessionRowViewModel>())
			row.RefreshClock();
	}

	/// <summary>Re-reads the date text of the rows (the date format setting changed).</summary>
	public void RefreshDisplay()
	{
		foreach (var row in List.Items.OfType<SessionRowViewModel>())
			row.RefreshDisplay();
	}

	public void Rebuild()
	{
		var tree = _tree;
		if (tree == null)
			return;

		var groups = new List<SessionGroupData>();
		foreach (var group in tree.TopLevelSessions())
		{
			var rows = group.Sessions
				.Select(node => (Node: node, State: node.State ?? new SessionStateDto(node.Id, false, false, null, false, null, null, false, null)))
				.OrderByDescending(x => x.State.HasDraft)
				.ThenByDescending(x => x.State.LastActivityUtc ?? DateTimeOffset.MinValue)
				.Select(x => new SessionRowViewModel(x.Node.Id, x.Node.Label, group.DirectoryLabel, x.State, showDirectory: false, canToggleFavorite: true))
				.ToList();
			groups.Add(new SessionGroupData(group.DirectoryId, group.DirectoryLabel, rows));
		}

		List.Show(groups);
	}

	private async Task ToggleFavoriteAsync(SessionRowViewModel? row)
	{
		if (row == null)
			return;

		var value = await _actions.SetFavoriteAsync(row.Id, !row.IsFavorite);
		if (value != null)
			row.State = row.State with { Favorite = value.Value };
		else
			_logger.LogDebug("Favourite of {NodeId} not changed", row.Id);
	}
}
