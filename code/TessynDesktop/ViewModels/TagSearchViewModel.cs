using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>
/// The Tags tab (FR.24.11–FR.24.13): searches the Session Tag Index, shows matching tags as toggle-selectable
/// chips, and lists the top-level sessions carrying at least one selected tag (OR) in Recent-style groups.
/// The index is loaded off the UI thread; text filtering runs in memory.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagSearchViewModel : ViewModelBase, ISessionGroupListHost
{
	private static readonly ILogger _log = Log.ForContext<TagSearchViewModel>();

	private const string EmptyTagsScoped = "No tags in this directory";
	private const string EmptyTagsUnscoped = "No tags yet";
	private const string EmptyTagsNoMatch = "No matching tags";
	private const string SessionsHintNoSelection = "Select tags to list their sessions";
	private const string SessionsNoMatch = "No sessions with the selected tags";
	private const int TextThrottleMs = 150;
	private const int ReloadThrottleMs = 300;

	private readonly SessionTreeViewModel _sessionTree;
	private readonly IDaemonClient _daemon;
	private readonly IDirectoryLabelService _labelService;
	private readonly IUiDispatcher _dispatcher;
	private readonly SessionTagLauncher _launcher;

	/// <summary>Selected tags by key (FR.24.12), with their model so they still show after leaving the index. App-run lifetime.</summary>
	private readonly Dictionary<string, SessionTagModel> _selected = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Directory paths the user collapsed in the session list, kept across rebuilds (FR.24.12).</summary>
	private readonly HashSet<string> _collapsedPaths = new(StringComparer.OrdinalIgnoreCase);

	private IReadOnlyList<SessionTagUsageModel> _index = [];
	private bool _active;
	private int _version;
	private int _reloadPending;
	private bool _suppressScopeReload;
	private bool _activating;
	private bool _hasRequested;
	private string? _requestedPath;
	private string? _scopeDirectoryPath;

	private string _searchText = string.Empty;
	private bool _isScopedToProject;
	private bool _isProjectScopeEnabled;
	private bool _isLoading;
	private string _tagsEmptyText = string.Empty;
	private string _sessionsEmptyText = string.Empty;

	public TagSearchViewModel(SessionTreeViewModel sessionTree, IDaemonClient daemon,
		IDirectoryLabelService labelService, IUiDispatcher dispatcher, SessionTagLauncher launcher)
	{
		_sessionTree = sessionTree;
		_daemon = daemon;
		_labelService = labelService;
		_dispatcher = dispatcher;
		_launcher = launcher;

		this.WhenAnyValue(x => x.SearchText)
			.Skip(1)
			.Throttle(TimeSpan.FromMilliseconds(TextThrottleMs))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => { if (_active) RebuildChips(); });

		// FR.24.11 — scope follows the tree's selected session (FR.1.10 rules).
		_sessionTree.WhenAnyValue(x => x.SelectedSession)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(UpdateScopeAvailability);

		_daemon.SessionTagsChanged += OnSessionTagsChanged;
	}

	public ObservableCollection<TagSearchChipViewModel> Chips { get; } = [];
	public ObservableCollection<RecentSessionGroupViewModel> Groups { get; } = [];

	/// <summary>Raised on every session-row click (FR.24.12); the owner selects the session in the tree.</summary>
	public event Action<SessionNodeViewModel>? SessionChosen;

	public string SearchText
	{
		get => _searchText;
		set => this.RaiseAndSetIfChanged(ref _searchText, value);
	}

	/// <summary>FR.24.11 — limits tags and sessions to the selected session's Directory node. Independent of the Tree tab's toggle.</summary>
	public bool IsScopedToProject
	{
		get => _isScopedToProject;
		set
		{
			this.RaiseAndSetIfChanged(ref _isScopedToProject, value);
			if (!_suppressScopeReload)
				ReloadIfScopeChanged();
		}
	}

	/// <summary>The toggle is interactive only while a session is selected.</summary>
	public bool IsProjectScopeEnabled
	{
		get => _isProjectScopeEnabled;
		private set => this.RaiseAndSetIfChanged(ref _isProjectScopeEnabled, value);
	}

	/// <summary>True while the tag index loads (search-box spinner, FR.1.11 style).</summary>
	public bool IsLoading
	{
		get => _isLoading;
		private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
	}

	public string TagsEmptyText
	{
		get => _tagsEmptyText;
		private set
		{
			this.RaiseAndSetIfChanged(ref _tagsEmptyText, value);
			this.RaisePropertyChanged(nameof(IsTagsEmptyVisible));
		}
	}

	public bool IsTagsEmptyVisible => _tagsEmptyText.Length > 0;

	public string SessionsEmptyText
	{
		get => _sessionsEmptyText;
		private set
		{
			this.RaiseAndSetIfChanged(ref _sessionsEmptyText, value);
			this.RaisePropertyChanged(nameof(IsSessionsEmptyVisible));
		}
	}

	public bool IsSessionsEmptyVisible => _sessionsEmptyText.Length > 0;

	public void SelectSession(SessionNodeViewModel session) => SessionChosen?.Invoke(session);

	/// <summary>The Tags tab was selected: reload the index (FR.24.13).</summary>
	public void Activate()
	{
		_active = true;

		// Reload once at the end: the scope update must not start a second load in the same call.
		_activating = true;
		try
		{
			UpdateScopeAvailability(_sessionTree.SelectedSession);
		}
		finally
		{
			_activating = false;
		}

		Reload();

		// Show the last known chips and the session-list hint at once; IsLoading is true, so the tag empty text stays hidden.
		Rebuild();
	}

	/// <summary>The Tags tab was left: stop reacting to tag changes and drop any in-flight load.</summary>
	public void Deactivate()
	{
		_active = false;
		Interlocked.Increment(ref _version);
		IsLoading = false;
	}

	private string? EffectivePath => IsScopedToProject ? _scopeDirectoryPath : null;

	/// <summary>FR.1.10 rules, copied from SessionTreeViewModel.UpdateProjectScopeAvailability.</summary>
	private void UpdateScopeAvailability(SessionNodeViewModel? session)
	{
		var wasEnabled = IsProjectScopeEnabled;
		IsProjectScopeEnabled = session != null;
		_scopeDirectoryPath = session == null ? null : _sessionTree.FindDirectoryPathOf(session);

		_suppressScopeReload = true;
		try
		{
			if (session == null)
				IsScopedToProject = false;
			else if (!wasEnabled)
				IsScopedToProject = true;
		}
		finally
		{
			_suppressScopeReload = false;
		}

		ReloadIfScopeChanged();
	}

	private void ReloadIfScopeChanged()
	{
		if (!_active || _activating)
			return;
		if (_hasRequested && string.Equals(EffectivePath, _requestedPath, StringComparison.OrdinalIgnoreCase))
			return;
		Reload();
	}

	/// <summary>Loads the index off the UI thread; a superseded load is dropped (FR.24.13). UI thread only.</summary>
	private void Reload()
	{
		var version = Interlocked.Increment(ref _version);
		var path = EffectivePath;
		_hasRequested = true;
		_requestedPath = path;
		IsLoading = true;

		_ = Task.Run(() =>
		{
			IReadOnlyList<SessionTagUsageModel>? index;
			try
			{
				index = _daemon.GetSessionTagIndex(path);
			}
			catch (Exception ex)
			{
				// Keep the previous index rather than showing a misleading "No tags yet".
				_log.Warning(ex, "Loading the session tag index failed");
				index = null;
			}

			_dispatcher.Post(() =>
			{
				if (version != Volatile.Read(ref _version) || !_active)
					return;
				if (index is not null)
					_index = index;
				IsLoading = false;
				Rebuild();
			});
		});
	}

	private void OnSessionTagsChanged(object? sender, SessionTagsChangedEventArgs e)
	{
		if (!_active)
			return;
		if (Interlocked.Exchange(ref _reloadPending, 1) == 1)
			return;

		_ = Task.Delay(ReloadThrottleMs).ContinueWith(_ => _dispatcher.Post(() =>
		{
			Interlocked.Exchange(ref _reloadPending, 0);
			if (_active)
				Reload();
		}));
	}

	private void ToggleTag(TagSearchChipViewModel chip)
	{
		if (!_selected.Remove(chip.Key))
			_selected[chip.Key] = chip.Tag;
		Rebuild();
	}

	/// <summary>Rebuilds chips and the session list (selection toggle, index load). UI thread only.</summary>
	private void Rebuild()
	{
		RebuildChips();
		RebuildSessions();
	}

	/// <summary>
	/// Rebuilds only the chips from the in-memory index (text change): the session list doesn't depend on the
	/// search text, so its rows and scroll position stay untouched while typing. UI thread only.
	/// </summary>
	private void RebuildChips()
	{
		var nodeMap = _sessionTree.BuildNodeIdMap();
		Func<string, DateTimeOffset?> lastActivityOf =
			id => nodeMap.TryGetValue(id, out var vm) ? vm.LastActivityTimestamp : null;

		var byKey = new Dictionary<string, SessionTagUsageModel>(StringComparer.OrdinalIgnoreCase);
		foreach (var usage in _index)
			byKey[usage.Tag.Key] = usage;

		// Selected tags first, even when they no longer match the text or have left the index (FR.24.12).
		var selectedUsages = _selected
			.Select(kv => byKey.TryGetValue(kv.Key, out var u) ? u : new SessionTagUsageModel { Tag = kv.Value })
			.ToList();
		var otherUsages = _index
			.Where(u => !_selected.ContainsKey(u.Tag.Key) && SessionTagSearch.Matches(u.Tag, SearchText))
			.ToList();

		var newChips = new List<TagSearchChipViewModel>();
		foreach (var usage in SessionTagSearch.Order(selectedUsages, lastActivityOf)
			.Concat(SessionTagSearch.Order(otherUsages, lastActivityOf)))
		{
			newChips.Add(new TagSearchChipViewModel(usage.Tag, usage.NodeIds.Count, _selected.ContainsKey(usage.Tag.Key), _launcher, ToggleTag));
		}

		// Skip the swap when nothing visible changed (Tooltip carries the URL and the session count).
		var unchanged = newChips.Count == Chips.Count;
		for (var i = 0; unchanged && i < newChips.Count; i++)
		{
			var a = newChips[i];
			var b = Chips[i];
			unchanged = a.Key == b.Key && a.Label == b.Label && a.Url == b.Url
				&& a.Tooltip == b.Tooltip && a.IsSelected == b.IsSelected;
		}

		if (!unchanged)
		{
			Chips.Clear();
			foreach (var chip in newChips)
				Chips.Add(chip);
		}

		TagsEmptyText = Chips.Count > 0 || IsLoading
			? string.Empty
			: _index.Count == 0
				? (IsScopedToProject ? EmptyTagsScoped : EmptyTagsUnscoped)
				: EmptyTagsNoMatch;
	}

	private void RebuildSessions()
	{
		var newGroups = new List<RecentSessionGroupViewModel>();
		if (_selected.Count > 0)
		{
			var ids = SessionTagSearch.SessionsWithAnyTag(_index, _selected.Keys);
			newGroups = SessionGroupListBuilder.Build(_sessionTree.Directories, _labelService, s => ids.Contains(s.Model.NodeId));
		}

		SessionsEmptyText = _selected.Count == 0
			? SessionsHintNoSelection
			: newGroups.Count == 0 ? SessionsNoMatch : string.Empty;

		// Same groups, same session instances in the same order: keep the existing group VMs, which preserves
		// IsExpanded and the list's scroll position.
		if (GroupsMatch(newGroups))
			return;

		Groups.Clear();
		foreach (var group in newGroups)
		{
			// FR.24.12 — groups start expanded; the user's collapsed groups stay collapsed across rebuilds.
			group.IsExpanded = !_collapsedPaths.Contains(group.Path);
			var path = group.Path;
			group.WhenAnyValue(g => g.IsExpanded).Skip(1).Subscribe(expanded =>
			{
				if (expanded)
					_collapsedPaths.Remove(path);
				else
					_collapsedPaths.Add(path);
			});
			Groups.Add(group);
		}
	}

	private bool GroupsMatch(List<RecentSessionGroupViewModel> newGroups)
	{
		if (newGroups.Count != Groups.Count)
			return false;

		for (var i = 0; i < newGroups.Count; i++)
		{
			var a = newGroups[i];
			var b = Groups[i];
			if (!string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) || a.Sessions.Count != b.Sessions.Count)
				return false;
			for (var j = 0; j < a.Sessions.Count; j++)
			{
				if (!ReferenceEquals(a.Sessions[j], b.Sessions[j]))
					return false;
			}
		}

		return true;
	}
}
