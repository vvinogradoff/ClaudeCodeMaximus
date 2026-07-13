using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;
using Serilog;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionFileService _fileService;
	private readonly IClaudeProcessManager _processManager;
	private readonly IDraftService _draftService;
	private readonly ICodeIndexService _codeIndexService;
	private readonly IClaudeProfileService _profileService;
	private readonly IClaudeSessionImportService _importService;
	private readonly IClaudeModelService _modelService;
	private readonly IClaudeUsageService _usageService;
	private readonly ISelfUpdateService _selfUpdate;
	private readonly ISessionTurnService _turnService;
	private readonly IAgentMcpServer _mcpServer;
	private readonly ITessynRunService? _runService;
	private readonly ITessynDaemonService? _daemonService;
	private readonly Dictionary<string, SessionViewModel> _sessionCache = new();
	private double _splitterPosition;
	private SessionViewModel? _activeSession;
	private bool _isTreePanelVisible;
	private bool _isDarkTheme;
	private int _selectedLeftTabIndex;
	private string _daemonStatusText = string.Empty;
	private string _daemonStatusColor = "Gray";
	private SessionNodeViewModel? _selectedRecentSession;
	private bool _suppressRecentNav;

	// --- FR.18 Status Bar ---
	private string _statusBarModelText = string.Empty;
	private double _fiveHourUtilization;
	private double _sevenDayUtilization;
	private string _fiveHourLabel = string.Empty;
	private string _sevenDayLabel = string.Empty;
	private bool _hasUsageData;
	private bool _hasActiveProfile;

	public SessionTreeViewModel SessionTree { get; }
	public RecentSessionsViewModel RecentSessions { get; }

	/// <summary>Flat list of recently-active sessions (last 24 h), newest first, for the top-bar dropdown.</summary>
	public ObservableCollection<SessionNodeViewModel> RecentDropdownSessions { get; } = [];

	/// <summary>Currently selected session in the top-bar recent-sessions dropdown.</summary>
	public SessionNodeViewModel? SelectedRecentSession
	{
		get => _selectedRecentSession;
		set
		{
			this.RaiseAndSetIfChanged(ref _selectedRecentSession, value);
			if (_suppressRecentNav || value == null || value == SessionTree.SelectedSession)
				return;
			// Reveal tree and navigate
			IsTreePanelVisible = true;
			SelectedLeftTabIndex = 0;
			SessionTree.SelectedSession = value;
		}
	}

	public SessionViewModel? ActiveSession
	{
		get => _activeSession;
		private set => this.RaiseAndSetIfChanged(ref _activeSession, value);
	}

	public double SplitterPosition
	{
		get => _splitterPosition;
		set
		{
			this.RaiseAndSetIfChanged(ref _splitterPosition, value);
			_appSettings.Settings.Window.SplitterPosition = value;
		}
	}

	/// <summary>Controls tree panel visibility (false = collapsed/auto-hidden).</summary>
	public bool IsTreePanelVisible
	{
		get => _isTreePanelVisible;
		set
		{
			this.RaiseAndSetIfChanged(ref _isTreePanelVisible, value);
			_appSettings.Settings.IsTreePanelCollapsed = !value;
		}
	}

	/// <summary>Selected tab in the left panel (0=Tree, 1=Recent).</summary>
	public int SelectedLeftTabIndex
	{
		get => _selectedLeftTabIndex;
		set
		{
			this.RaiseAndSetIfChanged(ref _selectedLeftTabIndex, value);
			if (value == 1)
				RecentSessions.Refresh();
		}
	}

	/// <summary>True when dark theme is active.</summary>
	public bool IsDarkTheme
	{
		get => _isDarkTheme;
		set
		{
			this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
			_appSettings.Settings.Theme = value ? "Dark" : "Light";
			ThemeApplicator.Apply(_appSettings.Settings);
			_appSettings.Save();
		}
	}

	private bool _isDaemonMissing;
	private string _daemonMissingMessage = string.Empty;

	/// <summary>True when tessyn binary was not found on PATH.</summary>
	public bool IsDaemonMissing
	{
		get => _isDaemonMissing;
		private set => this.RaiseAndSetIfChanged(ref _isDaemonMissing, value);
	}

	/// <summary>User-facing message about missing daemon.</summary>
	public string DaemonMissingMessage
	{
		get => _daemonMissingMessage;
		private set => this.RaiseAndSetIfChanged(ref _daemonMissingMessage, value);
	}

	/// <summary>Called when tessyn binary is not found during startup.</summary>
	public void SetDaemonMissing(string message)
	{
		IsDaemonMissing = true;
		DaemonMissingMessage = message;
		DaemonStatusText = "Not installed";
		DaemonStatusColor = "Red";
	}

	/// <summary>Status text for the Tessyn daemon indicator in the title bar.</summary>
	public string DaemonStatusText
	{
		get => _daemonStatusText;
		private set => this.RaiseAndSetIfChanged(ref _daemonStatusText, value);
	}

	/// <summary>Color name for the daemon status dot (Green, Orange, Red, Gray).</summary>
	public string DaemonStatusColor
	{
		get => _daemonStatusColor;
		private set => this.RaiseAndSetIfChanged(ref _daemonStatusColor, value);
	}

	/// <summary>Whether daemon status indicator should be visible.</summary>
	public bool IsDaemonStatusVisible => _daemonService != null && _appSettings.Settings.UseTessynDaemon;

	// --- FR.11 instruction toolbar forwarding properties ---

	/// <summary>Whether any session is selected (used to enable/disable toolbar buttons).</summary>
	public bool HasActiveSession => ActiveSession is not null;

	public bool IsAutoCommit
	{
		get => ActiveSession?.IsAutoCommit ?? false;
		set { if (ActiveSession is not null) ActiveSession.IsAutoCommit = value; }
	}

	public bool IsNewBranch
	{
		get => ActiveSession?.IsNewBranch ?? false;
		set { if (ActiveSession is not null) ActiveSession.IsNewBranch = value; }
	}

	public bool IsAutoDocument
	{
		get => ActiveSession?.IsAutoDocument ?? false;
		set { if (ActiveSession is not null) ActiveSession.IsAutoDocument = value; }
	}

	public bool IsAutoCompact
	{
		get => ActiveSession?.IsAutoCompact ?? false;
		set { if (ActiveSession is not null) ActiveSession.IsAutoCompact = value; }
	}

	public bool CanClear => ActiveSession?.CanClear ?? false;

	// --- FR.18 Status Bar properties ---

	/// <summary>Model ID + pricing shown in the left portion of the status bar.</summary>
	public string StatusBarModelText
	{
		get => _statusBarModelText;
		private set => this.RaiseAndSetIfChanged(ref _statusBarModelText, value);
	}

	/// <summary>5-hour window utilisation percentage (0–100). Drives the top progress bar.</summary>
	public double FiveHourUtilization
	{
		get => _fiveHourUtilization;
		private set => this.RaiseAndSetIfChanged(ref _fiveHourUtilization, value);
	}

	/// <summary>7-day window utilisation percentage (0–100). Drives the bottom progress bar.</summary>
	public double SevenDayUtilization
	{
		get => _sevenDayUtilization;
		private set => this.RaiseAndSetIfChanged(ref _sevenDayUtilization, value);
	}

	/// <summary>Text overlaid on the 5-hour progress bar (e.g. "5h: 42%  resets 10:49").</summary>
	public string FiveHourLabel
	{
		get => _fiveHourLabel;
		private set => this.RaiseAndSetIfChanged(ref _fiveHourLabel, value);
	}

	/// <summary>Text overlaid on the 7-day progress bar (e.g. "7d: 4%  resets Thu").</summary>
	public string SevenDayLabel
	{
		get => _sevenDayLabel;
		private set => this.RaiseAndSetIfChanged(ref _sevenDayLabel, value);
	}

	/// <summary>Whether usage bars should be visible (false when no data has been fetched yet).</summary>
	public bool HasUsageData
	{
		get => _hasUsageData;
		private set => this.RaiseAndSetIfChanged(ref _hasUsageData, value);
	}

	public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
	public ReactiveCommand<Unit, Unit> ExitCommand { get; }
	public ReactiveCommand<Unit, Unit> ToggleTreePanelCommand { get; }
	public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

	/// <summary>True when the app is running from build output — shows warning icon in title bar.</summary>
	public bool IsRunningFromBuildOutput => _selfUpdate.IsRunningFromBuildOutput;

	public MainWindowViewModel(
		IAppSettingsService appSettings,
		ISessionFileService fileService,
		IClaudeProcessManager processManager,
		IDraftService draftService,
		ICodeIndexService codeIndexService,
		IClaudeProfileService profileService,
		IClaudeSessionImportService importService,
		IClaudeModelService modelService,
		IClaudeUsageService usageService,
		ISelfUpdateService selfUpdate,
		IDirectoryLabelService labelService,
		SessionTreeViewModel sessionTree,
		ISessionTurnService turnService,
		IAgentMcpServer mcpServer,
		ITessynRunService? runService = null,
		ITessynDaemonService? daemonService = null)
	{
		_appSettings      = appSettings;
		_fileService      = fileService;
		_processManager   = processManager;
		_draftService     = draftService;
		_codeIndexService = codeIndexService;
		_profileService   = profileService;
		_importService    = importService;
		_modelService     = modelService;
		_usageService     = usageService;
		_selfUpdate       = selfUpdate;
		_turnService      = turnService;
		_mcpServer        = mcpServer;
		_runService       = runService;
		_daemonService    = daemonService;
		SessionTree       = sessionTree;
		RecentSessions    = new RecentSessionsViewModel(sessionTree, labelService);
		_splitterPosition = appSettings.Settings.Window.SplitterPosition;
		_isTreePanelVisible = !appSettings.Settings.IsTreePanelCollapsed;
		_isDarkTheme = appSettings.Settings.Theme == "Dark";

		OpenSettingsCommand    = ReactiveCommand.Create(OpenSettings);
		ExitCommand            = ReactiveCommand.Create(Exit);
		ToggleTreePanelCommand = ReactiveCommand.Create(() => { IsTreePanelVisible = !IsTreePanelVisible; });
		ToggleThemeCommand     = ReactiveCommand.Create(() => { IsDarkTheme = !IsDarkTheme; });

		// FR.18 — update model text in status bar whenever the model list refreshes
		_modelService.ModelsUpdated += (_, _) =>
			Dispatcher.UIThread.Post(UpdateStatusBarModel);

		// FR.18 — update usage bars whenever a fresh fetch arrives
		_usageService.UsageUpdated += (_, _) =>
			Dispatcher.UIThread.Post(() => UpdateStatusBarUsage(_usageService.CachedUsage));

		// Repair session files corrupted by the auto-compaction bug (one-time on startup)
		var repaired = fileService.RepairCorruptedCompactions();
		if (repaired > 0)
			Serilog.Log.Information("Repaired {Count} session file(s) with corrupted compaction format", repaired);

		// React to session selection changes
		this.WhenAnyValue(x => x.SessionTree.SelectedSession)
			.Subscribe(OnSelectedSessionChanged);

		// FR.18 — when the profile or model dropdown changes inside the active session, update status bar.
		// Skip(1) avoids a duplicate call with OnSelectedSessionChanged which handles the initial load.
		this.WhenAnyValue(x => x.ActiveSession)
			.Select(session => session == null
				? Observable.Empty<int>()
				: session.WhenAnyValue(s => s.SelectedProfileIndex).Skip(1))
			.Switch()
			.Subscribe(_ =>
			{
				var s = ActiveSession;
				if (s != null)
					_usageService.SetActiveProfile(ResolveCredentialsPath(s.SelectedProfileConfigDir));
			});

		this.WhenAnyValue(x => x.ActiveSession)
			.Select(session => session == null
				? Observable.Empty<int>()
				: session.WhenAnyValue(s => s.SelectedModelIndex).Skip(1))
			.Switch()
			.Subscribe(_ => UpdateStatusBarModel());

		// When a session is selected from the Recent tab, sync it to the tree
		this.WhenAnyValue(x => x.RecentSessions.SelectedSession)
			.Subscribe(node =>
			{
				if (node != null)
					SessionTree.SelectedSession = node;
			});

		// Subscribe to daemon state changes for status indicator
		if (_daemonService != null)
		{
			_daemonService.ConnectionStateChanged += (_, state) =>
			{
				Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateDaemonStatus(state, _daemonService.Readiness));
			};
			_daemonService.ReadinessChanged += (_, readiness) =>
			{
				Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateDaemonStatus(_daemonService.ConnectionState, readiness));
			};
			UpdateDaemonStatus(_daemonService.ConnectionState, _daemonService.Readiness);
		}

		// Populate the top-bar recent-sessions dropdown on startup
		RefreshRecentDropdown();
	}

	private void UpdateDaemonStatus(TessynConnectionState connection, TessynDaemonReadiness readiness)
	{
		(DaemonStatusText, DaemonStatusColor) = connection switch
		{
			TessynConnectionState.Disconnected => ("Disconnected", "Red"),
			TessynConnectionState.Connecting => ("Connecting...", "Orange"),
			TessynConnectionState.Reconnecting => ("Reconnecting...", "Orange"),
			TessynConnectionState.Connected => readiness switch
			{
				TessynDaemonReadiness.Ready => ($"Tessyn: {_daemonService?.LastStatus?.SessionsIndexed ?? 0} sessions", "Green"),
				TessynDaemonReadiness.Scanning => ("Indexing...", "Orange"),
				TessynDaemonReadiness.Cold => ("Starting...", "Orange"),
				TessynDaemonReadiness.Degraded => ("Degraded", "Orange"),
				_ => ("Connected", "Green"),
			},
			_ => ("Unknown", "Gray"),
		};
	}

	private void OnSelectedSessionChanged(SessionNodeViewModel? node)
	{
		if (node == null)
		{
			ActiveSession = null;
			_suppressRecentNav = true;
			try { _selectedRecentSession = null; this.RaisePropertyChanged(nameof(SelectedRecentSession)); }
			finally { _suppressRecentNav = false; }
			_hasActiveProfile = false;
			HasUsageData = false;
			_usageService.SetActiveProfile(null);
			StatusBarModelText = string.Empty;
			_appSettings.Settings.ActiveSessionFileName = null;
			_appSettings.Settings.ActiveSessionExternalId = null;
			RaiseInstructionToolbarChanged();
			return;
		}

		// Pre-warm Ollama discovery once (no-op after first call)
		_ = _modelService.EnsureModelsLoadedAsync();

		var cacheKey = node.SessionKey;
		if (!_sessionCache.TryGetValue(cacheKey, out var vm))
		{
			// Check if the VM is cached under the old FileName key (ExternalId was set after caching)
			if (node.ExternalId != null && _sessionCache.TryGetValue(node.FileName, out vm))
			{
				_sessionCache.Remove(node.FileName);
				_sessionCache[cacheKey] = vm;
			}
			else
			{
				vm = new SessionViewModel(node, _fileService, _processManager, _appSettings, _draftService, _codeIndexService, _profileService, _importService, _modelService, _turnService, _mcpServer, _runService, _daemonService);
				if (_appSettings.Settings.UseTessynDaemon && _daemonService != null && node.ExternalId != null)
					_ = vm.LoadFromDaemonAsync();
				else
					vm.LoadFromFile();
				vm.ResolveDefaultProfileEmail();
				_sessionCache[cacheKey] = vm;
			}
		}

		// Compute and set location info for the session header
		var (dirLabel, treePath) = SessionTree.GetSessionLocation(node);
		vm.ProjectDirectory = dirLabel;
		vm.TreePath = treePath;

		ActiveSession = vm;

		// Sync the top-bar dropdown to follow whatever session is now active
		_suppressRecentNav = true;
		try
		{
			_selectedRecentSession = RecentDropdownSessions.Contains(node) ? node : null;
			this.RaisePropertyChanged(nameof(SelectedRecentSession));
		}
		finally
		{
			_suppressRecentNav = false;
		}

		// Restore last-used profile/model/effort for this session before updating the status bar,
		// so UpdateStatusBarModel and SetActiveProfile pick up the restored (per-session) values.
		vm.RestoreLastUsedSettings();

		// FR.18 — update status bar for the newly active session; show zero bars immediately
		UpdateStatusBarModel();
		_hasActiveProfile = true;
		FiveHourUtilization = 0;
		SevenDayUtilization = 0;
		FiveHourLabel = "5h: 0%";
		SevenDayLabel = "7d: 0%";
		HasUsageData = true;
		_usageService.SetActiveProfile(ResolveCredentialsPath(vm.SelectedProfileConfigDir));

		_appSettings.Settings.ActiveSessionFileName = node.FileName;
		_appSettings.Settings.ActiveSessionExternalId = node.ExternalId;
		RaiseInstructionToolbarChanged();
	}

	/// <summary>Builds the status-bar model text from the active session's selected model (FR.18.2).</summary>
	private void UpdateStatusBarModel()
	{
		var session = ActiveSession;
		if (session == null) { StatusBarModelText = string.Empty; return; }

		var modelId = session.SelectedModelId;
		if (string.IsNullOrEmpty(modelId)) { StatusBarModelText = string.Empty; return; }

		var info = _modelService.GetCachedModels().FirstOrDefault(m => m.Id == modelId);

		if (info?.Provider == ModelProvider.Ollama)
		{
			StatusBarModelText = $"{modelId}  ·  local";
			return;
		}

		if (info != null && (info.InputPricePerMillion > 0 || info.OutputPricePerMillion > 0))
			StatusBarModelText = $"{modelId}  ·  in ${info.InputPricePerMillion:0.00} / out ${info.OutputPricePerMillion:0.00} per 1M";
		else
			StatusBarModelText = modelId;
	}

	/// <summary>Updates the status-bar usage bars from a freshly-fetched snapshot (FR.18.3).</summary>
	private void UpdateStatusBarUsage(ClaudeUsageData? usage)
	{
		if (!_hasActiveProfile) { HasUsageData = false; return; }

		HasUsageData        = true;
		FiveHourUtilization = usage?.FiveHourUtilization ?? 0;
		SevenDayUtilization = usage?.SevenDayUtilization ?? 0;

		if (usage == null)
		{
			FiveHourLabel = "5h: 0%";
			SevenDayLabel = "7d: 0%";
			return;
		}

		var fiveReset  = usage.FiveHourResetsAt.ToLocalTime();
		var sevenReset = usage.SevenDayResetsAt.ToLocalTime();

		FiveHourLabel = $"5h: {usage.FiveHourUtilization:0}%  resets {fiveReset:HH:mm}";
		SevenDayLabel = $"7d: {usage.SevenDayUtilization:0}%  resets {sevenReset:ddd}";
	}

	/// <summary>
	/// Returns the path to the .credentials.json file for the given profile config directory.
	/// Falls back to ~/.claude/.credentials.json for the system-wide default profile.
	/// </summary>
	private static string ResolveCredentialsPath(string? profileConfigDir)
	{
		if (!string.IsNullOrEmpty(profileConfigDir))
			return Path.Combine(profileConfigDir, Constants.Usage.CredentialsFileName);

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return Path.Combine(home, Constants.Usage.DefaultClaudeRelativePath, Constants.Usage.CredentialsFileName);
	}

	private void RaiseInstructionToolbarChanged()
	{
		this.RaisePropertyChanged(nameof(HasActiveSession));
		this.RaisePropertyChanged(nameof(IsAutoCommit));
		this.RaisePropertyChanged(nameof(IsNewBranch));
		this.RaisePropertyChanged(nameof(IsAutoDocument));
		this.RaisePropertyChanged(nameof(IsAutoCompact));
		this.RaisePropertyChanged(nameof(CanClear));
	}

	/// <summary>Immediately detaches the JSONL session for the active session (FR.11.7).</summary>
	public void DetachActiveSession() => ActiveSession?.DetachSession();

	public int ActiveSessionCount => _processManager.ActiveProcessCount;

	public void TerminateAllSessions() => _processManager.TerminateAll();

	private void OpenSettings()
	{
		var vm     = new SettingsViewModel(_appSettings);
		var window = new Views.SettingsWindow { DataContext = vm };
		window.Closed += (_, _) =>
		{
			// Sync title bar theme toggle with settings change (no re-apply needed)
			_isDarkTheme = _appSettings.Settings.Theme == "Dark";
			this.RaisePropertyChanged(nameof(IsDarkTheme));
		};
		window.Show();
	}

	private static void Exit()
	{
		if (Avalonia.Application.Current?.ApplicationLifetime is
			Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
			lt.Shutdown();
	}

	public void RestoreActiveSession()
	{
		// Try ExternalId first (new identity), fall back to FileName (legacy)
		var savedExternalId = _appSettings.Settings.ActiveSessionExternalId;
		var savedFileName = _appSettings.Settings.ActiveSessionFileName;

		SessionNodeViewModel? node = null;

		if (!string.IsNullOrEmpty(savedExternalId))
		{
			Log.Debug("RestoreActiveSession: looking for ExternalId {ExternalId}", savedExternalId);
			node = FindSessionByPredicate(s => s.ExternalId == savedExternalId);
		}

		if (node == null && !string.IsNullOrEmpty(savedFileName))
		{
			Log.Debug("RestoreActiveSession: falling back to FileName {FileName}", savedFileName);
			node = FindSessionByPredicate(s => s.FileName == savedFileName);
		}

		if (node != null)
		{
			Log.Debug("RestoreActiveSession: found node '{Name}', setting selection", node.Name);
			SessionTree.SelectedSession = node;
		}
		else if (!string.IsNullOrEmpty(savedExternalId) || !string.IsNullOrEmpty(savedFileName))
		{
			Log.Warning("RestoreActiveSession: session node not found for ExternalId={ExternalId}, FileName={FileName}",
				savedExternalId, savedFileName);
		}
	}

	/// <summary>
	/// Navigates to the session identified by <paramref name="nodeId"/> (FR.16 — toast click routing).
	/// Must be called on the UI thread.
	/// </summary>
	public void SelectSessionByNodeId(string nodeId)
	{
		var node = SessionTree.FindNodeVmByNodeId(nodeId);
		if (node != null)
			SessionTree.SelectedSession = node;
	}

	/// <summary>Rebuilds the top-bar recent-sessions dropdown from sessions active in the last 24 h.</summary>
	public void RefreshRecentDropdown()
	{
		var all = new List<SessionNodeViewModel>();
		foreach (var dir in SessionTree.Directories)
			CollectRecentSessions(dir.Children, all);

		all = all
			.Where(s => s.IsRecentlyActive)
			.OrderByDescending(s => s.LastPromptTimestamp)
			.ToList();

		_suppressRecentNav = true;
		try
		{
			RecentDropdownSessions.Clear();
			foreach (var s in all)
				RecentDropdownSessions.Add(s);

			var current = SessionTree.SelectedSession;
			_selectedRecentSession = current != null && RecentDropdownSessions.Contains(current) ? current : null;
			this.RaisePropertyChanged(nameof(SelectedRecentSession));
		}
		finally
		{
			_suppressRecentNav = false;
		}
	}

	private static void CollectRecentSessions(ObservableCollection<ViewModelBase> children, List<SessionNodeViewModel> result)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session)
				result.Add(session);
			else if (child is GroupNodeViewModel group)
				CollectRecentSessions(group.Children, result);
		}
	}

	private SessionNodeViewModel? FindSessionByPredicate(Func<SessionNodeViewModel, bool> predicate)
	{
		foreach (var dir in SessionTree.Directories)
		{
			var found = FindSessionInChildren(dir.Children, predicate);
			if (found != null)
				return found;
		}
		return null;
	}

	private static SessionNodeViewModel? FindSessionInChildren(
		System.Collections.ObjectModel.ObservableCollection<ViewModelBase> children,
		Func<SessionNodeViewModel, bool> predicate)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session && predicate(session))
				return session;
			if (child is GroupNodeViewModel group)
			{
				var found = FindSessionInChildren(group.Children, predicate);
				if (found != null)
					return found;
			}
		}
		return null;
	}
}
