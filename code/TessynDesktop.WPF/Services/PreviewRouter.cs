using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IPreviewRouter"/>: routes previewed files and web addresses to the window of the matching group while the mode is active (two or more
/// monitors and the toggle on), owns the group windows and the pinned windows, applies auto-close, reacts to the mode and monitor changes and keeps the
/// list of open windows so that the next start reopens them (FR.29.1, FR.29.3–FR.29.11). Contains no WPF types; every method runs on the UI thread
/// (settings events, which may come from another thread, are marshalled with <see cref="IUiDispatcher"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRouter : IPreviewRouter, IDisposable
{
	private readonly IPreviewSettingsService _settings;
	private readonly PreviewGroupMatcher _matcher;
	private readonly IMonitorWatcher _monitors;
	private readonly IPreviewWindowFactory _factory;
	private readonly UrlPolicy _urls;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<PreviewRouter> _logger;
	private readonly IClientStateStore? _state;
	private readonly IPreviewDockFactory? _dock;

	/// <summary>Every open window or docked panel in the order it was opened (one registry; the target kind is <see cref="OpenWindow.IsPanel"/>).</summary>
	private readonly List<OpenWindow> _windows = [];

	/// <summary>Saved windows the restore has not reached yet; they stay in the saved list until it has.</summary>
	private List<OpenPreviewWindowModel> _pending = [];

	/// <summary>The saved window being restored now (its file is checked); it counts as saved until its window exists.</summary>
	private OpenPreviewWindowModel? _handling;

	/// <summary>Saved panels the restore has not reached yet (FR.29.22); they stay in the saved panel list until it has.</summary>
	private List<OpenPreviewPanelModel> _pendingPanels = [];

	/// <summary>The saved panel being restored now; it counts as saved until its panel exists.</summary>
	private OpenPreviewPanelModel? _handlingPanel;

	private bool _lastCanToggle;
	private bool _lastEnabled;
	private bool _lastActive;
	private bool _restoreStarted;
	private bool _exiting;
	private bool _disposed;

	public PreviewRouter(
		IPreviewSettingsService settings,
		PreviewGroupMatcher matcher,
		IMonitorWatcher monitors,
		IPreviewWindowFactory factory,
		UrlPolicy urls,
		IUiDispatcher ui,
		ILogger<PreviewRouter> logger,
		IClientStateStore? state = null,
		IPreviewDockFactory? dock = null)
	{
		_dock = dock;
		_settings = settings;
		_matcher = matcher;
		_monitors = monitors;
		_factory = factory;
		_urls = urls;
		_ui = ui;
		_logger = logger;
		_state = state;
		_lastCanToggle = CanToggle;
		_lastEnabled = IsEnabled;
		_lastActive = IsActive;
		_monitors.Changed += OnMonitorsChanged;
		_settings.WindowsEnabledChanged += OnSettingsEvent;
		_settings.GroupsChanged += OnGroupsEvent;
	}

	/// <summary>One open window and what the router knows about it.</summary>
	private sealed class OpenWindow
	{
		public OpenWindow(IPreviewWindowHandle handle, string groupId, bool pinned, string groupName, bool isPanel)
		{
			Handle = handle;
			IsPanel = isPanel;
			GroupId = groupId;
			IsPinned = pinned;
			GroupName = groupName;
		}

		public IPreviewWindowHandle Handle { get; }

		/// <summary>A docked panel in the main window (FR.29.17) rather than an external window.</summary>
		public bool IsPanel { get; }

		/// <summary>The default Preview panel: the files that match no group (FR.29.18); never pinned, never auto-closed.</summary>
		public bool IsDefaultPanel => IsPanel && GroupId == Constants.Previews.DefaultPanelId;

		public string GroupId { get; }

		public bool IsPinned { get; set; }

		public string GroupName { get; set; }

		public EventHandler<PreviewWindowClosedEventArgs>? OnClosed { get; set; }

		public EventHandler? OnPinned { get; set; }

		public EventHandler? OnItemChanged { get; set; }

		public EventHandler? OnPlacementChanged { get; set; }
	}

	public event EventHandler? StateChanged;

	public bool CanToggle => _monitors.Monitors.Count >= 2;

	public bool IsEnabled => _settings.WindowsEnabled;

	public bool IsActive => CanToggle && IsEnabled;

	public string? CurrentNodeId { get; private set; }

	public void SetEnabled(bool enabled)
	{
		_settings.SetWindowsEnabled(enabled);
		Refresh();   // the settings event may come later or on another thread; Refresh is idempotent
	}

	/// <summary>Docked mode (FR.29.17): the external windows are off (not <see cref="IsActive"/>) and the main window can dock panels.</summary>
	private bool IsDocked => !IsActive && _dock != null;

	public bool WouldRouteFile(string? fileName)
	{
		if ((IsActive || IsDocked) && _matcher.MatchFile(_settings.Groups, fileName) != null)
			return true;

		return _dock != null && GoesToDefaultPanel(fileName);   // the default Preview panel replaces the old preview pane in both modes (FR.29.18)
	}

	public bool TryOpenFile(PreviewItemModel item, PreviewTarget fileHandle, ITessynClient client)
	{
		var docked = IsDocked;
		var match = IsActive || docked ? _matcher.MatchFile(_settings.Groups, item.FileName) : null;
		if (match != null)
			return OpenInGroupTarget(match.Group, docked, item, fileHandle, client);

		if (_dock == null || !GoesToDefaultPanel(item.FileName))
			return false;   // a video is left to the default media player (FR.26.6)

		return OpenInDefaultPanel(item, fileHandle, client);
	}

	public bool TryOpenUrl(PreviewItemModel item)
	{
		var docked = IsDocked;
		if ((!IsActive && !docked) || !_urls.TryParse(item.Url, out _))
			return false;

		var match = _matcher.MatchUrl(_settings.Groups, item.Url);
		if (match == null)
			return false;   // the system browser (FR.29.18)

		return OpenInGroupTarget(match.Group, docked, item, null, null);
	}

	/// <summary>A file that matches no group goes to the default Preview panel, except a video (FR.29.18, decision 1); in both modes.</summary>
	private bool GoesToDefaultPanel(string? fileName)
	{
		if (string.IsNullOrEmpty(fileName))
			return false;

		return !Constants.FileTypes.InlineVideo.Contains(Path.GetExtension(fileName));
	}

	public void OnSessionShown(string? nodeId)
	{
		CurrentNodeId = nodeId;
		if (nodeId == null)
			return;   // no session shown is not evidence that the user moved on (a transient clear)

		var groups = _settings.Groups;
		foreach (var window in _windows.ToList())
		{
			if (window.IsPinned || window.IsDefaultPanel || window.Handle.Item == null || window.Handle.Item.SourceNodeId == nodeId || !IsAutoClose(groups, window.GroupId))
				continue;

			CloseByRouter(window, "auto-close: another session is shown");
		}
	}

	public void OnFocusItemsClosed(string nodeId, IReadOnlyCollection<string> closedItemIds)
	{
		if (closedItemIds.Count == 0)
			return;

		var groups = _settings.Groups;
		foreach (var window in _windows.ToList())
		{
			var item = window.Handle.Item;
			if (window.IsPinned || window.IsDefaultPanel || item == null || item.SourceNodeId != nodeId || item.SourceFocusItemId == null
				|| !closedItemIds.Contains(item.SourceFocusItemId) || !IsAutoClose(groups, window.GroupId))
				continue;

			CloseByRouter(window, "auto-close: its Focus item closed");
		}
	}

	public void CloseAllForExit()
	{
		SaveList();
		_exiting = true;
		foreach (var window in _windows.ToList())
			CloseByRouter(window, "the app exits");
	}

	public async Task RestoreAsync(ITessynClient client)
	{
		if (_restoreStarted)
			return;

		_restoreStarted = true;
		_pending = _settings.OpenWindows.ToList();
		_pendingPanels = _settings.OpenPanels.ToList();
		try
		{
			while (_pending.Count > 0)
			{
				var saved = _pending[0];
				_pending.RemoveAt(0);
				_handling = saved;   // still in the saved list while its file is checked
				try
				{
					await RestoreOneAsync(saved, client);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Restoring a preview window of group {GroupId} failed", saved.GroupId);
				}
				finally
				{
					_handling = null;
				}
			}

			while (_pendingPanels.Count > 0)
			{
				var saved = _pendingPanels[0];
				_pendingPanels.RemoveAt(0);
				_handlingPanel = saved;
				try
				{
					await RestorePanelAsync(saved, client);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Restoring a docked preview panel of group {GroupId} failed", saved.GroupId);
				}
				finally
				{
					_handlingPanel = null;
				}
			}
		}
		finally
		{
			_pending = [];
			_handling = null;
			_pendingPanels = [];
			_handlingPanel = null;
			SaveList();
			_logger.LogInformation("Preview windows restored: {Count} open", _windows.Count);
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_monitors.Changed -= OnMonitorsChanged;
		_settings.WindowsEnabledChanged -= OnSettingsEvent;
		_settings.GroupsChanged -= OnGroupsEvent;
		foreach (var window in _windows.ToList())
			Detach(window);
		_windows.Clear();
	}

	// ---- opening ----

	/// <summary>Shows the item in the group's window, or in the group's docked panel; the group's slot holds the one target that is not pinned.</summary>
	private bool OpenInGroupTarget(PreviewGroupModel group, bool panel, PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client)
	{
		var window = _windows.FirstOrDefault(w => !w.IsPinned && w.IsPanel == panel && w.GroupId == group.Id);
		if (window == null)
		{
			window = CreateWindow(group, pinned: false, ownPlacement: null, panel);
			if (window == null)
				return false;
		}

		window.Handle.Show(item.Clone(), fileHandle, client);
		_logger.LogInformation("Preview: {Kind} opened in the {Target} of group {GroupId}", item.Kind, panel ? "panel" : "window", group.Id);
		SaveList();
		return true;
	}

	private bool OpenInDefaultPanel(PreviewItemModel item, PreviewTarget fileHandle, ITessynClient client)
	{
		var panel = _windows.FirstOrDefault(w => w.IsDefaultPanel) ?? CreateWindow(null, pinned: false, ownPlacement: null, panel: true);
		if (panel == null)
			return false;

		panel.Handle.Show(item.Clone(), fileHandle, client);
		_logger.LogInformation("Preview: {Kind} opened in the default Preview panel", item.Kind);
		SaveList();
		return true;
	}

	/// <summary>Creates the window, or the docked panel (<paramref name="panel"/>); a null <paramref name="group"/> is the default Preview panel.</summary>
	private OpenWindow? CreateWindow(PreviewGroupModel? group, bool pinned, PreviewPlacementModel? ownPlacement, bool panel = false, PreviewDockLayoutModel? ownLayout = null, string? panelId = null)
	{
		var groupId = group?.Id ?? Constants.Previews.DefaultPanelId;
		IPreviewWindowHandle handle;
		try
		{
			handle = panel ? _dock!.Create(group, pinned, ownLayout, panelId) : _factory.Create(group!, pinned, ownPlacement);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Creating the preview {Target} of group {GroupId} failed", panel ? "panel" : "window", groupId);
			return null;
		}

		var window = new OpenWindow(handle, groupId, pinned, group?.Name ?? Constants.Previews.DefaultPanelName, panel);
		window.OnClosed = (_, args) => OnWindowClosed(window, args);
		window.OnPinned = (_, _) => OnWindowPinned(window);
		window.OnItemChanged = (_, _) => SaveList();
		window.OnPlacementChanged = (_, _) => OnWindowPlacementChanged(window);
		handle.Closed += window.OnClosed;
		handle.Pinned += window.OnPinned;
		handle.ItemChanged += window.OnItemChanged;
		handle.PlacementChanged += window.OnPlacementChanged;
		_windows.Add(window);
		return window;
	}

	private void Detach(OpenWindow window)
	{
		window.Handle.Closed -= window.OnClosed;
		window.Handle.Pinned -= window.OnPinned;
		window.Handle.ItemChanged -= window.OnItemChanged;
		window.Handle.PlacementChanged -= window.OnPlacementChanged;
	}

	// ---- window events ----

	private void OnWindowClosed(OpenWindow window, PreviewWindowClosedEventArgs args)
	{
		if (!_windows.Remove(window))
			return;

		Detach(window);
		_logger.LogInformation("Preview window of group {GroupId} closed ({Reason})", window.GroupId, args.ByUser ? "by the user" : "by the app");
		if (!_exiting)
			SaveList();
	}

	/// <summary>A pinned window settled at a new place: the list is saved again, so a crash does not lose the move (FR.29.11).</summary>
	private void OnWindowPlacementChanged(OpenWindow window)
	{
		if (window.IsPinned)
			SaveList();   // a pinned window's place, or a pinned panel's own layout (FR.29.22)
	}

	private void OnWindowPinned(OpenWindow window)
	{
		if (window.IsPinned || window.IsDefaultPanel)
			return;   // the default panel has no Pin (FR.29.18)

		window.IsPinned = true;   // it leaves the group's slot: the group's next item opens a new window
		_logger.LogInformation("Preview window of group {GroupId} pinned", window.GroupId);
		SaveList();
	}

	private void CloseByRouter(OpenWindow window, string reason)
	{
		_logger.LogInformation("Closing the preview window of group {GroupId}: {Reason}", window.GroupId, reason);
		window.Handle.Close();
		if (_windows.Contains(window))
		{
			// A handle that did not raise Closed: forget it anyway.
			_windows.Remove(window);
			Detach(window);
		}
	}

	// ---- mode, monitors, groups ----

	private void OnMonitorsChanged(object? sender, EventArgs e) => _ui.Post(Refresh);

	private void OnSettingsEvent(object? sender, EventArgs e) => _ui.Post(Refresh);

	private void OnGroupsEvent(object? sender, EventArgs e) => _ui.Post(ApplyGroupChanges);

	private void Refresh()
	{
		if (_disposed)
			return;

		var canToggle = CanToggle;
		var enabled = IsEnabled;
		var active = canToggle && enabled;
		if (canToggle == _lastCanToggle && enabled == _lastEnabled && active == _lastActive)
			return;

		var deactivated = _lastActive && !active;
		var activated = !_lastActive && active;
		_lastCanToggle = canToggle;
		_lastEnabled = enabled;
		_lastActive = active;
		_logger.LogInformation("Preview windows mode: toggle {CanToggle}, enabled {Enabled}, active {Active}", canToggle, enabled, active);
		if (deactivated)
		{
			foreach (var window in _windows.Where(w => !w.IsPinned && !w.IsPanel).ToList())
				CloseByRouter(window, "the mode turned off");
		}

		if (activated)
		{
			// The external windows take over: the group panels close; pinned panels and the default Preview panel (the old pane's replacement) stay (FR.29.23, decision 2).
			foreach (var window in _windows.Where(w => !w.IsPinned && w.IsPanel && !w.IsDefaultPanel).ToList())
				CloseByRouter(window, "the mode turned on");
		}

		StateChanged?.Invoke(this, EventArgs.Empty);
	}

	private void ApplyGroupChanges()
	{
		if (_disposed)
			return;

		var groups = _settings.Groups;
		foreach (var window in _windows.ToList())
		{
			if (window.IsDefaultPanel)
				continue;

			var group = groups.FirstOrDefault(g => g.Id == window.GroupId);
			if (group == null)
			{
				if (!window.IsPinned)
					CloseByRouter(window, "its group was removed");

				continue;
			}

			if (group.Name != window.GroupName)
			{
				window.GroupName = group.Name;
				window.Handle.SetGroupName(group.Name);
			}
		}
	}

	private bool IsAutoClose(IReadOnlyList<PreviewGroupModel> groups, string groupId) => groups.FirstOrDefault(g => g.Id == groupId)?.AutoClose == true;

	// ---- the saved list ----

	private void SaveList()
	{
		if (_disposed || _exiting)
			return;   // at exit the list was saved whole first; a panel that flushes its layout while closing must not save the shorter list

		SaveWindows();
		SavePanels();
	}

	/// <summary>The open docked panels with their items (FR.29.22); a pinned panel also with its own layout, a group panel's layout is the group's (<c>previewDock</c> layouts).</summary>
	private void SavePanels()
	{
		var list = new List<OpenPreviewPanelModel>();
		foreach (var window in _windows)
		{
			var item = window.Handle.Item;
			if (item == null || !window.IsPanel)
				continue;   // not shown yet

			list.Add(new OpenPreviewPanelModel
			{
				GroupId = window.GroupId,
				Pinned = window.IsPinned,
				Item = item.Clone(),
				Layout = window.IsPinned ? (window.Handle as IDockedPreviewPanelHandle)?.CurrentLayout : null,
				PanelId = window.IsPinned ? (window.Handle as IDockedPreviewPanelHandle)?.PanelId : null,
			});
		}

		if (_handlingPanel != null)
			list.Add(_handlingPanel);

		list.AddRange(_pendingPanels);   // saved panels the restore has not reached yet are not lost
		try
		{
			_settings.SaveOpenPanels(list);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Saving the list of open preview panels failed");
		}
	}

	private void SaveWindows()
	{
		var list = new List<OpenPreviewWindowModel>();
		foreach (var window in _windows)
		{
			var item = window.Handle.Item;
			if (item == null || window.IsPanel)
				continue;   // not shown yet; panels are in their own list (SavePanels)

			list.Add(new OpenPreviewWindowModel
			{
				GroupId = window.GroupId,
				Pinned = window.IsPinned,
				Item = item.Clone(),
				Placement = window.IsPinned ? window.Handle.CurrentPlacement : null,
			});
		}

		if (_handling != null)
			list.Add(_handling);

		list.AddRange(_pending);   // saved windows the restore has not reached yet are not lost
		try
		{
			_settings.SaveOpenWindows(list);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Saving the list of open preview windows failed");
		}
	}

	// ---- restore ----

	private async Task RestoreOneAsync(OpenPreviewWindowModel saved, ITessynClient client)
	{
		var item = saved.Item;
		var group = _settings.Groups.FirstOrDefault(g => g.Id == saved.GroupId);
		if (group == null)
		{
			_logger.LogInformation("Restore: skipped a window, group {GroupId} no longer exists", saved.GroupId);
			return;
		}

		if (!saved.Pinned && !Admissible(group, item))
			return;

		if (item.Kind == PreviewItemKind.Url && !_urls.IsAllowed(item.Url))
		{
			_logger.LogInformation("Restore: skipped a window of group {GroupId}, its address is not http(s)", group.Id);
			return;
		}

		PreviewTarget? target = null;
		if (item.Kind == PreviewItemKind.File)
		{
			target = await ResolveFileAsync(item, client);
			if (target == null)
				return;

			// The awaits above gave the user time to change things: decide again.
			group = _settings.Groups.FirstOrDefault(g => g.Id == saved.GroupId);
			if (group == null || (!saved.Pinned && !Admissible(group, item)))
				return;
		}

		_handling = null;   // its window comes now and takes its place in the saved list
		var window = CreateWindow(group, saved.Pinned, saved.Pinned ? saved.Placement : null);
		if (window == null)
			return;

		window.Handle.Show(item.Clone(), target, item.Kind == PreviewItemKind.File ? client : null);
		_logger.LogInformation("Restore: reopened a {Pinned} window of group {GroupId}", saved.Pinned ? "pinned" : "group", group.Id);
	}

	/// <summary>
	/// Reopens one saved docked panel (FR.29.22, FR.29.11): a group panel only in docked mode, a pinned panel in either mode (it stays a docked panel); a file is checked again, a web
	/// page comes back at its address; whatever cannot open is skipped with a log line. Never takes the keyboard focus (the panel's own Show does not).
	/// </summary>
	private async Task RestorePanelAsync(OpenPreviewPanelModel saved, ITessynClient client)
	{
		var item = saved.Item;
		var isDefault = saved.GroupId == Constants.Previews.DefaultPanelId;
		var pinned = saved.Pinned && !isDefault;   // the default panel has no Pin
		if (_dock == null)
		{
			_logger.LogInformation("Restore: skipped a panel of group {GroupId}, there is no docking area", saved.GroupId);
			return;
		}

		PreviewGroupModel? group = null;
		if (!isDefault)
		{
			group = _settings.Groups.FirstOrDefault(g => g.Id == saved.GroupId);
			if (group == null)
			{
				_logger.LogInformation("Restore: skipped a panel, group {GroupId} no longer exists", saved.GroupId);
				return;
			}
		}

		if (!pinned && !PanelAdmissible(group, item))
			return;

		if (item.Kind == PreviewItemKind.Url && !_urls.IsAllowed(item.Url))
		{
			_logger.LogInformation("Restore: skipped a panel of group {GroupId}, its address is not http(s)", saved.GroupId);
			return;
		}

		PreviewTarget? target = null;
		if (item.Kind == PreviewItemKind.File)
		{
			target = await ResolveFileAsync(item, client);
			if (target == null)
				return;

			// The awaits above gave the user time to change things: decide again.
			if (!isDefault)
			{
				group = _settings.Groups.FirstOrDefault(g => g.Id == saved.GroupId);
				if (group == null)
					return;
			}

			if (!pinned && !PanelAdmissible(group, item))
				return;
		}

		_handlingPanel = null;   // its panel comes now and takes its place in the saved list
		var window = CreateWindow(group, pinned, null, panel: true, ownLayout: pinned ? saved.Layout : null, panelId: pinned ? saved.PanelId : null);
		if (window == null)
			return;

		window.Handle.Show(item.Clone(), target, item.Kind == PreviewItemKind.File ? client : null);
		_logger.LogInformation("Restore: reopened a {Kind} panel of group {GroupId}", pinned ? "pinned" : "group", saved.GroupId);
	}

	/// <summary>A group panel comes back only in docked mode, for the session on show when its group closes automatically, and when the group has no panel yet; the default panel in both modes when it has none.</summary>
	private bool PanelAdmissible(PreviewGroupModel? group, PreviewItemModel item)
	{
		if (group == null)
		{
			if (_windows.Any(w => w.IsDefaultPanel))
			{
				_logger.LogInformation("Restore: skipped the default Preview panel, one is already open");
				return false;
			}

			return true;
		}

		if (!IsDocked)
		{
			_logger.LogInformation("Restore: skipped a panel of group {GroupId}, external windows are active", group.Id);
			return false;
		}

		if (group.AutoClose && item.SourceNodeId != (CurrentNodeId ?? _state?.State.LastSelectedNodeId))
		{
			_logger.LogInformation("Restore: skipped a panel of group {GroupId}, it belongs to another session", group.Id);
			return false;
		}

		if (_windows.Any(w => !w.IsPinned && w.IsPanel && w.GroupId == group.Id))
		{
			_logger.LogInformation("Restore: skipped a panel of group {GroupId}, the group already has one", group.Id);
			return false;
		}

		return true;
	}

	/// <summary>A group window comes back only while the mode is active, for the session on show when its group closes automatically, and when the group has no window yet.</summary>
	private bool Admissible(PreviewGroupModel group, PreviewItemModel item)
	{
		if (!IsActive)
		{
			_logger.LogInformation("Restore: skipped a window of group {GroupId}, the mode is off", group.Id);
			return false;
		}

		if (group.AutoClose && item.SourceNodeId != (CurrentNodeId ?? _state?.State.LastSelectedNodeId))
		{
			_logger.LogInformation("Restore: skipped a window of group {GroupId}, it belongs to another session", group.Id);
			return false;
		}

		if (_windows.Any(w => !w.IsPinned && !w.IsPanel && w.GroupId == group.Id))
		{
			_logger.LogInformation("Restore: skipped a window of group {GroupId}, the group already has one", group.Id);
			return false;
		}

		return true;
	}

	/// <summary>The read handle of a saved file: its Focus reference again, or <c>file.preview</c> again; a 1-byte read proves the daemon still serves it.</summary>
	private async Task<PreviewTarget?> ResolveFileAsync(PreviewItemModel item, ITessynClient client)
	{
		var name = item.FileName ?? "file";
		try
		{
			PreviewTarget target;
			if (!string.IsNullOrEmpty(item.RefId))
				target = new PreviewTarget(item.RefId, name);
			else if (item.NodeId != null && item.ItemId != null && item.Path != null)
			{
				var result = await client.FilePreviewForItemAsync(item.NodeId, item.ItemId, item.Path);
				target = new PreviewTarget(result.Handle, string.IsNullOrEmpty(result.Name) ? name : result.Name);
			}
			else if (item.NodeId != null && item.EntryId != null && item.Path != null)
			{
				var result = await client.FilePreviewAsync(item.NodeId, item.EntryId, item.Path);
				target = new PreviewTarget(result.Handle, string.IsNullOrEmpty(result.Name) ? name : result.Name);
			}
			else
			{
				_logger.LogInformation("Restore: skipped {Name}, nothing says how to read it again", name);
				return null;
			}

			await client.FileReadAsync(target.Handle, 0, 1);
			return target;
		}
		catch (Exception ex)
		{
			_logger.LogInformation("Restore: skipped {Name}: {Reason}", name, ex.Message);
			return null;
		}
	}
}
