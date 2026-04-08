using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
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
	private readonly ISelfUpdateService _selfUpdate;
	private readonly ITessynRunService? _runService;
	private readonly ITessynDaemonService? _daemonService;
	private readonly Dictionary<string, SessionViewModel> _sessionCache = new();
	private double _splitterPosition;
	private SessionViewModel? _activeSession;
	private bool _isTreePanelVisible;
	private bool _isDarkTheme;
	private string _daemonStatusText = string.Empty;
	private string _daemonStatusColor = "Gray";
	private bool _isAuthWarningVisible;
	private string _authWarningMessage = string.Empty;
	private string? _authEmail;

	public SessionTreeViewModel SessionTree { get; }

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

	/// <summary>Whether the "not logged in" warning banner should be visible.</summary>
	public bool IsAuthWarningVisible
	{
		get => _isAuthWarningVisible;
		private set => this.RaiseAndSetIfChanged(ref _isAuthWarningVisible, value);
	}

	/// <summary>Warning message shown when auth check fails.</summary>
	public string AuthWarningMessage
	{
		get => _authWarningMessage;
		private set => this.RaiseAndSetIfChanged(ref _authWarningMessage, value);
	}

	/// <summary>Authenticated email address, null if not logged in.</summary>
	public string? AuthEmail
	{
		get => _authEmail;
		private set => this.RaiseAndSetIfChanged(ref _authEmail, value);
	}

	/// <summary>Called after daemon auth check. Shows or hides the auth warning.</summary>
	public void SetAuthStatus(bool loggedIn, string? email)
	{
		AuthEmail = email;
		IsAuthWarningVisible = !loggedIn;
		AuthWarningMessage = loggedIn
			? string.Empty
			: "Not logged in — run  claude login  in a terminal or type /login in the input box.";
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

	public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
	public ReactiveCommand<Unit, Unit> ExitCommand { get; }
	public ReactiveCommand<Unit, Unit> ToggleTreePanelCommand { get; }
	public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
	public ReactiveCommand<Unit, Unit> ClearSessionCommand { get; }

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
		ISelfUpdateService selfUpdate,
		SessionTreeViewModel sessionTree,
		ITessynRunService runService,
		ITessynDaemonService daemonService)
	{
		_appSettings      = appSettings;
		_fileService      = fileService;
		_processManager   = processManager;
		_draftService     = draftService;
		_codeIndexService = codeIndexService;
		_profileService   = profileService;
		_importService    = importService;
		_selfUpdate       = selfUpdate;
		_runService       = runService;
		_daemonService    = daemonService;
		SessionTree       = sessionTree;
		_splitterPosition = appSettings.Settings.Window.SplitterPosition;
		_isTreePanelVisible = !appSettings.Settings.IsTreePanelCollapsed;
		_isDarkTheme = appSettings.Settings.Theme == "Dark";

		OpenSettingsCommand    = ReactiveCommand.Create(OpenSettings);
		ExitCommand            = ReactiveCommand.Create(Exit);
		ToggleTreePanelCommand = ReactiveCommand.Create(() => { IsTreePanelVisible = !IsTreePanelVisible; });
		ToggleThemeCommand     = ReactiveCommand.Create(() => { IsDarkTheme = !IsDarkTheme; });
		ClearSessionCommand    = ReactiveCommand.Create(() => { ActiveSession?.ClearCommand.Execute().Subscribe(); });

		// Repair session files corrupted by the auto-compaction bug (one-time on startup)
		var repaired = fileService.RepairCorruptedCompactions();
		if (repaired > 0)
			Serilog.Log.Information("Repaired {Count} session file(s) with corrupted compaction format", repaired);

		// React to session selection changes
		this.WhenAnyValue(x => x.SessionTree.SelectedSession)
			.Subscribe(OnSelectedSessionChanged);

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
			_appSettings.Settings.ActiveSessionFileName = null;
			_appSettings.Settings.ActiveSessionExternalId = null;
			RaiseInstructionToolbarChanged();
			return;
		}

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
				vm = new SessionViewModel(node, _fileService, _processManager, _appSettings, _draftService, _codeIndexService, _profileService, _importService, _runService, _daemonService);
				if (_appSettings.Settings.UseTessynDaemon && _daemonService != null && node.ExternalId != null)
					_ = vm.LoadFromDaemonAsync();
				else
					vm.LoadFromFile();
				vm.ResolveDefaultProfileEmail();
				_sessionCache[cacheKey] = vm;
			}
		}

		ActiveSession = vm;
		_appSettings.Settings.ActiveSessionFileName = node.FileName;
		_appSettings.Settings.ActiveSessionExternalId = node.ExternalId;
		RaiseInstructionToolbarChanged();
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
