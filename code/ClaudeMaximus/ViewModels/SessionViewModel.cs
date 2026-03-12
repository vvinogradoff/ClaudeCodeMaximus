using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;
using Serilog;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionViewModel : ViewModelBase
{
	private static readonly ILogger _log = Log.ForContext<SessionViewModel>();

	private readonly SessionNodeViewModel _node;
	private readonly ISessionFileService _fileService;
	private readonly IClaudeProcessManager _processManager;
	private readonly IAppSettingsService _appSettings;
	private string _name;
	private string _inputText = string.Empty;
	private bool _isBusy;
	// Counts concurrent in-flight sends; IsBusy = count > 0
	private int _activeCount;

	public string Name
	{
		get => _name;
		private set => this.RaiseAndSetIfChanged(ref _name, value);
	}

	public string InputText
	{
		get => _inputText;
		set => this.RaiseAndSetIfChanged(ref _inputText, value);
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
	}

	public ObservableCollection<MessageEntryViewModel> Messages { get; } = [];

	public ReactiveCommand<Unit, Unit> SendCommand { get; }

	public SessionViewModel(
		SessionNodeViewModel node,
		ISessionFileService fileService,
		IClaudeProcessManager processManager,
		IAppSettingsService appSettings)
	{
		_node           = node;
		_fileService    = fileService;
		_processManager = processManager;
		_appSettings    = appSettings;
		_name           = node.Name;

		node.WhenAnyValue(x => x.Name).Subscribe(n => Name = n);

		// No canSend gate — allow sending at any time, even while a response is in flight
		SendCommand = ReactiveCommand.CreateFromTask(SendAsync);
	}

	public void LoadFromFile()
	{
		var entries = _fileService.ReadEntries(_node.FileName);
		foreach (var entry in entries)
			Messages.Add(EntryToViewModel(entry));
	}

	private async System.Threading.Tasks.Task SendAsync()
	{
		var message = InputText.Trim();
		if (string.IsNullOrEmpty(message))
			return;

		InputText = string.Empty;
		IncrementActive();

		_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleUser, message);
		Dispatcher.UIThread.Post(() => Messages.Add(new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleUser,
			Content   = message,
			Timestamp = DateTimeOffset.UtcNow,
		}));

		try
		{
			await _processManager.SendMessageAsync(
				workingDirectory: _node.Model.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        _node.Model.ClaudeSessionId,
				userMessage:      message,
				onEvent:          HandleStreamEvent);
		}
		finally
		{
			DecrementActive();
		}
	}

	private void HandleStreamEvent(ClaudeStreamEvent evt)
	{
		// File writes happen on the background thread (safe — append + flush)
		switch (evt.Type)
		{
			case "assistant" when evt.Content is not null:
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleAssistant, evt.Content);
				break;
			case "system" when evt.Subtype is "compact":
				_fileService.AppendCompactionSeparator(_node.FileName);
				break;
			case "system" when evt.IsError && evt.Content is not null:
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				break;
			case "result" when evt.SessionId is not null:
				_node.Model.ClaudeSessionId = evt.SessionId;
				_appSettings.Save();
				break;
		}

		// UI updates must be on the UI thread
		Dispatcher.UIThread.Post(() =>
		{
			switch (evt.Type)
			{
				case "assistant" when evt.Content is not null:
					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleAssistant,
						Content   = evt.Content,
						Timestamp = evt.Timestamp,
					});
					break;

				case "system" when evt.Subtype is "compact":
					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleCompaction,
						Content   = string.Empty,
						Timestamp = evt.Timestamp,
					});
					break;

				case "system" when evt.Subtype is "task_progress" or "task_started"
				                   && evt.Content is not null:
					// Live progress: update the last progress entry in-place to avoid flooding
					var last = Messages.Count > 0 ? Messages[^1] : null;
					if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
						last.Content = evt.Content;
					else
						Messages.Add(new MessageEntryViewModel
						{
							Role       = Constants.SessionFile.RoleSystem,
							Content    = evt.Content,
							Timestamp  = evt.Timestamp,
							IsProgress = true,
						});
					break;

				case "system" when evt.IsError && evt.Content is not null:
					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = evt.Content,
						Timestamp = evt.Timestamp,
					});
					break;
			}
		});
	}

	private void IncrementActive()
	{
		var count = Interlocked.Increment(ref _activeCount);
		_log.Debug("Active sends: {Count}", count);
		Dispatcher.UIThread.Post(() => { IsBusy = true; _node.IsRunning = true; });
	}

	private void DecrementActive()
	{
		var count = Interlocked.Decrement(ref _activeCount);
		_log.Debug("Active sends: {Count}", count);
		if (count <= 0)
			Dispatcher.UIThread.Post(() => { IsBusy = false; _node.IsRunning = false; });
	}

	private static MessageEntryViewModel EntryToViewModel(SessionEntryModel entry)
		=> new()
		{
			Role      = entry.Role,
			Content   = entry.Content,
			Timestamp = entry.Timestamp,
		};
}
