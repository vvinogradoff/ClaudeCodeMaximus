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
	private bool _pendingClear;
	private bool _isNewBranch;
	private bool _isAutoCompact;
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

	/// <summary>Per-session sticky toggle (FR.11.3). Persisted in appsettings.json.</summary>
	public bool IsAutoCommit
	{
		get => _node.Model.IsAutoCommit;
		set
		{
			_node.Model.IsAutoCommit = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	/// <summary>One-shot toggle (FR.11.4). Auto-resets after prompt sent.</summary>
	public bool IsNewBranch
	{
		get => _isNewBranch;
		set => this.RaiseAndSetIfChanged(ref _isNewBranch, value);
	}

	/// <summary>Per-session sticky toggle (FR.11.5). Persisted in appsettings.json.</summary>
	public bool IsAutoDocument
	{
		get => _node.Model.IsAutoDocument;
		set
		{
			_node.Model.IsAutoDocument = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	/// <summary>One-shot toggle (FR.11.6). Auto-resets after compaction completes.</summary>
	public bool IsAutoCompact
	{
		get => _isAutoCompact;
		set => this.RaiseAndSetIfChanged(ref _isAutoCompact, value);
	}

	/// <summary>True when the session has a live ClaudeSessionId that can be cleared.</summary>
	public bool CanClear => _node.Model.ClaudeSessionId is not null;

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
	public ReactiveCommand<Unit, Unit> ClearCommand { get; }
	public AutocompleteViewModel AutocompleteVm { get; }
	public OutputSearchViewModel OutputSearchVm { get; }
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
		OutputSearchVm    = new OutputSearchViewModel(Messages);

		node.WhenAnyValue(x => x.Name).Subscribe(n => Name = n);

		SendCommand           = ReactiveCommand.Create(() => { _ = SendAsync(); });
		ToggleMarkdownCommand = ReactiveCommand.Create(() => { IsMarkdownMode = !IsMarkdownMode; });
		ClearCommand          = ReactiveCommand.Create(() => { _pendingClear = true; });

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

		// Capture one-shot toggle states before resetting them
		var wasNewBranch = _isNewBranch;
		var wasAutoCompact = _isAutoCompact;
		var wasPendingClear = _pendingClear;

		// Build augmented message with hidden instructions (FR.11.2, FR.11.9)
		var instructionBlock = BuildInstructionBlock();
		var augmentedMessage = message + instructionBlock;

		// Reset one-shot toggles immediately
		if (wasNewBranch) IsNewBranch = false;
		_pendingClear = false;

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

		// Store only the clean user message in file and UI (FR.11.2)
		_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleUser, message);
		var now = DateTimeOffset.UtcNow;
		Messages.Add(new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleUser,
			Content   = message,
			Timestamp = now,
		});
		_node.LastPromptTime = now.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
		_node.LastPromptTimestamp = now;

		Messages.Add(new MessageEntryViewModel
		{
			Role       = Constants.SessionFile.RoleSystem,
			Content    = "Claude is thinking...",
			Timestamp  = DateTimeOffset.UtcNow,
			IsProgress = true,
		});

		// Proactive context reload (FR.11.10): if file has history but no session ID, wrap with context
		var sessionId = _node.Model.ClaudeSessionId;
		var messageToSend = augmentedMessage;
		if (sessionId == null)
		{
			var entries = _fileService.ReadEntries(_node.FileName);
			var hasHistory = entries.Any(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant);
			// Exclude the message we just appended (last USER entry is the current prompt)
			var priorEntries = entries
				.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
				.ToList();
			if (priorEntries.Count > 1) // More than just the current prompt
			{
				messageToSend = BuildContextPreamble(augmentedMessage);
				_log.Information("Proactive context reload for session {FileName}", _node.FileName);
			}
		}

		try
		{
			await _processManager.SendMessageAsync(
				workingDirectory: _node.Model.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        sessionId,
				userMessage:      messageToSend,
				onEvent:          HandleStreamEvent);

			if (_needsContextRetry)
			{
				_needsContextRetry = false;
				var enrichedMessage = BuildContextPreamble(augmentedMessage);

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

			// Post-response: handle Clear (FR.11.7)
			if (wasPendingClear)
			{
				_log.Information("Clearing Claude session for {FileName}", _node.FileName);
				_node.Model.ClaudeSessionId = null;
				_appSettings.Save();
				Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(CanClear)));
			}

			// Post-response: handle Auto-Compact (FR.11.6)
			if (wasAutoCompact)
			{
				await SendCompactionPromptAsync();
				IsAutoCompact = false;
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

	/// <summary>Sends a follow-up compaction prompt and rewrites the session file (FR.11.6).</summary>
	private async System.Threading.Tasks.Task SendCompactionPromptAsync()
	{
		_log.Information("Starting auto-compaction for session {FileName}", _node.FileName);

		Dispatcher.UIThread.Post(() =>
		{
			Messages.Add(new MessageEntryViewModel
			{
				Role       = Constants.SessionFile.RoleSystem,
				Content    = "Compacting session...",
				Timestamp  = DateTimeOffset.UtcNow,
				IsProgress = true,
			});
		});

		var compactedContent = new StringBuilder();

		await _processManager.SendMessageAsync(
			workingDirectory: _node.Model.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        _node.Model.ClaudeSessionId,
			userMessage:      Constants.Instructions.CompactionPrompt,
			onEvent:          evt =>
			{
				if (evt.Type == "assistant" && !string.IsNullOrWhiteSpace(evt.Content))
					compactedContent.AppendLine(evt.Content);

				if (evt.Type == "result" && !evt.IsError && evt.SessionId is not null)
				{
					_node.Model.ClaudeSessionId = evt.SessionId;
					_appSettings.Save();
				}
			});

		var compacted = compactedContent.ToString().Trim();
		if (!string.IsNullOrEmpty(compacted))
		{
			_fileService.RewriteSessionFile(_node.FileName, compacted);

			Dispatcher.UIThread.Post(() =>
			{
				Messages.Clear();
				var entries = _fileService.ReadEntries(_node.FileName);
				foreach (var entry in entries)
				{
					if (entry.Role != Constants.SessionFile.RoleCompaction
					    && string.IsNullOrWhiteSpace(entry.Content))
						continue;
					Messages.Add(EntryToViewModel(entry));
				}
			});

			_log.Information("Session {FileName} compacted successfully", _node.FileName);
		}
		else
		{
			_log.Warning("Compaction returned empty content for session {FileName}; keeping original", _node.FileName);
			Dispatcher.UIThread.Post(() =>
			{
				for (var i = Messages.Count - 1; i >= 0; i--)
				{
					if (Messages[i].IsProgress) Messages.RemoveAt(i);
				}
			});
		}
	}

	/// <summary>Builds the hidden instruction block appended to the user's message for claude stdin (FR.11.9).</summary>
	private string BuildInstructionBlock()
	{
		var sb = new StringBuilder();
		sb.AppendLine(Constants.Instructions.Delimiter);

		// Auto-commit: always inject (ON or OFF)
		sb.AppendLine(IsAutoCommit
			? $"- {Constants.Instructions.AutoCommitOn}"
			: $"- {Constants.Instructions.AutoCommitOff}");

		if (_isNewBranch)
			sb.AppendLine($"- {Constants.Instructions.NewBranch}");

		if (IsAutoDocument)
			sb.AppendLine($"- {Constants.Instructions.AutoDocument}");

		if (_pendingClear)
			sb.AppendLine($"- {Constants.Instructions.Clear}");

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
