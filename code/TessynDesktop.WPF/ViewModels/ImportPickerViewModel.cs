using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The import picker (W7, FR.13): lists the Claude sessions of a directory (<c>import.list</c>) or, with Global on, those of every project that match a search
/// (<c>search.global</c>, started with Enter); titles that do not exist yet arrive progressively (<c>import.titles</c>, asked again while some are pending); the ticked
/// sessions are imported into the chosen directory or group (<c>import.run</c>). An empty or already imported session is greyed and cannot be ticked. Contains no
/// WPF types; the window and the host that opens it are separate.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ImportPickerViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly ILogger _logger;
	private readonly TimeSpan _titlePollInterval;
	private readonly int _maxTitlePolls;
	private readonly int _pageSize;
	private CancellationTokenSource _titles = new();
	private int _loadToken;
	private bool _recountSuspended;
	private bool _targetChosenByUser;
	private string _query = string.Empty;

	[ObservableProperty]
	private ImportTargetViewModel? _selectedSource;

	[ObservableProperty]
	private ImportTargetViewModel? _selectedTarget;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowSourcePicker))]
	private bool _isGlobal;

	[ObservableProperty]
	private string _searchText = string.Empty;

	/// <summary>A list, search or import is running: the spinner.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(SearchCommand), nameof(LoadMoreCommand))]
	private bool _isBusy;

	[ObservableProperty]
	private string _statusText = string.Empty;

	/// <summary>Titles are still being generated; <see cref="TitleProgressText"/> says how many are left.</summary>
	[ObservableProperty]
	private bool _isGeneratingTitles;

	[ObservableProperty]
	private string _titleProgressText = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
	private int _total;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ImportCommand))]
	private int _selectedCount;

	public ImportPickerViewModel(ITessynClient client, ILogger logger, TimeSpan? titlePollInterval = null, int maxTitlePolls = Constants.Import.MaxTitlePolls, int pageSize = Constants.Import.PageSize)
	{
		_client = client;
		_logger = logger;
		_titlePollInterval = titlePollInterval ?? Constants.Import.TitlePollInterval;
		_maxTitlePolls = maxTitlePolls;
		_pageSize = pageSize;
	}

	/// <summary>Every directory and group of the tree, in tree order.</summary>
	public ObservableCollection<ImportTargetViewModel> Targets { get; } = [];

	/// <summary>The directories: where the Claude sessions of the list live.</summary>
	public ObservableCollection<ImportTargetViewModel> Sources { get; } = [];

	public ObservableCollection<ImportItemViewModel> Items { get; } = [];

	/// <summary>The directory list is not used while the search looks in every project.</summary>
	public bool ShowSourcePicker => !IsGlobal;

	public bool HasItems => Items.Count > 0;

	public bool CanLoadMoreItems => Total > Items.Count;

	/// <summary>Raised after an import: the node ids of the sessions that were added (the host shows the first in the tree).</summary>
	public event EventHandler<IReadOnlyList<string>>? Imported;

	/// <summary>Raised when the picker has nothing more to do (everything imported, or Close).</summary>
	public event EventHandler? CloseRequested;

	/// <summary>
	/// Fills the choices and lists the sessions of the source. <paramref name="targetId"/> is the directory or group the picker was opened on (null: the first
	/// directory); the source is that node's directory.
	/// </summary>
	public async Task InitializeAsync(IReadOnlyList<ImportTargetViewModel> targets, string? targetId)
	{
		Targets.Clear();
		Sources.Clear();
		foreach (var target in targets)
		{
			Targets.Add(target);
			if (target.IsDirectory)
				Sources.Add(target);
		}

		var initial = targets.FirstOrDefault(t => t.Id == targetId) ?? targets.FirstOrDefault();
		_targetChosenByUser = false;
		_selectedTarget = initial;
		OnPropertyChanged(nameof(SelectedTarget));
		_selectedSource = Sources.FirstOrDefault(s => s.Id == initial?.DirectoryId) ?? Sources.FirstOrDefault();
		OnPropertyChanged(nameof(SelectedSource));
		await LoadDirectoryAsync();
	}

	// Only the user (the combo box) sets the property; the picker itself sets the field, so a change here is a choice.
	partial void OnSelectedTargetChanged(ImportTargetViewModel? value)
	{
		_targetChosenByUser = true;
		ImportCommand.NotifyCanExecuteChanged();
	}

	partial void OnSelectedSourceChanged(ImportTargetViewModel? value)
	{
		// Importing from a directory goes into that directory unless the user picked another place on purpose.
		if (!_targetChosenByUser && value != null && Targets.FirstOrDefault(t => t.Id == value.Id) is { } target)
			_selectedTarget = target;
		OnPropertyChanged(nameof(SelectedTarget));
		if (!IsGlobal)
			_ = LoadDirectoryAsync();
	}

	partial void OnIsGlobalChanged(bool value)
	{
		Cancel();
		if (value)
		{
			ReplaceItems([], 0);
			StatusText = Constants.Import.SearchHint;
			if (!string.IsNullOrWhiteSpace(SearchText))
				_ = SearchAsync();
		}
		else
		{
			_ = LoadDirectoryAsync();
		}
	}

	/// <summary>Enter in the search box: filters the directory's sessions, or with Global on searches every project.</summary>
	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task SearchAsync()
	{
		_query = SearchText.Trim();
		if (IsGlobal)
			await LoadGlobalAsync(offset: 0);
		else
			await LoadDirectoryAsync();
	}

	[RelayCommand(CanExecute = nameof(CanLoadMore))]
	private async Task LoadMoreAsync()
	{
		if (IsGlobal)
			await LoadGlobalAsync(Items.Count);
		else
			await LoadDirectoryAsync(Items.Count);
	}

	private bool CanRun() => !IsBusy;

	private bool CanLoadMore() => !IsBusy && Total > Items.Count;

	/// <summary>Ticks every session that can be imported.</summary>
	[RelayCommand]
	private void SelectAll()
	{
		// One recount at the end, not one per ticked row (each recount walks the whole list).
		_recountSuspended = true;
		try
		{
			foreach (var item in Items.Where(i => i.IsSelectable))
				item.IsSelected = true;
		}
		finally
		{
			_recountSuspended = false;
		}

		RecountSelection();
	}

	/// <summary>Called by the view when a row's tick changes.</summary>
	public void RecountSelection() => SelectedCount = Items.Count(i => i.IsSelected && i.IsSelectable);

	[RelayCommand]
	private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

	private bool CanImport() => !IsBusy && SelectedCount > 0 && SelectedTarget != null;

	[RelayCommand(CanExecute = nameof(CanImport))]
	private async Task ImportAsync()
	{
		var target = SelectedTarget;
		var chosen = Items.Where(i => i.IsSelected && i.IsSelectable).ToList();
		if (target == null || chosen.Count == 0)
			return;

		IsBusy = true;
		try
		{
			var result = await _client.ImportRunAsync(target.Id, [.. chosen.Select(i => new ImportRequestDto(i.ExternalId))]);
			var done = result.Imported.Select(i => i.ExternalId).ToHashSet(StringComparer.Ordinal);
			foreach (var item in chosen.Where(i => done.Contains(i.ExternalId)))
			{
				item.AlreadyImported = true;
				item.IsSelected = false;
			}

			RecountSelection();
			var failed = result.Failed.Select(f => f.Reason).Distinct().ToList();
			StatusText = string.Format(Constants.Import.ImportedFormat, result.Imported.Count == 1 ? "1 session" : result.Imported.Count + " sessions")
				+ (failed.Count > 0 ? " " + string.Format(Constants.Import.FailedFormat, result.Failed.Count, string.Join("; ", failed)) : string.Empty);
			if (result.Imported.Count > 0)
				Imported?.Invoke(this, [.. result.Imported.Select(i => i.NodeId)]);

			if (result.Failed.Count == 0 && result.Imported.Count > 0)
				CloseRequested?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "import.run failed for {Target}", target.Id);
			StatusText = Constants.Import.ImportFailed + ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task LoadDirectoryAsync(int offset = 0)
	{
		var source = SelectedSource;
		if (source == null)
		{
			ReplaceItems([], 0);
			StatusText = Constants.Import.NothingFound;
			return;
		}

		var token = Restart(offset == 0);
		IsBusy = true;
		StatusText = Constants.Import.Loading;
		try
		{
			var query = _query.Length > 0 ? _query : null;
			var result = await _client.ImportListAsync(source.Id, false, query, _pageSize, offset);
			if (token != _loadToken)
				return;

			Show(result.Items.Select(c => new ImportItemViewModel(c)), result.Total, offset);
			_ = LoadTitlesAsync(token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (token != _loadToken)
				return;

			_logger.LogWarning(ex, "import.list failed for {Directory}", source.Id);
			StatusText = Constants.Import.ListFailed + ex.Message;
		}
		finally
		{
			if (token == _loadToken)
				IsBusy = false;
		}
	}

	private async Task LoadGlobalAsync(int offset)
	{
		if (_query.Length == 0)
		{
			Restart(true);
			ReplaceItems([], 0);
			StatusText = Constants.Import.SearchHint;
			return;
		}

		var token = Restart(offset == 0);
		IsBusy = true;
		StatusText = Constants.Import.Loading;
		try
		{
			var result = await _client.SearchGlobalAsync(_query, null, _pageSize, offset);
			if (token != _loadToken)
				return;

			Show(result.Hits.Select(h => new ImportItemViewModel(h)), result.Total, offset);
			_ = LoadTitlesAsync(token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (token != _loadToken)
				return;

			_logger.LogWarning(ex, "search.global failed");
			StatusText = Constants.Import.SearchFailed + ex.Message;
		}
		finally
		{
			if (token == _loadToken)
				IsBusy = false;
		}
	}

	/// <summary>Starts a new load: older answers and the title polling of the list before are dropped. Returns its token.</summary>
	private int Restart(bool replace)
	{
		if (replace)
			Cancel();
		else
			RestartTitles();   // Load more: the title polling of the list so far is stopped now; LoadTitlesAsync of the new load starts one polling for all untitled rows

		return Interlocked.Increment(ref _loadToken);
	}

	private void RestartTitles()
	{
		_titles.Cancel();
		_titles.Dispose();
		_titles = new CancellationTokenSource();
	}

	private void Cancel()
	{
		RestartTitles();
		Interlocked.Increment(ref _loadToken);
		IsBusy = false;
		IsGeneratingTitles = false;
		TitleProgressText = string.Empty;
	}

	private void Show(IEnumerable<ImportItemViewModel> items, int total, int offset)
	{
		var list = items.ToList();
		foreach (var item in list)
			item.PropertyChanged += OnItemChanged;

		if (offset == 0)
			ReplaceItems(list, total);
		else
		{
			foreach (var item in list)
				Items.Add(item);
			Total = total;
			OnListChanged();
		}

		StatusText = Items.Count == 0 ? Constants.Import.NothingFound : string.Format(Constants.Import.FoundFormat, total, Items.Count);
	}

	private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ImportItemViewModel.IsSelected) && !_recountSuspended)
			RecountSelection();
	}

	private void ReplaceItems(IReadOnlyList<ImportItemViewModel> items, int total)
	{
		foreach (var old in Items)
			old.PropertyChanged -= OnItemChanged;

		Items.Clear();
		foreach (var item in items)
			Items.Add(item);

		Total = total;
		RecountSelection();
		OnListChanged();
	}

	private void OnListChanged()
	{
		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(CanLoadMoreItems));
		LoadMoreCommand.NotifyCanExecuteChanged();
	}

	/// <summary>
	/// Asks <c>import.titles</c> for the sessions that have none and again while some are pending, so the rows get their titles one after another. Gives up after
	/// a bounded number of rounds (a session that cannot get a title stays pending for the daemon too).
	/// </summary>
	private async Task LoadTitlesAsync(int token)
	{
		var cancel = _titles.Token;
		var waiting = Items.Where(i => !i.HasTitle).ToList();
		if (waiting.Count == 0)
			return;

		try
		{
			for (var round = 0; round < _maxTitlePolls && waiting.Count > 0; round++)
			{
				var pending = 0;
				foreach (var batch in waiting.Chunk(Constants.Import.TitleBatch))
				{
					var result = await _client.ImportTitlesAsync([.. batch.Select(i => i.ExternalId)], cancel);
					if (token != _loadToken || cancel.IsCancellationRequested)
						return;

					pending += result.Pending;
					foreach (var title in result.Titles)
					{
						var row = batch.FirstOrDefault(i => i.ExternalId == title.ExternalId);
						if (row != null && !string.IsNullOrWhiteSpace(title.Title))
							row.Title = title.Title;
					}
				}

				waiting = [.. waiting.Where(i => !i.HasTitle)];
				if (pending == 0 || waiting.Count == 0)
					break;

				IsGeneratingTitles = true;
				TitleProgressText = string.Format(Constants.Import.TitlesLeftFormat, pending);
				await Task.Delay(_titlePollInterval, cancel);
			}
		}
		catch (OperationCanceledException)
		{
			// A new list or a closed window ended the polling.
		}
		catch (Exception ex)
		{
			// A title is a nicety: the row keeps its first prompt.
			_logger.LogWarning(ex, "import.titles failed");
		}
		finally
		{
			if (token == _loadToken)
			{
				IsGeneratingTitles = false;
				TitleProgressText = string.Empty;
			}
		}
	}

	public void Dispose()
	{
		_titles.Cancel();
		_titles.Dispose();
	}
}
