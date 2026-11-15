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
/// The Tags tab (FR.1.15, FR.24.11-24.14): a search box over the tags, the chips it finds (selected ones first), and the sessions carrying
/// any selected tag (OR filter) grouped by directory. Everything is the daemon's <c>tags.search</c>; the client only sends the text and the
/// selected tags back. Refreshes on <c>tags.changed</c>; a favourite or running flip arrives as <c>session.changed</c> and updates the rows in place.
/// Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TagSearchViewModel : ObservableObject, IDisposable
{
	private readonly IUiDispatcher _ui;
	private readonly ISessionListActions _actions;
	private readonly ILogger<TagSearchViewModel> _logger;
	private readonly TimeSpan _debounce;
	private readonly List<TagRefDto> _selected = [];
	private ITessynClient? _client;
	private CancellationTokenSource? _searchCts;
	private Task _searchTask = Task.CompletedTask;
	private bool _clearing;

	[ObservableProperty]
	private string _searchText = string.Empty;

	[ObservableProperty]
	private string? _emptyChipsText;

	[ObservableProperty]
	private string? _emptySessionsText;

	[ObservableProperty]
	private string? _error;

	public TagSearchViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<TagSearchViewModel> logger)
		: this(actions, ui, logger, Constants.Timeouts.SearchDebounce)
	{
	}

	public TagSearchViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<TagSearchViewModel> logger, TimeSpan debounce)
	{
		_actions = actions;
		_ui = ui;
		_logger = logger;
		_debounce = debounce;
	}

	/// <summary>Selected chips first, then the other matches.</summary>
	public ObservableCollection<TagChipViewModel> Chips { get; } = [];

	/// <summary>The sessions carrying any selected tag, per directory.</summary>
	public ObservableCollection<TagGroupViewModel> Groups { get; } = [];

	/// <summary>The tags the filter is made of.</summary>
	public IReadOnlyList<TagRefDto> SelectedTags => _selected;

	/// <summary>The task of the latest search (tests await it).</summary>
	public Task SearchTask => _searchTask;

	/// <summary>Starts listening to <paramref name="client"/> and runs the first search; replaces a previous client.</summary>
	public Task AttachAsync(ITessynClient client)
	{
		Detach();
		_client = client;
		client.NotificationReceived += OnNotification;
		client.Resynced += OnResynced;
		return StartSearch(TimeSpan.Zero);
	}

	private void Detach()
	{
		_searchCts?.Cancel();
		if (_client == null)
			return;

		_client.NotificationReceived -= OnNotification;
		_client.Resynced -= OnResynced;
		_client = null;
	}

	public void Dispose() => Detach();

	partial void OnSearchTextChanged(string value)
	{
		if (!_clearing)
			_ = StartSearch(_debounce);
	}

	/// <summary>Searches again with the current text and selection, without a delay.</summary>
	[RelayCommand]
	public Task RefreshAsync() => StartSearch(TimeSpan.Zero);

	/// <summary>Adds the chip's tag to the filter, or removes it when it is selected.</summary>
	[RelayCommand]
	private Task ToggleChipAsync(TagChipViewModel? chip)
	{
		if (chip == null)
			return Task.CompletedTask;

		var at = _selected.FindIndex(t => t.Key == chip.Key);
		if (at >= 0)
			_selected.RemoveAt(at);
		else
			_selected.Add(chip.ToRef());

		return StartSearch(TimeSpan.Zero);
	}

	/// <summary>Drops the whole selection and the text.</summary>
	[RelayCommand]
	private Task ClearAsync()
	{
		_selected.Clear();
		if (SearchText.Length > 0)
		{
			_clearing = true;   // no debounced search: the one below replaces it
			SearchText = string.Empty;
			_clearing = false;
		}

		return StartSearch(TimeSpan.Zero);
	}

	[RelayCommand]
	private void Open(SessionRowViewModel? row)
	{
		if (row != null)
			_actions.Open(row.Id, row.Name);
	}

	[RelayCommand]
	private async Task ToggleFavoriteAsync(SessionRowViewModel? row)
	{
		if (row == null)
			return;

		var value = await _actions.SetFavoriteAsync(row.Id, !row.IsFavorite);
		if (value != null)
			row.State = row.State with { Favorite = value.Value };
	}

	private Task StartSearch(TimeSpan delay)
	{
		_searchCts?.Cancel();
		var cts = new CancellationTokenSource();
		_searchCts = cts;
		_searchTask = SearchAsync(delay, cts);
		return _searchTask;
	}

	private async Task SearchAsync(TimeSpan delay, CancellationTokenSource cts)
	{
		var client = _client;
		if (client == null)
			return;

		try
		{
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cts.Token);

			var result = await client.TagsSearchAsync(SearchText, _selected.ToList(), null, cts.Token);
			if (!cts.IsCancellationRequested)
				Apply(result);
		}
		catch (OperationCanceledException)
		{
			// a newer search replaced this one
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "tags.search failed");
			Error = ex.Message;
		}
	}

	private void Apply(TagSearchResult result)
	{
		Error = null;
		Chips.Clear();
		foreach (var chip in result.Chips)
			Chips.Add(new TagChipViewModel(chip));

		// A selected tag the daemon did not echo as selected must not stay in the filter: the chips are the truth.
		_selected.RemoveAll(t => !result.Chips.Any(c => c.Selected && c.Key == t.Key));

		EmptyChipsText = result.EmptyReason switch
		{
			TagEmptyReason.NoTagsScoped or TagEmptyReason.NoTagsUnscoped => Constants.SessionLists.TagsNoTagsText,
			TagEmptyReason.NoMatchingTags => Constants.SessionLists.TagsNoMatchText,
			_ => null,
		};

		Groups.Clear();
		foreach (var group in result.Groups)
			Groups.Add(new TagGroupViewModel(group.DirectoryId, group.DirectoryLabel, group.Sessions.Select(s => WithCommands(SessionRowViewModel.From(s, group.DirectoryLabel)))));

		EmptySessionsText = result.SessionsEmptyReason switch
		{
			TagSessionsEmptyReason.SelectTags => Constants.SessionLists.TagsSelectTagsText,
			TagSessionsEmptyReason.NoSessions => Constants.SessionLists.TagsNoSessionsText,
			_ => null,
		};
	}

	private SessionRowViewModel WithCommands(SessionRowViewModel row)
	{
		row.OpenCommand = OpenCommand;
		row.ToggleFavoriteCommand = ToggleFavoriteCommand;
		return row;
	}

	private void OnResynced(object? sender, ResyncedEventArgs e) => _ui.Post(() => _ = StartSearch(TimeSpan.Zero));

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		switch (notification.Method)
		{
			case RemoteProtocol.Events.TagsChanged:
				_ui.Post(() => _ = StartSearch(TimeSpan.Zero));
				break;

			case RemoteProtocol.Events.SessionChanged:
				var payload = notification.Params.Deserialize<SessionChangedPayload>(TdcpJsonOptions.Default);
				if (payload != null)
					_ui.Post(() => OnSessionChanged(payload.State));
				break;
		}
	}

	private void OnSessionChanged(SessionStateDto state)
	{
		foreach (var row in Groups.SelectMany(g => g.Sessions).Where(r => r.Id == state.Id))
			row.State = state;
	}
}
