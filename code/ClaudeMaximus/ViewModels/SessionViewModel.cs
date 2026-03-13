using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
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
	private readonly IDraftService _draftService;
	private readonly ICodeIndexService _codeIndexService;
	private string _name;
	private string _inputText = string.Empty;
	private bool _isBusy;
	private bool _isMarkdownMode = true;
	private string _thinkingDuration = string.Empty;
	private DispatcherTimer? _thinkingTimer;
	private DateTimeOffset _thinkingStartedAt;
	private int _busyCount;
	private bool _needsContextRetry;
	private DispatcherTimer? _draftDebounceTimer;

	public string Name
	{
		get => _name;
		private set => this.RaiseAndSetIfChanged(ref _name, value);
	}

	public string InputText
	{
		get => _inputText;
		set
		{
			this.RaiseAndSetIfChanged(ref _inputText, value);
			SaveDraft(value);
		}
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
	}

	public string ThinkingDuration
	{
		get => _thinkingDuration;
		private set => this.RaiseAndSetIfChanged(ref _thinkingDuration, value);
	}

	public bool IsMarkdownMode
	{
		get => _isMarkdownMode;
		set => this.RaiseAndSetIfChanged(ref _isMarkdownMode, value);
	}

	public double AssistantFontSize
	{
		get => _appSettings.Settings.AssistantFontSize;
		set
		{
			_appSettings.Settings.AssistantFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	public double AssistantMarkdownFontSize
	{
		get => _appSettings.Settings.AssistantMarkdownFontSize;
		set
		{
			_appSettings.Settings.AssistantMarkdownFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	public double UserFontSize
	{
		get => _appSettings.Settings.UserFontSize;
		set
		{
			_appSettings.Settings.UserFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	public double InputFontSize
	{
		get => _appSettings.Settings.InputFontSize;
		set
		{
			_appSettings.Settings.InputFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	/// <summary>Persisted vertical scroll offset for the message area.</summary>
	public double ScrollOffset
	{
		get => _node.Model.ScrollOffset;
		set => _node.Model.ScrollOffset = value;
	}

	public ObservableCollection<MessageEntryViewModel> Messages { get; } = [];

	public ReactiveCommand<Unit, Unit> SendCommand { get; }
	public ReactiveCommand<Unit, Unit> ToggleMarkdownCommand { get; }
	public AutocompleteViewModel AutocompleteVm { get; }
	public string WorkingDirectory => _node.Model.WorkingDirectory;

	public SessionViewModel(
		SessionNodeViewModel node,
		ISessionFileService fileService,
		IClaudeProcessManager processManager,
		IAppSettingsService appSettings,
		IDraftService draftService,
		ICodeIndexService codeIndexService)
	{
		_node             = node;
		_fileService      = fileService;
		_processManager   = processManager;
		_appSettings      = appSettings;
		_draftService     = draftService;
		_codeIndexService = codeIndexService;
		_name             = node.Name;
		AutocompleteVm    = new AutocompleteViewModel(codeIndexService);

		node.WhenAnyValue(x => x.Name).Subscribe(n => Name = n);

		SendCommand           = ReactiveCommand.Create(() => { _ = SendAsync(); });
		ToggleMarkdownCommand = ReactiveCommand.Create(() => { IsMarkdownMode = !IsMarkdownMode; });

		// Start background indexing for this session's working directory
		if (!string.IsNullOrWhiteSpace(WorkingDirectory))
			_ = codeIndexService.GetOrCreateIndexAsync(WorkingDirectory);
	}

	public void LoadFromFile()
	{
		var entries = _fileService.ReadEntries(_node.FileName);
		foreach (var entry in entries)
		{
			if (entry.Role != Constants.SessionFile.RoleCompaction
			    && string.IsNullOrWhiteSpace(entry.Content))
				continue;

			Messages.Add(EntryToViewModel(entry));
		}

		var draft = _draftService.LoadDraft(_node.FileName);
		if (draft is not null)
			_inputText = draft;
	}

	private async System.Threading.Tasks.Task SendAsync()
	{
		var message = InputText.Trim();
		if (string.IsNullOrEmpty(message))
			return;

		InputText = string.Empty;
		_draftService.DeleteDraft(_node.FileName);

		_busyCount++;
		IsBusy = true;
		_node.IsRunning = true;

		// Start timer only for the first concurrent send; subsequent sends keep the running clock
		if (_thinkingTimer == null)
		{
			_thinkingStartedAt = DateTimeOffset.UtcNow;
			ThinkingDuration = "0:00";
			_thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			_thinkingTimer.Tick += OnThinkingTimerTick;
			_thinkingTimer.Start();
		}

		_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleUser, message);
		var now = DateTimeOffset.UtcNow;
		Messages.Add(new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleUser,
			Content   = message,
			Timestamp = now,
		});
		_node.LastPromptTime = now.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

		Messages.Add(new MessageEntryViewModel
		{
			Role       = Constants.SessionFile.RoleSystem,
			Content    = "Claude is thinking...",
			Timestamp  = DateTimeOffset.UtcNow,
			IsProgress = true,
		});

		try
		{
			await _processManager.SendMessageAsync(
				workingDirectory: _node.Model.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        _node.Model.ClaudeSessionId,
				userMessage:      message,
				onEvent:          HandleStreamEvent);

			if (_needsContextRetry)
			{
				_needsContextRetry = false;
				var enrichedMessage = BuildContextPreamble(message);

				Dispatcher.UIThread.Post(() =>
				{
					var last = Messages.Count > 0 ? Messages[^1] : null;
					if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
						last.Content = "Resuming session with conversation history...";
					else
						Messages.Add(new MessageEntryViewModel
						{
							Role       = Constants.SessionFile.RoleSystem,
							Content    = "Resuming session with conversation history...",
							Timestamp  = DateTimeOffset.UtcNow,
							IsProgress = true,
						});
				});

				await _processManager.SendMessageAsync(
					workingDirectory: _node.Model.WorkingDirectory,
					claudePath:       _appSettings.Settings.ClaudePath,
					sessionId:        null,
					userMessage:      enrichedMessage,
					onEvent:          HandleStreamEvent);
			}
		}
		finally
		{
			_busyCount = Math.Max(0, _busyCount - 1);
			if (_busyCount == 0)
			{
				var t = _thinkingTimer;
				_thinkingTimer = null;
				t?.Stop();
				ThinkingDuration = string.Empty;
				IsBusy = false;
				_node.IsRunning = false;
			}
		}
	}

	private void HandleStreamEvent(ClaudeStreamEvent evt)
	{
		switch (evt.Type)
		{
			case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleAssistant, evt.Content);
				break;
			case "system" when evt.Subtype is "compact":
				_fileService.AppendCompactionSeparator(_node.FileName);
				break;
			case "system" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				// When context retry is pending, suppress stderr-based errors (they echo the
				// same "No conversation found" that the result event already handled).
				if (_needsContextRetry)
					return;
				if (evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase))
				{
					_log.Information("No conversation found (system error) for session {FileName} — will retry with context", _node.FileName);
					_node.Model.ClaudeSessionId = null;
					_appSettings.Save();
					_needsContextRetry = true;
					return;
				}
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				break;
			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase):
				// "No conversation found" — transient infrastructure error.
				// Set flag for auto-retry with context preamble; skip file write and UI post.
				_log.Information("No conversation found for session {FileName} — will retry with context", _node.FileName);
				_node.Model.ClaudeSessionId = null;
				_appSettings.Save();
				_needsContextRetry = true;
				return;
			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				_log.Warning("Claude result error: {Error}", evt.Content);
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				break;
			case "result" when !evt.IsError && evt.SessionId is not null:
				_node.Model.ClaudeSessionId = evt.SessionId;
				_appSettings.Save();
				break;
		}

		Dispatcher.UIThread.Post(() =>
		{
			switch (evt.Type)
			{
				case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
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
				                   && !string.IsNullOrWhiteSpace(evt.Content):
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

				case "system" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = evt.Content,
						Timestamp = evt.Timestamp,
					});
					break;

				case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
					for (var i = Messages.Count - 1; i >= 0; i--)
					{
						if (Messages[i].IsProgress)
							Messages.RemoveAt(i);
					}
					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = evt.Content,
						Timestamp = evt.Timestamp,
					});
					break;

				case "result":
					for (var i = Messages.Count - 1; i >= 0; i--)
					{
						if (Messages[i].IsProgress)
							Messages.RemoveAt(i);
					}
					break;
			}
		});
	}

	private void SaveDraft(string text)
	{
		_draftDebounceTimer?.Stop();
		_draftDebounceTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(Constants.DraftDebounceMilliseconds)
		};
		_draftDebounceTimer.Tick += (_, _) =>
		{
			_draftDebounceTimer?.Stop();
			_draftDebounceTimer = null;
			if (string.IsNullOrEmpty(text))
				_draftService.DeleteDraft(_node.FileName);
			else
				_draftService.SaveDraft(_node.FileName, text);
		};
		_draftDebounceTimer.Start();
	}

	private void OnThinkingTimerTick(object? sender, EventArgs e)
	{
		var elapsed = DateTimeOffset.UtcNow - _thinkingStartedAt;
		ThinkingDuration = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
	}

	private string BuildContextPreamble(string currentMessage)
	{
		var entries = _fileService.ReadEntries(_node.FileName);
		var conversationEntries = entries
			.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
			.ToList();

		if (conversationEntries.Count == 0)
			return currentMessage;

		var sb = new StringBuilder();
		sb.AppendLine("The following is the conversation history from a previous session that is no longer available. Use it as context for continuity:");
		sb.AppendLine("---");

		foreach (var entry in conversationEntries)
		{
			var roleLabel = entry.Role == Constants.SessionFile.RoleUser ? "Human" : "Assistant";
			sb.AppendLine($"[{roleLabel}]: {entry.Content}");
			sb.AppendLine();
		}

		sb.AppendLine("---");
		sb.AppendLine("Now, continuing the conversation:");
		sb.AppendLine(currentMessage);

		return sb.ToString();
	}

	private static MessageEntryViewModel EntryToViewModel(SessionEntryModel entry)
		=> new()
		{
			Role      = entry.Role,
			Content   = entry.Content,
			Timestamp = entry.Timestamp,
		};
}
