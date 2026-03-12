using System;
using System.Collections.Generic;
using System.Reactive;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionFileService _fileService;
	private readonly IClaudeProcessManager _processManager;
	private readonly Dictionary<string, SessionViewModel> _sessionCache = new();
	private double _splitterPosition;
	private SessionViewModel? _activeSession;

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

	public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
	public ReactiveCommand<Unit, Unit> ExitCommand { get; }

	public MainWindowViewModel(
		IAppSettingsService appSettings,
		ISessionFileService fileService,
		IClaudeProcessManager processManager,
		SessionTreeViewModel sessionTree)
	{
		_appSettings    = appSettings;
		_fileService    = fileService;
		_processManager = processManager;
		SessionTree     = sessionTree;
		_splitterPosition = appSettings.Settings.Window.SplitterPosition;

		OpenSettingsCommand = ReactiveCommand.Create(OpenSettings);
		ExitCommand         = ReactiveCommand.Create(Exit);

		// React to session selection changes
		this.WhenAnyValue(x => x.SessionTree.SelectedSession)
			.Subscribe(OnSelectedSessionChanged);
	}

	private void OnSelectedSessionChanged(SessionNodeViewModel? node)
	{
		if (node == null)
		{
			ActiveSession = null;
			return;
		}

		if (!_sessionCache.TryGetValue(node.FileName, out var vm))
		{
			vm = new SessionViewModel(node, _fileService, _processManager, _appSettings);
			vm.LoadFromFile();
			_sessionCache[node.FileName] = vm;
		}

		ActiveSession = vm;
	}

	public int ActiveSessionCount => _processManager.ActiveProcessCount;

	public void TerminateAllSessions() => _processManager.TerminateAll();

	private void OpenSettings()
	{
		var vm     = new SettingsViewModel(_appSettings);
		var window = new Views.SettingsWindow { DataContext = vm };
		window.Show();
	}

	private static void Exit()
	{
		if (Avalonia.Application.Current?.ApplicationLifetime is
			Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
			lt.Shutdown();
	}
}
