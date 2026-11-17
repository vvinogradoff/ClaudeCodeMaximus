using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The Tags tab (FR.1.15, FR.24.11-24.14): a search box over the tags, the chips it finds (selected ones first), and the sessions carrying
/// any selected tag (OR filter) grouped by directory. Everything is the daemon's <c>tags.search</c>; the client sends the text, the selected
/// tags and, while the project scope is on, the selected session (the daemon scopes to its directory; a worker scopes like its supervisor).
/// The scope is on by default while a session (or worker) is selected and off without one, also while a directory or group is selected (as in the Avalonia client). Chips and rows are updated in place. Refreshes on
/// <c>tags.changed</c>; a favourite or running flip arrives as <c>session.changed</c> and updates the rows in place. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TagSearchViewModel : ObservableObject, IDisposable
{
	private readonly IUiDispatcher _ui;
	private readonly ISessionListActions _actions;
	private readonly ILogger<TagSearchViewModel> _logger;
	private readonly TimeSpan _debounce;
	private readonly IUrlLauncher? _launcher;
	private readonly List<TagRefDto> _selected = [];
	private readonly Dictionary<string, TagChipViewModel> _chipsByKey = new(StringComparer.Ordinal);
	private readonly ListDiff _diff = new();
	private ITessynClient? _client;
	private TreeViewModel? _tree;
	private CancellationTokenSource? _searchCts;
	private Task _searchTask = Task.CompletedTask;
	private bool _clearing;
	private string? _scopeSessionId;
	private string? _scopeDirectoryId;

	[ObservableProperty]
	private string _searchText = string.Empty;

	[ObservableProperty]
	private string? _emptyChipsText;

	[ObservableProperty]
	private string? _emptySessionsText;

	[ObservableProperty]
	private string? _error;

	/// <summary>The user's project-scope toggle (FR.24.11); on by default.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsScoped))]
	private bool _isScopeEnabled = true;

	public TagSearchViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<TagSearchViewModel> logger)
		: this(actions, ui, logger, Constants.Timeouts.SearchDebounce)
	{
	}

	public TagSearchViewModel(ISessionListActions actions, IUiDispatcher ui, ILogger<TagSearchViewModel> logger, TimeSpan debounce, IUrlLauncher? launcher = null)
	{
		_actions = actions;
		_ui = ui;
		_logger = logger;
		_debounce = debounce;
		_launcher = launcher;
		OpenCommand = new RelayCommand<SessionRowViewModel>(row =>
		{
			if (row != null)
				_actions.Open(row.Id, row.Name);
		});
		ToggleFavoriteCommand = new AsyncRelayCommand<SessionRowViewModel>(ToggleFavoriteRowAsync);
		Sessions = new GroupedSessionListViewModel(OpenCommand, ToggleFavoriteCommand);
	}

	/// <summary>Selected chips first, then the other matches.</summary>
	public ObservableCollection<TagChipViewModel> Chips { get; } = [];

	/// <summary>The sessions carrying any selected tag, per directory (virtualised, collapsible).</summary>
	public GroupedSessionListViewModel Sessions { get; }

	public System.Windows.Input.ICommand OpenCommand { get; }

	public System.Windows.Input.ICommand ToggleFavoriteCommand { get; }

	/// <summary>The tags the filter is made of.</summary>
	public IReadOnlyList<TagRefDto> SelectedTags => _selected;

	/// <summary>The search is limited to the selected session's directory.</summary>
	public bool IsScoped => IsScopeEnabled && _scopeSessionId != null;

	/// <summary>A session or worker is selected, so the scope toggle has an effect; false forces the scope off.</summary>
	public bool CanScope => _scopeSessionId != null;

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

	/// <summary>Follows the tree selection for the project scope; replaces a previous tree.</summary>
	public void AttachTree(TreeViewModel tree)
	{
		if (_tree != null)
			_tree.SelectionChanged -= OnSelectionChanged;

		_tree = tree;
		tree.SelectionChanged += OnSelectionChanged;
		UpdateScope(search: false);
		if (_client != null)
			_ = StartSearch(TimeSpan.Zero);
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

	public void Dispose()
	{
		Detach();
		if (_tree != null)
			_tree.SelectionChanged -= OnSelectionChanged;
	}

	private void OnSelectionChanged(object? sender, TreeNodeViewModel node) => UpdateScope(search: true);

	/// <summary>Reads the scope from the tree selection; searches again when the scoped directory (or whether there is one) changed.</summary>
	private void UpdateScope(bool search)
	{
		var sessionId = _tree?.ScopeSessionIdOfSelection();
		var directoryId = sessionId == null ? null : _tree?.DirectoryIdOf(sessionId);
		var directoryChanged = directoryId != _scopeDirectoryId;
		var hadScope = CanScope;
		_scopeSessionId = sessionId;
		_scopeDirectoryId = directoryId;
		if (hadScope != CanScope)
		{
			OnPropertyChanged(nameof(CanScope));
			OnPropertyChanged(nameof(IsScoped));
		}

		if (search && directoryChanged && IsScopeEnabled && _client != null)
			_ = StartSearch(TimeSpan.Zero);
	}

	partial void OnSearchTextChanged(string value)
	{
		if (!_clearing)
			_ = StartSearch(_debounce);
	}

	partial void OnIsScopeEnabledChanged(bool value)
	{
		if (_scopeSessionId != null && _client != null)
			_ = StartSearch(TimeSpan.Zero);
	}

	/// <summary>Searches again with the current text, selection and scope, without a delay.</summary>
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

	/// <summary>Opens the chip's link in the system browser (FR.24.12); nothing when the tag has no link.</summary>
	[RelayCommand]
	private void OpenChip(TagChipViewModel? chip)
	{
		if (chip is { HasUrl: true })
			_launcher?.TryOpen(chip.Url);
	}

	/// <summary>Switches the project scope off: the tags of every directory are searched (the "show all" link under the search box while the scope is on).</summary>
	[RelayCommand]
	private void ShowAllDirectories() => IsScopeEnabled = false;
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

	private async Task ToggleFavoriteRowAsync(SessionRowViewModel? row)
	{
		if (row == null)
			return;

		var value = await _actions.SetFavoriteAsync(row.Id, !row.IsFavorite);
		if (value != null)
			row.State = row.State with { Favorite = value.Value };
	}

	/// <summary>
	/// What the daemon is asked for. A tag matches when every word of the query occurs in its label or key, so a wildcard has nothing to match: <c>HSE-*</c> would find no tag.
	/// A star or a question mark is therefore a word separator (<c>HSE-*</c> is <c>HSE-</c>, <c>HSE*240</c> is <c>HSE 240</c>).
	/// </summary>
	public static string NormalizeQuery(string text) => text.Replace('*', ' ').Replace('?', ' ').Trim();

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

			var scoped = IsScoped;
			var result = await client.TagsSearchAsync(NormalizeQuery(SearchText), _selected.ToList(), scoped ? _scopeSessionId : null, cts.Token);
			if (!cts.IsCancellationRequested)
				Apply(result, scoped);
		}
		catch (OperationCanceledException)
		{
			// a newer search replaced this one
		}
		catch (Exception ex) when (!cts.IsCancellationRequested)
		{
			_logger.LogWarning(ex, "tags.search failed");
			Error = ex.Message;
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "A superseded tags.search failed");   // a newer search runs: its answer is the one to show
		}
	}

	private void Apply(TagSearchResult result, bool scoped)
	{
		Error = null;
		var wanted = new List<TagChipViewModel>(result.Chips.Count);
		foreach (var dto in result.Chips)
		{
			if (_chipsByKey.TryGetValue(dto.Key, out var chip))
				chip.Update(dto);
			else
				_chipsByKey[dto.Key] = chip = new TagChipViewModel(dto);

			wanted.Add(chip);
		}

		foreach (var gone in _chipsByKey.Keys.Where(k => result.Chips.All(c => c.Key != k)).ToList())
			_chipsByKey.Remove(gone);

		_diff.Apply(Chips, wanted);

		// A selected tag the daemon did not echo as selected must not stay in the filter: the chips are the truth.
		_selected.RemoveAll(t => !result.Chips.Any(c => c.Selected && c.Key == t.Key));

		EmptyChipsText = result.EmptyReason switch
		{
			TagEmptyReason.NoTagsScoped => scoped ? Constants.SessionLists.TagsNoTagsScopedText : Constants.SessionLists.TagsNoTagsText,
			TagEmptyReason.NoTagsUnscoped => Constants.SessionLists.TagsNoTagsText,
			TagEmptyReason.NoMatchingTags => scoped ? Constants.SessionLists.TagsNoMatchScopedText : Constants.SessionLists.TagsNoMatchText,
			_ => null,
		};

		Sessions.Show(result.Groups
			.Select(g => new SessionGroupData(g.DirectoryId, g.DirectoryLabel, g.Sessions.Select(SessionRowViewModel.From).ToList()))
			.ToList());

		EmptySessionsText = result.SessionsEmptyReason switch
		{
			TagSessionsEmptyReason.SelectTags => Constants.SessionLists.TagsSelectTagsText,
			TagSessionsEmptyReason.NoSessions => Constants.SessionLists.TagsNoSessionsText,
			_ => null,
		};
	}

	/// <summary>Re-reads the date text of the rows (the date format setting changed).</summary>
	public void RefreshDisplay()
	{
		foreach (var row in Sessions.Items.OfType<SessionRowViewModel>())
			row.RefreshDisplay();
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
		foreach (var row in Sessions.Items.OfType<SessionRowViewModel>().Where(r => r.Id == state.Id))
			row.State = state;
	}
}
