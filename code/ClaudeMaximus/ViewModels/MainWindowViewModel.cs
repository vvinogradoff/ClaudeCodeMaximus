using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
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
	private readonly ISelfUpdateService _selfUpdate;
	private readonly Dictionary<string, SessionViewModel> _sessionCache = new();
	private double _splitterPosition;
	private SessionViewModel? _activeSession;
	private bool _isTreePanelVisible;
	private bool _isDarkTheme;
	private int _selectedLeftTabIndex;

	public SessionTreeViewModel SessionTree { get; }
	public RecentSessionsViewModel RecentSessions { get; }

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
		ISelfUpdateService selfUpdate,
		IDirectoryLabelService labelService,
		SessionTreeViewModel sessionTree)
	{
		_appSettings      = appSettings;
		_fileService      = fileService;
		_processManager   = processManager;
		_draftService     = draftService;
		_codeIndexService = codeIndexService;
		_profileService   = profileService;
		_importService    = importService;
		_modelService     = modelService;
		_selfUpdate       = selfUpdate;
		SessionTree       = sessionTree;
		RecentSessions    = new RecentSessionsViewModel(sessionTree, labelService);
		_splitterPosition = appSettings.Settings.Window.SplitterPosition;
		_isTreePanelVisible = !appSettings.Settings.IsTreePanelCollapsed;
		_isDarkTheme = appSettings.Settings.Theme == "Dark";

		OpenSettingsCommand    = ReactiveCommand.Create(OpenSettings);
		ExitCommand            = ReactiveCommand.Create(Exit);
		ToggleTreePanelCommand = ReactiveCommand.Create(() => { IsTreePanelVisible = !IsTreePanelVisible; });
		ToggleThemeCommand     = ReactiveCommand.Create(() => { IsDarkTheme = !IsDarkTheme; });

		// Repair session files corrupted by the auto-compaction bug (one-time on startup)
		var repaired = fileService.RepairCorruptedCompactions();
		if (repaired > 0)
			Serilog.Log.Information("Repaired {Count} session file(s) with corrupted compaction format", repaired);

		// React to session selection changes
		this.WhenAnyValue(x => x.SessionTree.SelectedSession)
			.Subscribe(OnSelectedSessionChanged);

		// When a session is selected from the Recent tab, sync it to the tree
		this.WhenAnyValue(x => x.RecentSessions.SelectedSession)
			.Subscribe(node =>
			{
				if (node != null)
					SessionTree.SelectedSession = node;
			});

	}

	private void OnSelectedSessionChanged(SessionNodeViewModel? node)
	{
		if (node == null)
		{
			ActiveSession = null;
			_appSettings.Settings.ActiveSessionFileName = null;
			RaiseInstructionToolbarChanged();
			return;
		}

		// Pre-warm model list once (no-op after first call)
		var profileConfigDir = _profileService.GetConfigDirForProfile(
			_appSettings.Settings.SelectedProfileIndex, _appSettings.Settings.Profiles);
		_ = _modelService.EnsureModelsLoadedAsync(_appSettings.Settings.ClaudePath, profileConfigDir);

		if (!_sessionCache.TryGetValue(node.FileName, out var vm))
		{
			vm = new SessionViewModel(node, _fileService, _processManager, _appSettings, _draftService, _codeIndexService, _profileService, _importService, _modelService);
			vm.LoadFromFile();
			vm.ResolveDefaultProfileEmail();
			_sessionCache[node.FileName] = vm;
		}

		// Compute and set location info for the session header
		var (dirLabel, treePath) = SessionTree.GetSessionLocation(node);
		vm.ProjectDirectory = dirLabel;
		vm.TreePath = treePath;

		ActiveSession = vm;
		_appSettings.Settings.ActiveSessionFileName = node.FileName;
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
		var savedFileName = _appSettings.Settings.ActiveSessionFileName;
		if (string.IsNullOrEmpty(savedFileName))
		{
			Log.Debug("RestoreActiveSession: no saved ActiveSessionFileName");
			return;
		}

		Log.Debug("RestoreActiveSession: looking for {FileName} in {DirCount} directories",
			savedFileName, SessionTree.Directories.Count);

		var node = FindSessionNode(savedFileName);
		if (node != null)
		{
			Log.Debug("RestoreActiveSession: found node '{Name}', setting selection", node.Name);
			SessionTree.SelectedSession = node;
		}
		else
		{
			Log.Warning("RestoreActiveSession: session node not found for {FileName}", savedFileName);
		}
	}

	private SessionNodeViewModel? FindSessionNode(string fileName)
	{
		foreach (var dir in SessionTree.Directories)
		{
			var found = FindSessionInChildren(dir.Children, fileName);
			if (found != null)
				return found;
		}
		return null;
	}

	private static SessionNodeViewModel? FindSessionInChildren(
		System.Collections.ObjectModel.ObservableCollection<ViewModelBase> children, string fileName)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session && session.FileName == fileName)
				return session;
			if (child is GroupNodeViewModel group)
			{
				var found = FindSessionInChildren(group.Children, fileName);
				if (found != null)
					return found;
			}
		}
		return null;
	}
}
