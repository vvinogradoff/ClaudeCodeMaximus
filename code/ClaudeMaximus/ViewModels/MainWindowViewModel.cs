using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionFileService _fileService;
	private readonly IClaudeProcessManager _processManager;
	private readonly IDraftService _draftService;
	private readonly ICodeIndexService _codeIndexService;
	private readonly Dictionary<string, SessionViewModel> _sessionCache = new();
	private double _splitterPosition;
	private SessionViewModel? _activeSession;
	private bool _isTreePanelVisible;
	private bool _isDarkTheme;

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

	public MainWindowViewModel(
		IAppSettingsService appSettings,
		ISessionFileService fileService,
		IClaudeProcessManager processManager,
		IDraftService draftService,
		ICodeIndexService codeIndexService,
		SessionTreeViewModel sessionTree)
	{
		_appSettings      = appSettings;
		_fileService      = fileService;
		_processManager   = processManager;
		_draftService     = draftService;
		_codeIndexService = codeIndexService;
		SessionTree       = sessionTree;
		_splitterPosition = appSettings.Settings.Window.SplitterPosition;
		_isTreePanelVisible = !appSettings.Settings.IsTreePanelCollapsed;
		_isDarkTheme = appSettings.Settings.Theme == "Dark";

		OpenSettingsCommand    = ReactiveCommand.Create(OpenSettings);
		ExitCommand            = ReactiveCommand.Create(Exit);
		ToggleTreePanelCommand = ReactiveCommand.Create(() => { IsTreePanelVisible = !IsTreePanelVisible; });
		ToggleThemeCommand     = ReactiveCommand.Create(() => { IsDarkTheme = !IsDarkTheme; });
		ClearSessionCommand    = ReactiveCommand.Create(() => { ActiveSession?.ClearCommand.Execute().Subscribe(); });

		// React to session selection changes
		this.WhenAnyValue(x => x.SessionTree.SelectedSession)
			.Subscribe(OnSelectedSessionChanged);

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

		if (!_sessionCache.TryGetValue(node.FileName, out var vm))
		{
			vm = new SessionViewModel(node, _fileService, _processManager, _appSettings, _draftService, _codeIndexService);
			vm.LoadFromFile();
			_sessionCache[node.FileName] = vm;
		}

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
			return;

		var node = FindSessionNode(savedFileName);
		if (node != null)
			SessionTree.SelectedSession = node;
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
