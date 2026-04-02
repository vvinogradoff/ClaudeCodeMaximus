using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;
using Serilog;

namespace ClaudeMaximus.ViewModels;

/// <summary>
/// ViewModel for the session import picker dialog.
/// Manages discovery, title generation, search, and selection.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportPickerViewModel : ViewModelBase
{
	private static readonly ILogger _log = Log.ForContext<ImportPickerViewModel>();

	private readonly IClaudeSessionImportService _importService;
	private readonly IClaudeAssistService _assistService;
	private readonly ITessynDaemonService? _daemonService;
	private string _searchText = string.Empty;
	private bool _isSearching;
	private bool _isGlobalSearch;
	private bool _isGeneratingTitles;
	private string _statusMessage = string.Empty;
	private string _titleProgressText = string.Empty;
	private double _titleProgressValue;
	private double _titleProgressMax = 1;
	private CancellationTokenSource? _titleCts;
	private ImportSessionItemViewModel? _selectedItem;
	private ImportTargetModel? _selectedSource;
	private ImportTargetModel? _selectedImportTarget;
	private IReadOnlySet<string> _alreadyImportedIds = new HashSet<string>();
	private Dictionary<int, TessynSessionModel>? _daemonSessionCache;

	/// <summary>All discovered session items (master list).</summary>
	private List<ImportSessionItemViewModel> _allItems = [];

	/// <summary>Currently visible items (may be filtered by search).</summary>
	public ObservableCollection<ImportSessionItemViewModel> Items { get; } = [];

	/// <summary>Preview entries for the currently focused (highlighted) item.</summary>
	public ObservableCollection<PreviewEntryViewModel> PreviewEntries { get; } = [];

	/// <summary>Source directories for session discovery (only directories, not groups).</summary>
	public ObservableCollection<ImportTargetModel> SourceDirectories { get; } = [];

	/// <summary>All possible import targets (directories + groups, with hierarchy).</summary>
	public ObservableCollection<ImportTargetModel> ImportTargets { get; } = [];

	/// <summary>Maximum number of user+assistant entries to show in preview.</summary>
	private const int PreviewEntryLimit = 8;

	public string SearchText
	{
		get => _searchText;
		set => this.RaiseAndSetIfChanged(ref _searchText, value);
	}

	public bool IsSearching
	{
		get => _isSearching;
		set => this.RaiseAndSetIfChanged(ref _isSearching, value);
	}

	/// <summary>When true, search across all projects via daemon FTS5 instead of local project only.</summary>
	public bool IsGlobalSearch
	{
		get => _isGlobalSearch;
		set => this.RaiseAndSetIfChanged(ref _isGlobalSearch, value);
	}

	/// <summary>Whether daemon-based global search is available.</summary>
	public bool CanUseGlobalSearch => _daemonService != null;

	public bool IsGeneratingTitles
	{
		get => _isGeneratingTitles;
		set => this.RaiseAndSetIfChanged(ref _isGeneratingTitles, value);
	}

	public string StatusMessage
	{
		get => _statusMessage;
		set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
	}

	public string TitleProgressText
	{
		get => _titleProgressText;
		set => this.RaiseAndSetIfChanged(ref _titleProgressText, value);
	}

	public double TitleProgressValue
	{
		get => _titleProgressValue;
		set => this.RaiseAndSetIfChanged(ref _titleProgressValue, value);
	}

	public double TitleProgressMax
	{
		get => _titleProgressMax;
		set => this.RaiseAndSetIfChanged(ref _titleProgressMax, value);
	}

	/// <summary>
	/// The currently focused/highlighted item in the list (distinct from checkbox selection).
	/// When set, loads a preview of the session's first exchanges.
	/// </summary>
	public ImportSessionItemViewModel? SelectedItem
	{
		get => _selectedItem;
		set
		{
			this.RaiseAndSetIfChanged(ref _selectedItem, value);
			LoadPreview(value);
		}
	}

	/// <summary>Source directory for session discovery. Changing this re-discovers sessions.</summary>
	public ImportTargetModel? SelectedSource
	{
		get => _selectedSource;
		set
		{
			if (this.RaiseAndSetIfChanged(ref _selectedSource, value) != null && value != null)
				RediscoverForDirectory(value.WorkingDirectory);
		}
	}

	/// <summary>Target location in the tree where imported sessions will be added.</summary>
	public ImportTargetModel? SelectedImportTarget
	{
		get => _selectedImportTarget;
		set
		{
			if (value?.IsNewDirectoryAction == true)
			{
				// Don't actually select the sentinel — request a new directory from the view
				NewDirectoryRequested?.Invoke(this, EventArgs.Empty);
				return;
			}
			this.RaiseAndSetIfChanged(ref _selectedImportTarget, value);
		}
	}

	/// <summary>Raised when user selects "New directory..." — view should show folder picker.</summary>
	public event EventHandler? NewDirectoryRequested;

	/// <summary>
	/// Called by the view after the user picks a folder. Adds the new directory to targets and selects it.
	/// </summary>
	public void AddNewDirectoryTarget(string path, string displayName)
	{
		var target = new ImportTargetModel
		{
			DisplayName = displayName,
			WorkingDirectory = path,
			Key = path,
			IsDirectory = true,
			Depth = 0,
		};

		// Insert before the "New directory..." sentinel
		var sentinelIndex = -1;
		for (var i = 0; i < ImportTargets.Count; i++)
		{
			if (ImportTargets[i].IsNewDirectoryAction)
			{
				sentinelIndex = i;
				break;
			}
		}

		if (sentinelIndex >= 0)
			ImportTargets.Insert(sentinelIndex, target);
		else
			ImportTargets.Add(target);

		SelectedImportTarget = target;
	}

	public bool HasMultipleSources => SourceDirectories.Count > 1;

	public bool HasPreview => PreviewEntries.Count > 0;

	public bool HasItems => Items.Count > 0;

	public bool HasNoItems => Items.Count == 0 && !IsSearching;

	/// <summary>Returns the selected (checked) items that are importable.</summary>
	public IReadOnlyList<ImportSessionItemViewModel> SelectedItems =>
		Items.Where(i => i.IsSelected).ToList();

	public bool HasSelection => Items.Any(i => i.IsSelected);

	public ImportPickerViewModel(
		IClaudeSessionImportService importService,
		IClaudeAssistService assistService,
		ITessynDaemonService? daemonService = null)
	{
		_importService = importService;
		_assistService = assistService;
		_daemonService = daemonService;
	}

	/// <summary>
	/// Sets up source directories, import targets, and discovers sessions.
	/// </summary>
	public void Initialize(
		IReadOnlyList<ImportTargetModel> sourceDirectories,
		IReadOnlyList<ImportTargetModel> importTargets,
		string? initialWorkingDirectory,
		string? initialTargetKey,
		IReadOnlySet<string> alreadyImportedIds)
	{
		_alreadyImportedIds = alreadyImportedIds;

		SourceDirectories.Clear();
		foreach (var src in sourceDirectories)
			SourceDirectories.Add(src);
		this.RaisePropertyChanged(nameof(HasMultipleSources));

		ImportTargets.Clear();
		foreach (var tgt in importTargets)
			ImportTargets.Add(tgt);

		// Add "New directory..." action at the end
		ImportTargets.Add(new ImportTargetModel
		{
			DisplayName = "+ New directory...",
			WorkingDirectory = string.Empty,
			Key = "__new_directory__",
			IsDirectory = true,
			IsNewDirectoryAction = true,
		});

		// Select initial source (without triggering rediscovery)
		_selectedSource = sourceDirectories.FirstOrDefault(d =>
			string.Equals(d.WorkingDirectory, initialWorkingDirectory, StringComparison.OrdinalIgnoreCase))
			?? sourceDirectories.FirstOrDefault();
		this.RaisePropertyChanged(nameof(SelectedSource));

		// Select initial import target
		_selectedImportTarget = initialTargetKey != null
			? importTargets.FirstOrDefault(t => t.Key == initialTargetKey)
			: importTargets.FirstOrDefault(t =>
				string.Equals(t.WorkingDirectory, initialWorkingDirectory, StringComparison.OrdinalIgnoreCase));
		_selectedImportTarget ??= importTargets.FirstOrDefault();
		this.RaisePropertyChanged(nameof(SelectedImportTarget));

		if (_selectedSource != null)
			PopulateFromDirectory(_selectedSource.WorkingDirectory);
	}

	/// <summary>
	/// Legacy entry point: discovers for a single directory without directory selector.
	/// </summary>
	public void DiscoverSessions(string workingDirectory, IReadOnlySet<string> alreadyImportedIds)
	{
		_alreadyImportedIds = alreadyImportedIds;
		PopulateFromDirectory(workingDirectory);
	}

	private void RediscoverForDirectory(string workingDirectory)
	{
		CancelTitleGeneration();
		PreviewEntries.Clear();
		this.RaisePropertyChanged(nameof(HasPreview));
		PopulateFromDirectory(workingDirectory);
	}

	private void PopulateFromDirectory(string workingDirectory)
	{
		var summaries = _importService.DiscoverSessions(workingDirectory);

		_allItems = summaries.Select(s =>
			new ImportSessionItemViewModel(s, _alreadyImportedIds.Contains(s.SessionId))
		).ToList();

		Items.Clear();
		foreach (var item in _allItems)
			Items.Add(item);

		this.RaisePropertyChanged(nameof(HasItems));
		this.RaisePropertyChanged(nameof(HasNoItems));

		if (_allItems.Count == 0)
			StatusMessage = "No Claude Code sessions found for this project directory.";
		else
			StatusMessage = $"Found {_allItems.Count} sessions.";

		// Start async title generation (only for sessions without cached titles)
		_ = GenerateTitlesAsync();
	}

	/// <summary>
	/// Performs a search: tries Claude-powered semantic search first,
	/// falls back to local substring matching.
	/// </summary>
	public async Task SearchAsync()
	{
		var query = SearchText?.Trim();

		if (string.IsNullOrEmpty(query))
		{
			// Clear search — show all items in original order
			Items.Clear();
			foreach (var item in _allItems)
				Items.Add(item);
			StatusMessage = $"Found {_allItems.Count} sessions.";
			this.RaisePropertyChanged(nameof(HasItems));
			this.RaisePropertyChanged(nameof(HasNoItems));
			return;
		}

		IsSearching = true;
		StatusMessage = "Searching...";

		try
		{
			if (_isGlobalSearch && _daemonService != null)
			{
				await SearchViaDaemonAsync(query);
			}
			else
			{
				var summaries = _allItems.Select(i => i.Summary).ToList();
				var matchedIds = await _assistService.SearchSessionsAsync(summaries, query);

				if (matchedIds.Count > 0)
				{
					ReorderByIds(matchedIds);
					StatusMessage = $"Found {matchedIds.Count} matching sessions.";
				}
				else
				{
					FallbackSubstringSearch(query);
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "SearchAsync: search failed, using fallback");
			FallbackSubstringSearch(query);
		}
		finally
		{
			IsSearching = false;
			this.RaisePropertyChanged(nameof(HasItems));
			this.RaisePropertyChanged(nameof(HasNoItems));
		}
	}

	private async Task SearchViaDaemonAsync(string query)
	{
		var response = await _daemonService!.SearchAsync(query, limit: 50);
		if (response.Results.Count == 0)
		{
			StatusMessage = "No results found across all projects.";
			Items.Clear();
			return;
		}

		// Collect unique session IDs from search results
		var hitSessionIds = response.Results.Select(r => r.SessionId).Distinct().ToList();

		// Resolve to full session models (cached to avoid per-keystroke fetches)
		if (_daemonSessionCache == null)
		{
			var allSessions = await _daemonService.SessionsListAsync(limit: 10000);
			_daemonSessionCache = allSessions.ToDictionary(s => s.Id);
		}
		var sessionMap = _daemonSessionCache;

		Items.Clear();
		var count = 0;
		foreach (var sid in hitSessionIds)
		{
			if (!sessionMap.TryGetValue(sid, out var session)) continue;

			// Check if already in _allItems (local discovery)
			var existing = _allItems.FirstOrDefault(i => i.Summary.SessionId == session.ExternalId);
			if (existing != null)
			{
				Items.Add(existing);
			}
			else
			{
				// Create a virtual item from daemon data (cross-project result)
				var summary = new ClaudeSessionSummaryModel
				{
					SessionId       = session.ExternalId,
					JsonlPath       = string.Empty,
					Created         = DateTimeOffset.FromUnixTimeMilliseconds(session.CreatedAt),
					LastUsed        = DateTimeOffset.FromUnixTimeMilliseconds(session.UpdatedAt),
					MessageCount    = session.MessageCount,
					FirstUserPrompt = session.FirstPrompt,
					GeneratedTitle  = session.Title,
				};
				var isImported = _alreadyImportedIds.Contains(session.ExternalId);
				var item = new ImportSessionItemViewModel(summary, isImported);
				Items.Add(item);
			}
			count++;
		}

		StatusMessage = $"Found {count} sessions across all projects.";
	}

	/// <summary>
	/// Cancels any running title generation.
	/// </summary>
	public void CancelTitleGeneration()
	{
		_titleCts?.Cancel();
	}

	private async Task GenerateTitlesAsync()
	{
		_titleCts?.Cancel();
		_titleCts = new CancellationTokenSource();
		var ct = _titleCts.Token;

		// Only generate titles for sessions that don't already have one (from cache or prior generation)
		var pendingItems = _allItems
			.Where(i => i.Summary.FirstUserPrompt != null && i.Summary.GeneratedTitle == null)
			.ToList();
		var summaries = pendingItems.Select(i => i.Summary).ToList();

		if (summaries.Count == 0)
			return;

		IsGeneratingTitles = true;
		TitleProgressMax = summaries.Count;
		TitleProgressValue = 0;
		TitleProgressText = $"Generating titles... 0/{summaries.Count}";

		try
		{
			await _assistService.GenerateTitlesAsync(summaries, OnBatchComplete, ct);

			if (ct.IsCancellationRequested)
				return;

			// Mark any remaining items as no longer pending (title generation finished or failed)
			foreach (var item in pendingItems)
			{
				if (item.IsTitlePending)
					item.IsTitlePending = false;
			}
		}
		catch (OperationCanceledException)
		{
			// Expected when cancelled
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "GenerateTitlesAsync: title generation failed");
		}
		finally
		{
			IsGeneratingTitles = false;
			TitleProgressText = string.Empty;
		}
	}

	private void OnBatchComplete(Dictionary<string, string> allTitlesSoFar)
	{
		// Cache all titles for future dialog opens
		foreach (var (sessionId, title) in allTitlesSoFar)
			_importService.CacheTitle(sessionId, title);

		Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			// Always apply titles unconditionally — don't check IsTitlePending here
			// because the await continuation may clear it before this Post runs
			foreach (var (sessionId, title) in allTitlesSoFar)
			{
				var item = _allItems.FirstOrDefault(i => i.SessionId == sessionId);
				item?.UpdateTitle(title);
			}

			TitleProgressValue = allTitlesSoFar.Count;
			var total = (int)TitleProgressMax;
			TitleProgressText = $"Generating titles... {allTitlesSoFar.Count}/{total}";
		});
	}

	private void ReorderByIds(List<string> orderedIds)
	{
		// Build a lookup of local items by session ID for validation
		var itemLookup = new Dictionary<string, ImportSessionItemViewModel>();
		foreach (var item in _allItems)
			itemLookup[item.SessionId] = item;

		var matched = new List<ImportSessionItemViewModel>();
		var matchedSet = new HashSet<string>();

		// Add matched items in relevance order, skipping duplicates and unknown IDs
		foreach (var id in orderedIds)
		{
			if (matchedSet.Contains(id))
				continue;
			if (!itemLookup.TryGetValue(id, out var item))
				continue;
			matched.Add(item);
			matchedSet.Add(id);
		}

		// If no valid matches found, fall back (don't show a misleading "found N" message)
		if (matched.Count == 0)
		{
			StatusMessage = "No matching sessions found (search returned no valid results).";
			return;
		}

		// Add remaining items after matched
		Items.Clear();
		foreach (var item in matched)
			Items.Add(item);
		foreach (var item in _allItems)
		{
			if (!matchedSet.Contains(item.SessionId))
				Items.Add(item);
		}
	}

	private void FallbackSubstringSearch(string query)
	{
		var lowerQuery = query.ToLowerInvariant();
		var matched = new List<ImportSessionItemViewModel>();
		var unmatched = new List<ImportSessionItemViewModel>();

		foreach (var item in _allItems)
		{
			var title = (item.DisplayTitle ?? string.Empty).ToLowerInvariant();
			var prompt = (item.Summary.FirstUserPrompt ?? string.Empty).ToLowerInvariant();

			if (title.Contains(lowerQuery) || prompt.Contains(lowerQuery))
				matched.Add(item);
			else
				unmatched.Add(item);
		}

		Items.Clear();
		foreach (var item in matched)
			Items.Add(item);
		foreach (var item in unmatched)
			Items.Add(item);

		StatusMessage = matched.Count > 0
			? $"Found {matched.Count} matching sessions (local search)."
			: "No matching sessions found.";
	}

	private void LoadPreview(ImportSessionItemViewModel? item)
	{
		PreviewEntries.Clear();

		if (item == null)
		{
			this.RaisePropertyChanged(nameof(HasPreview));
			return;
		}

		try
		{
			// For daemon-sourced virtual items (cross-project search), load preview from daemon
			if (string.IsNullOrEmpty(item.Summary.JsonlPath))
			{
				if (_daemonService != null)
					_ = LoadPreviewFromDaemonAsync(item.Summary.SessionId);
				else
					PreviewEntries.Add(new PreviewEntryViewModel("SYSTEM",
						"Preview not available without daemon connection."));
				this.RaisePropertyChanged(nameof(HasPreview));
				return;
			}

			var entries = _importService.ParseJsonlSession(item.Summary.JsonlPath);

			// Collect the last N user/assistant entries (most recent = most memorable)
			var conversationEntries = new List<(string Role, string Content)>();
			foreach (var entry in entries)
			{
				if (entry.Role is not (Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant))
					continue;
				conversationEntries.Add((entry.Role, entry.Content));
			}

			// Take the tail
			var startIndex = Math.Max(0, conversationEntries.Count - PreviewEntryLimit);
			for (var i = startIndex; i < conversationEntries.Count; i++)
			{
				var content = conversationEntries[i].Content;
				if (content.Length > 300)
					content = content[..297] + "...";
				PreviewEntries.Add(new PreviewEntryViewModel(conversationEntries[i].Role, content));
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "LoadPreview: failed to parse {Path}", item.Summary.JsonlPath);
		}

		this.RaisePropertyChanged(nameof(HasPreview));
	}

	private async Task LoadPreviewFromDaemonAsync(string externalId)
	{
		try
		{
			var result = await _daemonService!.SessionsGetAsync(externalId, limit: PreviewEntryLimit * 2);
			var conversationEntries = result.Messages
				.Where(m => m.Role is "user" or "assistant")
				.Select(m => (Role: m.Role.ToUpperInvariant(), m.Content))
				.ToList();

			var startIndex = Math.Max(0, conversationEntries.Count - PreviewEntryLimit);
			for (var i = startIndex; i < conversationEntries.Count; i++)
			{
				var content = conversationEntries[i].Content;
				if (content.Length > 300)
					content = content[..297] + "...";

				PreviewEntries.Add(new PreviewEntryViewModel(conversationEntries[i].Role, content));
			}

			if (PreviewEntries.Count == 0)
				PreviewEntries.Add(new PreviewEntryViewModel("SYSTEM", "(no conversation content)"));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to load daemon preview for {ExternalId}", externalId);
			PreviewEntries.Add(new PreviewEntryViewModel("SYSTEM", "Could not load preview."));
		}

		this.RaisePropertyChanged(nameof(HasPreview));
	}
}
