using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The session pane: header (name, state, tag chips), the transcript surface and the input box. One instance serves every
/// selected session; at most <see cref="Constants.Limits.MaxCachedTranscripts"/> sessions' entries stay in memory (LRU).
/// Entries are paged tail-first through <c>session.entries</c>, appended on <c>session.appended</c> and reloaded in full on
/// <c>session.reset</c> or a client resync. Selection is client state; the daemon is never told which session is selected.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly ITranscriptSurface _surface;
	private readonly IEntryRenderer _renderer;
	private readonly IUrlLauncher _launcher;
	private readonly IUiDispatcher _ui;
	private readonly SwitchLatencyTracker _switchTracker;
	private readonly ILogger<SessionPaneViewModel> _logger;
	private readonly TranscriptCache _cache;
	private readonly Dictionary<string, string> _drafts = new();
	private readonly MediaPreviewPolicy? _media;
	private readonly LocalPathDetector _pathDetector = new();
	private int _viewTop = -1;
	private bool _viewAtBottom = true;

	private TranscriptModel? _current;
	private int _openToken;

	private Task _appendTask = Task.CompletedTask;
	private bool _appendRunning;
	private bool _appendPending;
	private bool _missedAppend;
	private Task _olderTask = Task.CompletedTask;
	private bool _olderRunning;
	private int _olderToken;
	private Task _reloadTask = Task.CompletedTask;
	private Task _expandTask = Task.CompletedTask;
	private Task _findTask = Task.CompletedTask;
	private Task _newerTask = Task.CompletedTask;
	private bool _newerRunning;

	[ObservableProperty]
	private string? _nodeId;

	[ObservableProperty]
	private bool _hasSession;

	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private string _stateText = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(StopCommand))]
	private bool _isRunning;

	[ObservableProperty]
	private bool _isLoading;

	[ObservableProperty]
	private string? _error;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private string _inputText = string.Empty;

	[ObservableProperty]
	private bool _findBarVisible;

	[ObservableProperty]
	private string _findText = string.Empty;

	[ObservableProperty]
	private string _findStatus = string.Empty;

	public SessionPaneViewModel(
		ITessynClient client,
		ITranscriptSurface surface,
		IEntryRenderer renderer,
		IUrlLauncher launcher,
		IUiDispatcher ui,
		SwitchLatencyTracker switchTracker,
		ILogger<SessionPaneViewModel> logger)
		: this(client, surface, renderer, launcher, ui, switchTracker, logger, Constants.Limits.MaxCachedTranscripts)
	{
	}

	public SessionPaneViewModel(
		ITessynClient client,
		ITranscriptSurface surface,
		IEntryRenderer renderer,
		IUrlLauncher launcher,
		IUiDispatcher ui,
		SwitchLatencyTracker switchTracker,
		ILogger<SessionPaneViewModel> logger,
		int cacheCapacity,
		FocusViewModel? focus = null,
		FilePreviewViewModel? preview = null,
		MediaPreviewPolicy? media = null,
		IClientStateStore? stateStore = null)
	{
		_client = client;
		_surface = surface;
		_renderer = renderer;
		_launcher = launcher;
		_ui = ui;
		_switchTracker = switchTracker;
		_logger = logger;
		_cache = new TranscriptCache(cacheCapacity);
		_media = media;
		InitializeComposer(stateStore);
		Focus = focus ?? new FocusViewModel(client, ui, NullLogger<FocusViewModel>.Instance);
		Preview = preview;
		Focus.RefActivated += OnRefActivated;
		Focus.PropertyChanged += OnSideContentChanged;
		if (Preview != null)
			Preview.PropertyChanged += OnSideContentChanged;
		_surface.ViewChanged += OnViewChanged;
		_surface.PathClicked += OnPathClicked;

		_client.NotificationReceived += OnNotification;
		_client.Resynced += OnResynced;
		_surface.Painted += OnPainted;
		_surface.OlderRequested += OnOlderRequested;
		_surface.NewerRequested += OnNewerRequested;
		_surface.ExpandRequested += OnExpandRequested;
		_surface.LinkClicked += OnLinkClicked;
		_surface.FindRequested += OnFindRequested;
		_surface.FindStepRequested += OnFindStepRequested;
		_surface.FindStatusChanged += OnFindStatusChanged;
		_surface.PageRestarted += OnPageRestarted;
	}

	/// <summary>The session tag chips of the header.</summary>
	public ObservableCollection<SessionTagViewModel> Tags { get; } = [];

	/// <summary>Raised when the find box should take keyboard focus.</summary>
	public event EventHandler? FindFocusRequested;

	/// <summary>The cache of loaded transcripts (exposed for tests and diagnostics).</summary>
	public TranscriptCache Cache => _cache;

	/// <summary>Completes when no append, older-page, reload, expand or find operation is running.</summary>
	public async Task WhenIdleAsync()
	{
		await _appendTask;
		await _olderTask;
		await _newerTask;
		await _reloadTask;
		await _expandTask;
		await _findTask;
		await _appendTask;   // an on-demand page can start a catch-up append while it completes
	}

	/// <summary>Shows a session: from the cache when loaded before (then catches up), otherwise its newest page.</summary>
	/// <param name="nodeId">The session's node id.</param>
	/// <param name="title">The name known from the tree, shown until the session header arrives.</param>
	public async Task OpenAsync(string nodeId, string? title = null)
	{
		if (nodeId == NodeId && _current != null)
			return;

		// A plain selection (tree click, restored state) starts a fresh trail and drops a preview of another session's file.
		if (nodeId != NodeId)
		{
			_back.Clear();
			NotifyBack();
			Preview?.CloseCommand.Execute(null);
		}

		await OpenCoreAsync(nodeId, title, target: null, preloaded: null, shown: null);
	}

	/// <summary>
	/// Opens <paramref name="nodeId"/>. With <paramref name="target"/> the view starts at that entry (from <paramref name="preloaded"/>,
	/// the page <c>session.entries</c> returned around it) and highlights the quote; without it the newest page shows, scrolled to the bottom.
	/// <paramref name="shown"/> is called once, right after the page was handed the entries and only if this open is still the current one
	/// (so a caller can announce the navigation without racing a later selection). Returns whether that happened. No newer pages are
	/// loaded here: after a jump they come on demand when the user scrolls down (<see cref="OnNewerRequested"/>).
	/// </summary>
	private async Task<bool> OpenCoreAsync(string nodeId, string? title, JumpTarget? target, SessionEntriesResult? preloaded, Action? shown)
	{
		var wasShown = false;
		if (nodeId != NodeId)
			AbandonImport();   // first: a copy that ends at once still adds to the text this switch is about to save
		if (NodeId != null)
			_drafts[NodeId] = InputText;

		var token = ++_openToken;
		_switchTracker.Begin(nodeId);
		NodeId = nodeId;
		HasSession = true;
		Title = title ?? nodeId;
		StateText = string.Empty;
		IsRunning = false;
		Error = null;
		Tags.Clear();
		FindBarVisible = false;
		FindText = string.Empty;
		FindStatus = string.Empty;
		InputText = _drafts.GetValueOrDefault(nodeId, string.Empty);
		IsLoading = true;
		_current = null;
		_missedAppend = false;
		_olderRunning = false;
		NewerFailed = false;
		_ = Focus.SetSessionAsync(nodeId);

		try
		{
			var detailTask = _client.SessionGetAsync(nodeId);
			_ = detailTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observed even when the switch is superseded
			// A cached model that stops short of the newest entries (left by a jump) is not a bottom view: reload the newest page instead.
			var warm = preloaded == null && _cache.TryGet(nodeId, out var cached0) && (target == null ? !cached0.HasNewer : cached0.Entries.Any(e => e.EntryId == target.EntryId));
			_switchTracker.MarkWarm(nodeId, warm);
			TranscriptModel model;
			if (warm)
			{
				_cache.TryGet(nodeId, out model!);
			}
			else
			{
				var page = preloaded ?? await _client.SessionEntriesAsync(nodeId, limit: Constants.Limits.EntriesPageSize);
				if (token != _openToken)
					return wasShown;

				model = await BuildModelAsync(nodeId, page);
				if (token != _openToken)
					return wasShown;

				_cache.Put(model);
			}

			_current = model;
			_viewTop = -1;
			_viewAtBottom = target == null;
			_surface.Load(nodeId, model.Views, model.HasOlder, toBottom: target == null, hasNewer: model.HasNewer);
			if (target != null)
				ScrollToTarget(model, target);
			wasShown = true;
			shown?.Invoke();
			_logger.LogInformation("Session {NodeId} opened: {Loaded} of {Total} entries loaded ({Kind})", nodeId, model.Entries.Count, model.Total, warm ? "warm" : "cold");

			var detail = await detailTask;
			if (token != _openToken)
				return wasShown;

			ApplyDetail(detail);
			if (detail.Epoch != model.Epoch)
				await ReloadCoreAsync(token);
			else if (!model.HasNewer && (warm || _missedAppend || detail.EntryCount > model.LastIndex + 1))
				await RequestAppend();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Opening session {NodeId} failed", nodeId);
			if (token == _openToken)
				Error = "Could not load the session: " + ex.Message;
		}
		finally
		{
			if (token == _openToken)
				IsLoading = false;
		}

		return wasShown;
	}

	/// <summary>Stops showing a session (e.g. when it was deleted).</summary>
	public void Close()
	{
		if (NodeId != null)
			_drafts[NodeId] = InputText;

		_openToken++;
		NodeId = null;
		HasSession = false;
		_current = null;
		Title = string.Empty;
		Tags.Clear();
		_back.Clear();
		NotifyBack();
		Focus.Clear();
		Preview?.CloseCommand.Execute(null);
		_surface.Load(string.Empty, [], hasOlder: false, toBottom: true);
	}

	private bool CanSend() => HasSession && !string.IsNullOrWhiteSpace(InputText);

	partial void OnHasSessionChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

	[RelayCommand(CanExecute = nameof(CanSend))]
	private async Task SendAsync()
	{
		var id = NodeId;
		var text = InputText.Trim();
		if (id == null || text.Length == 0)
			return;

		try
		{
			Error = null;
			var result = await _client.SessionSendAsync(id, text);
			if (!result.Accepted)
			{
				Error = "The daemon did not accept the message.";
				return;
			}

			if (id == NodeId)
				InputText = string.Empty;
			else
				_drafts.Remove(id);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "session.send failed for {NodeId}", id);
			Error = "Send failed: " + ex.Message;
		}
	}

	[RelayCommand(CanExecute = nameof(IsRunning))]
	private async Task StopAsync()
	{
		var id = NodeId;
		if (id == null)
			return;

		try
		{
			await _client.SessionStopAsync(id);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "session.stop failed for {NodeId}", id);
			Error = "Stop failed: " + ex.Message;
		}
	}

	// ---- find ----

	[RelayCommand]
	private void ShowFind()
	{
		FindBarVisible = true;
		FindFocusRequested?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void CloseFind()
	{
		FindBarVisible = false;
		FindStatus = string.Empty;
		_surface.ClearFind();
	}

	[RelayCommand]
	private void FindNext() => _surface.FindStep(1);

	[RelayCommand]
	private void FindPrevious() => _surface.FindStep(-1);

	[RelayCommand]
	private Task FindAsync()
	{
		_findTask = RunFindAsync(FindText);
		return _findTask;
	}

	private async Task RunFindAsync(string term)
	{
		var model = _current;
		if (model == null || term.Length == 0)
		{
			_surface.ClearFind();
			FindStatus = string.Empty;
			return;
		}

		// Search the raw text of the loaded entries off the UI thread; the page highlights the rendered ones.
		var snapshot = model.Entries.ToList();
		var matches = await Task.Run(() => snapshot.Where(e => e.Content.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(e => e.Index).ToList());
		if (model != _current)
			return;

		FindStatus = matches.Count == 0 ? "No matches" : string.Empty;
		_surface.Find(term, matches);
	}

	private void OnFindRequested(object? sender, EventArgs e) => _ui.Post(ShowFind);

	private void OnFindStepRequested(object? sender, int direction) => _ui.Post(() => _surface.FindStep(direction));

	private void OnFindStatusChanged(object? sender, FindStatusEventArgs e)
	{
		_ui.Post(() => FindStatus = e.Count == 0 ? "No matches" : $"{e.Current} of {e.Count}");
	}

	// ---- header ----

	private void ApplyDetail(SessionDetailDto detail)
	{
		Title = detail.Name;
		IsRunning = detail.State.Running;
		StateText = StateTextOf(detail.State);
		Tags.Clear();
		foreach (var tag in detail.Tags)
			Tags.Add(new SessionTagViewModel(tag, _launcher));
	}

	private static string StateTextOf(SessionStateDto state) => state.Running ? "Running" : "Idle";

	// ---- push events ----

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		switch (notification.Method)
		{
			case RemoteProtocol.Events.SessionAppended:
			{
				var payload = notification.Params.Deserialize<SessionEpochPayload>(TdcpJsonOptions.Default);
				if (payload != null)
					_ui.Post(() => OnAppended(payload));
				break;
			}

			case RemoteProtocol.Events.SessionReset:
			{
				var payload = notification.Params.Deserialize<SessionEpochPayload>(TdcpJsonOptions.Default);
				if (payload != null)
					_ui.Post(() => OnReset(payload));
				break;
			}

			case RemoteProtocol.Events.SessionChanged:
			{
				var payload = notification.Params.Deserialize<SessionChangedPayload>(TdcpJsonOptions.Default);
				if (payload != null)
					_ui.Post(() => OnStateChanged(payload.State));
				break;
			}
		}
	}

	private void OnAppended(SessionEpochPayload payload)
	{
		if (payload.NodeId != NodeId)
			return;

		if (_current == null)
		{
			_missedAppend = true;
			return;
		}

		_ = RequestAppend();
	}

	/// <summary>The one guarded path for catching up: at most one append loop runs; requests during a run cause one more pass.</summary>
	private Task RequestAppend()
	{
		_appendPending = true;
		if (!_appendRunning)
		{
			_appendRunning = true;
			_appendTask = RunAppendLoopAsync();
		}

		return _appendTask;
	}

	private async Task RunAppendLoopAsync()
	{
		try
		{
			while (_appendPending)
			{
				_appendPending = false;
				try
				{
					await AppendNewCoreAsync(_openToken);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Appending new entries failed");
				}
			}
		}
		finally
		{
			_appendRunning = false;
		}
	}

	private void OnReset(SessionEpochPayload payload)
	{
		if (payload.NodeId != NodeId)
		{
			_cache.Remove(payload.NodeId);
			return;
		}

		_reloadTask = ReloadAsync();
	}

	private void OnStateChanged(SessionStateDto state)
	{
		if (state.Id != NodeId)
			return;

		IsRunning = state.Running;
		StateText = StateTextOf(state);
	}

	private void OnResynced(object? sender, ResyncedEventArgs e)
	{
		_ui.Post(() =>
		{
			var current = NodeId;
			_cache.Clear();
			if (current != null && HasSession)
				_reloadTask = ReloadAsync();
		});
	}

	// ---- loading ----

	private async Task ReloadAsync()
	{
		var token = _openToken;
		try
		{
			await ReloadCoreAsync(token);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reloading the transcript failed");
			if (token == _openToken)
				Error = "Could not reload the session: " + ex.Message;
		}
	}

	private async Task ReloadCoreAsync(int token)
	{
		var id = NodeId;
		if (id == null)
			return;

		var page = await _client.SessionEntriesAsync(id, limit: Constants.Limits.EntriesPageSize);
		if (token != _openToken)
			return;

		var model = await BuildModelAsync(id, page);
		if (token != _openToken)
			return;

		_cache.Put(model);
		_current = model;
		_viewAtBottom = true;
		_viewTop = -1;
		_surface.Load(id, model.Views, model.HasOlder, toBottom: true, hasNewer: model.HasNewer);
	}

	private async Task AppendNewCoreAsync(int token)
	{
		var model = _current;
		if (model == null || model.HasNewer)   // a model that stops short of the newest entries (after a jump) grows only on demand
			return;

		while (true)
		{
			var page = await _client.SessionEntriesAsync(model.NodeId, afterIndex: model.LastIndex, limit: Constants.Limits.EntriesPageSize);
			if (token != _openToken || model != _current)
				return;

			if (page.Epoch != model.Epoch)
			{
				await ReloadCoreAsync(token);
				return;
			}

			var fresh = page.Entries.Where(e => e.Index > model.LastIndex).ToList();
			if (fresh.Count == 0)
			{
				model.UpdateTotal(page.Total);
				return;
			}

			var views = await RenderAsync(fresh);
			if (token != _openToken || model != _current)
				return;

			fresh = fresh.Where(e => e.Index > model.LastIndex).ToList();
			if (fresh.Count != views.Count)
				views = views.Where(v => v.Index > model.LastIndex).ToList();

			model.Append(fresh, views, page.Total);
			_surface.Append(views);
			if (page.Entries.Count < Constants.Limits.EntriesPageSize)
				return;
		}
	}

	private void OnNewerRequested(object? sender, EventArgs e) => _ui.Post(StartLoadNewer);

	private void StartLoadNewer()
	{
		if (_newerRunning)
			return;

		_newerRunning = true;
		NewerFailed = false;
		_newerTask = LoadNewerAsync();
	}

	/// <summary>The last on-demand page failed; the page does not ask again until the user scrolls, and this command asks at once.</summary>
	[ObservableProperty]
	private bool _newerFailed;

	[RelayCommand]
	private void RetryNewer() => StartLoadNewer();

	/// <summary>One page of entries after the loaded ones, when the user scrolled to the end of a view that started at a jump target.</summary>
	private async Task LoadNewerAsync()
	{
		var token = _openToken;
		var model = _current;
		try
		{
			if (model == null || !model.HasNewer)
			{
				_surface.Append([], hasNewer: false);
				return;
			}

			var page = await _client.SessionEntriesAsync(model.NodeId, afterIndex: model.LastIndex, limit: Constants.Limits.EntriesPageSize);
			if (token != _openToken || model != _current)
				return;

			if (page.Epoch != model.Epoch)
			{
				await ReloadCoreAsync(token);
				return;
			}

			var fresh = page.Entries.Where(e => e.Index > model.LastIndex).ToList();
			if (fresh.Count == 0)
			{
				model.UpdateTotal(model.LastIndex + 1);   // nothing newer after all
				_surface.Append([], hasNewer: false);
				return;
			}

			var views = await RenderAsync(fresh);
			if (token != _openToken || model != _current)
				return;

			model.Append(fresh, views, page.Total);
			_surface.Append(views, model.HasNewer);

			// Live appends were ignored while the model stopped short; an entry that arrived during this last page would otherwise wait for the next event (N4).
			if (!model.HasNewer)
				_ = RequestAppend();
		}
		catch (Exception ex)
		{
			// No Append and no automatic retry (N3): the page keeps its request marked as pending until the user scrolls, and Retry asks at once.
			_logger.LogWarning(ex, "Loading newer entries failed");
			if (token == _openToken)
			{
				NewerFailed = true;
				_surface.NewerLoadFailed();
			}
		}
		finally
		{
			_newerRunning = false;
		}
	}

	private void OnOlderRequested(object? sender, EventArgs e) => _ui.Post(() =>
	{
		if (_olderRunning)
			return;

		_olderRunning = true;
		_olderTask = LoadOlderAsync(++_olderToken);
	});

	private async Task LoadOlderAsync(int olderToken)
	{
		var token = _openToken;
		var model = _current;
		try
		{
			if (model == null || !model.HasOlder)
			{
				_surface.Prepend([], hasOlder: false);
				return;
			}

			var page = await _client.SessionEntriesAsync(model.NodeId, beforeIndex: model.FirstIndex, limit: Constants.Limits.EntriesPageSize);
			if (token != _openToken || model != _current)
				return;

			if (page.Epoch != model.Epoch)
			{
				await ReloadCoreAsync(token);
				return;
			}

			var views = await RenderAsync(page.Entries);
			if (token != _openToken || model != _current)
				return;

			model.Prepend(page.Entries, views);
			_surface.Prepend(views, model.HasOlder);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Loading older entries failed");
			_surface.Prepend([], hasOlder: model?.HasOlder ?? false);
		}
		finally
		{
			if (olderToken == _olderToken)
				_olderRunning = false;
		}
	}

	private void OnExpandRequested(object? sender, int index) => _ui.Post(() => _expandTask = ExpandAsync(index));

	private async Task ExpandAsync(int index)
	{
		var token = _openToken;
		var model = _current;
		if (model == null)
			return;

		try
		{
			var full = await _client.SessionEntryAsync(model.NodeId, index, model.Epoch);
			if (token != _openToken || model != _current)
				return;

			var view = await Task.Run(() => _renderer.Render(full));
			if (token != _openToken || model != _current)
				return;

			model.Replace(full, view);
			_surface.Replace(view);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Loading the full entry {Index} failed", index);
		}
	}

	/// <summary>The page restarted (reload or crash recovery): replay everything loaded for the current session.</summary>
	private void OnPageRestarted(object? sender, EventArgs e) => _ui.Post(() =>
	{
		var model = _current;
		if (model != null)
			_surface.Load(model.NodeId, model.Views, model.HasOlder, toBottom: true, hasNewer: model.HasNewer);
	});

	private void OnLinkClicked(object? sender, string url) => _ui.Post(() => _launcher.TryOpen(url));

	private void OnPainted(object? sender, TranscriptPaintedEventArgs e) => _ui.Post(() => _switchTracker.Painted(e.Key, e.JsMilliseconds));

	private async Task<TranscriptModel> BuildModelAsync(string nodeId, SessionEntriesResult page)
	{
		var views = await RenderAsync(page.Entries);
		return new TranscriptModel(nodeId, page.Epoch, page.Total, page.Entries, views);
	}

	private Task<List<TranscriptEntryView>> RenderAsync(IReadOnlyList<SessionEntryDto> entries) =>
		Task.Run(() => entries.Select(_renderer.Render).ToList());

	public void Dispose()
	{
		_importCts?.Cancel();
		_client.NotificationReceived -= OnNotification;
		_client.Resynced -= OnResynced;
		_surface.Painted -= OnPainted;
		_surface.OlderRequested -= OnOlderRequested;
		_surface.NewerRequested -= OnNewerRequested;
		_surface.ExpandRequested -= OnExpandRequested;
		_surface.LinkClicked -= OnLinkClicked;
		_surface.FindRequested -= OnFindRequested;
		_surface.FindStepRequested -= OnFindStepRequested;
		_surface.FindStatusChanged -= OnFindStatusChanged;
		_surface.PageRestarted -= OnPageRestarted;
		_surface.ViewChanged -= OnViewChanged;
		_surface.PathClicked -= OnPathClicked;
		Focus.RefActivated -= OnRefActivated;
		Focus.PropertyChanged -= OnSideContentChanged;
		if (Preview != null)
			Preview.PropertyChanged -= OnSideContentChanged;
		Focus.Dispose();
		Preview?.Dispose();
	}
}
