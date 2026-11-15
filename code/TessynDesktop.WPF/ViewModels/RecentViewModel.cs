using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The Recent list (FR.9.6-9.8, FR.1.16) behind both the Recent tab and the title-bar dropdown: the daemon's <c>recent.list</c> in the
/// daemon's order (pinned and newest first), hide through <c>recent.hide</c>, favourite through <c>session.setFavorite</c>. It refreshes on
/// <c>session.changed</c>: a known row only takes the new state, a session not listed yet or a changed favourite reloads the list.
/// Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class RecentViewModel : ObservableObject, IDisposable
{
	private readonly IUiDispatcher _ui;
	private readonly ISessionListActions _actions;
	private readonly ILogger<RecentViewModel> _logger;
	private readonly Throttle _throttle;
	private ITessynClient? _client;
	private readonly Dictionary<string, int> _hiddenAt = new(StringComparer.Ordinal);
	private int _loadSequence;
	private bool _refreshRunning;
	private bool _refreshPending;
	private Task _refreshTask = Task.CompletedTask;

	[ObservableProperty]
	private string? _error;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsEmpty), nameof(EmptyText))]
	private bool _loaded;

	public RecentViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<RecentViewModel> logger)
		: this(actions, ui, logger, Constants.Timeouts.ListRebuildThrottle)
	{
	}

	public RecentViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<RecentViewModel> logger, TimeSpan throttle)
	{
		_actions = actions;
		_ui = ui;
		_logger = logger;
		_throttle = new Throttle(throttle, ui);
		Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
	}

	/// <summary>The listed sessions, in the daemon's order.</summary>
	public ObservableCollection<SessionRowViewModel> Rows { get; } = [];

	public bool IsEmpty => Loaded && Rows.Count == 0;

	public string EmptyText => Constants.SessionLists.RecentEmptyText;

	/// <summary>The task of the latest refresh (tests await it).</summary>
	public Task RefreshTask => _refreshTask;

	/// <summary>Starts listening to <paramref name="client"/> and loads the list; replaces a previous client.</summary>
	public Task AttachAsync(ITessynClient client)
	{
		Detach();
		_client = client;
		client.NotificationReceived += OnNotification;
		client.Resynced += OnResynced;
		return RefreshAsync();
	}

	private void Detach()
	{
		if (_client == null)
			return;

		_client.NotificationReceived -= OnNotification;
		_client.Resynced -= OnResynced;
		_client = null;
	}

	public void Dispose() => Detach();

	/// <summary>Reloads the list; calls that arrive while a load runs share one more load afterwards.</summary>
	[RelayCommand]
	public Task RefreshAsync()
	{
		if (_client == null)
			return Task.CompletedTask;

		if (_refreshRunning)
		{
			_refreshPending = true;
			return _refreshTask;
		}

		_refreshRunning = true;
		_refreshTask = RefreshLoopAsync();
		return _refreshTask;
	}

	private async Task RefreshLoopAsync()
	{
		try
		{
			do
			{
				_refreshPending = false;
				var client = _client;
				if (client == null)
					return;

				var sequence = ++_loadSequence;
				try
				{
					var result = await client.RecentListAsync();
					if (!ReferenceEquals(client, _client))
					{
						_refreshPending = true;   // another client was attached meanwhile: its answer is the one to show
						continue;
					}

					Apply(result, sequence);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogWarning(ex, "recent.list failed");
					if (ReferenceEquals(client, _client))
						Error = ex.Message;
				}
			}
			while (_refreshPending);
		}
		finally
		{
			_refreshRunning = false;
		}
	}

	private void Apply(RecentListResult result, int sequence)
	{
		Error = null;
		// A load that was already running when the user hid a row still lists it: it must not bring the row back.
		var sessions = result.Sessions.Where(s => !_hiddenAt.TryGetValue(s.Id, out var at) || sequence > at).ToList();
		foreach (var id in _hiddenAt.Where(h => sequence > h.Value).Select(h => h.Key).ToList())
			_hiddenAt.Remove(id);

		var existing = Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
		var ordered = new List<SessionRowViewModel>(sessions.Count);
		foreach (var dto in sessions)
		{
			if (existing.TryGetValue(dto.Id, out var row))
			{
				row.Name = dto.Name;
				row.State = dto.State;
				row.Pinned = dto.Pinned;
				row.CanHide = dto.CanHide;
				ordered.Add(row);
			}
			else
			{
				var created = SessionRowViewModel.From(dto);
				created.OpenCommand = OpenCommand;
				created.ToggleFavoriteCommand = ToggleFavoriteCommand;
				created.HideCommand = HideCommand;
				ordered.Add(created);
			}
		}

		// Keep the row objects (the selection and focus live on them) and only move, add and remove what changed.
		for (var i = Rows.Count - 1; i >= 0; i--)
		{
			if (!ordered.Contains(Rows[i]))
				Rows.RemoveAt(i);
		}

		for (var i = 0; i < ordered.Count; i++)
		{
			if (i < Rows.Count && ReferenceEquals(Rows[i], ordered[i]))
				continue;

			var at = Rows.IndexOf(ordered[i]);
			if (at >= 0)
				Rows.Move(at, i);
			else
				Rows.Insert(i, ordered[i]);
		}

		Loaded = true;
	}

	/// <summary>Opens the row's session.</summary>
	[RelayCommand]
	private void Open(SessionRowViewModel? row)
	{
		if (row != null)
			_actions.Open(row.Id, row.Name);
	}

	/// <summary>Hides the row from the list (<c>recent.hide</c>); a pinned row cannot be hidden.</summary>
	[RelayCommand]
	private async Task HideAsync(SessionRowViewModel? row)
	{
		var client = _client;
		if (row == null || client == null || !row.CanHide)
			return;

		try
		{
			var result = await client.RecentHideAsync(row.Id);
			if (result.Hidden)
			{
				_hiddenAt[row.Id] = _loadSequence;   // loads started up to now may still list it
				Rows.Remove(row);
			}
			else
				await RefreshAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "recent.hide failed for {NodeId}", row.Id);
			Error = ex.Message;
		}
	}

	/// <summary>Flips the row's favourite flag; the list reloads, because a favourite is pinned (and an unpinned one may drop out).</summary>
	[RelayCommand]
	private async Task ToggleFavoriteAsync(SessionRowViewModel? row)
	{
		if (row == null)
			return;

		var value = await _actions.SetFavoriteAsync(row.Id, !row.IsFavorite);
		if (value == null)
			return;

		row.State = row.State with { Favorite = value.Value };
		await RefreshAsync();
	}

	private void OnResynced(object? sender, ResyncedEventArgs e) => _ui.Post(() => _ = RefreshAsync());

	/// <summary>Reloads at most once per throttle interval (a turn streaming pushes many session.changed events).</summary>
	private void ScheduleRefresh() => _throttle.Request(() => _ = RefreshAsync());

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (notification.Method != RemoteProtocol.Events.SessionChanged)
			return;

		var payload = notification.Params.Deserialize<SessionChangedPayload>(TdcpJsonOptions.Default);
		if (payload != null)
			_ui.Post(() => OnSessionChanged(payload.State));
	}

	private void OnSessionChanged(SessionStateDto state)
	{
		var row = Rows.FirstOrDefault(r => r.Id == state.Id);
		if (row == null)
		{
			ScheduleRefresh();   // a session that just got active enters the list
			return;
		}

		// Anything that moves a row or changes what pins it: last activity (the order, the 24 h window), running, attention (a Focus item pins), favourite.
		var reorder = row.State.LastActivityUtc != state.LastActivityUtc
			|| row.IsRunning != state.Running
			|| row.AttentionCount != (state.Attention?.Count ?? 0)
			|| row.IsFavorite != state.Favorite;
		row.State = state;
		if (reorder)
			ScheduleRefresh();
	}
}
