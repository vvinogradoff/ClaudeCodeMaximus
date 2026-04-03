using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;
using Serilog;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionViewModel : ViewModelBase, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<SessionViewModel>();

	private readonly SessionNodeViewModel _node;
	private readonly ISessionFileService _fileService;
	private readonly IClaudeProcessManager _processManager;
	private readonly IAppSettingsService _appSettings;
	private readonly IDraftService _draftService;
	private readonly ICodeIndexService _codeIndexService;
	private readonly IClaudeProfileService _profileService;
	private readonly IClaudeSessionImportService _importService;
	private readonly ITessynRunService? _runService;
	private readonly ITessynDaemonService? _daemonService;
	private IDisposable? _runEventSubscription;
	private string? _activeRunId;
	private bool _daemonPendingClear;
	private bool _daemonPendingAutoCompact;
	private CancellationTokenSource? _draftSaveCts;
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
	private bool _midRunAutoCompactState;
	private DispatcherTimer? _draftDebounceTimer;
	private bool _isCommandBarVisible;
	private int _selectedModelIndex;
	private FileSystemWatcher? _fileWatcher;
	private FileSystemWatcher? _jsonlWatcher;
	private Timer? _fileChangeDebounceTimer;
	private Timer? _jsonlChangeDebounceTimer;
	private int _lastKnownEntryCount;
	private int _selectedProfileIndex;
	private bool _isProfileAuthInProgress;

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
			_node.HasDraftText = !string.IsNullOrWhiteSpace(value);
			SaveDraft(value);
		}
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
	}

	/// <summary>True when this session is being actively used in an external terminal.</summary>
	public bool IsExternallyActive => _node.IsExternallyActive;

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
			var oldValue = _node.Model.IsAutoCommit;
			_node.Model.IsAutoCommit = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
			if (IsBusy && value != oldValue)
				SendMidRunToggleCorrection("AutoCommit", value,
					Constants.Instructions.MidRunAutoCommitOn,
					Constants.Instructions.MidRunAutoCommitOff);
		}
	}

	/// <summary>One-shot toggle (FR.11.4). Auto-resets after prompt sent.</summary>
	public bool IsNewBranch
	{
		get => _isNewBranch;
		set
		{
			var oldValue = _isNewBranch;
			this.RaiseAndSetIfChanged(ref _isNewBranch, value);
			if (IsBusy && value != oldValue)
				SendMidRunToggleCorrection("NewBranch", value,
					Constants.Instructions.MidRunNewBranchOn,
					Constants.Instructions.MidRunNewBranchOff);
		}
	}

	/// <summary>Per-session sticky toggle (FR.11.5). Persisted in appsettings.json.</summary>
	public bool IsAutoDocument
	{
		get => _node.Model.IsAutoDocument;
		set
		{
			var oldValue = _node.Model.IsAutoDocument;
			_node.Model.IsAutoDocument = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
			if (IsBusy && value != oldValue)
				SendMidRunToggleCorrection("AutoDocument", value,
					Constants.Instructions.MidRunAutoDocumentOn,
					Constants.Instructions.MidRunAutoDocumentOff);
		}
	}

	/// <summary>One-shot toggle (FR.11.6). Auto-resets after compaction completes.</summary>
	public bool IsAutoCompact
	{
		get => _isAutoCompact;
		set
		{
			var oldValue = _isAutoCompact;
			this.RaiseAndSetIfChanged(ref _isAutoCompact, value);
			if (IsBusy && value != oldValue)
			{
				_midRunAutoCompactState = value;
				var label = value ? "enabled" : "disabled";
				var statusMsg = value
					? Constants.Instructions.MidRunAutoCompactOn
					: Constants.Instructions.MidRunAutoCompactOff;
				Messages.Add(new MessageEntryViewModel
				{
					Role      = Constants.SessionFile.RoleSystem,
					Content   = $"[AutoCompact was {label} for this run]",
					Timestamp = DateTimeOffset.UtcNow,
				});
				_log.Information("Mid-run AutoCompact toggle: {State}", label);
			}
		}
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

	/// <summary>Whether the command bar beneath the input area is visible.</summary>
	public bool IsCommandBarVisible
	{
		get => _isCommandBarVisible;
		set => this.RaiseAndSetIfChanged(ref _isCommandBarVisible, value);
	}

	/// <summary>Display names for the model selector.</summary>
	public static string[] AvailableModels { get; } = ["Default", "Opus", "Sonnet", "Haiku"];

	/// <summary>Model aliases passed to --model flag. Empty string means no flag. CLI resolves aliases to latest version.</summary>
	private static readonly string[] ModelIds = ["", "opus", "sonnet", "haiku"];

	/// <summary>Selected model index (0=Default, 1=Opus, 2=Sonnet, 3=Haiku). Persisted in appsettings.</summary>
	public int SelectedModelIndex
	{
		get => _selectedModelIndex;
		set
		{
			this.RaiseAndSetIfChanged(ref _selectedModelIndex, value);
			_appSettings.Settings.SelectedModelIndex = value;
			_appSettings.Save();
		}
	}

	/// <summary>Returns the model ID for --model flag, or null if Default is selected.</summary>
	public string? SelectedModelId =>
		_selectedModelIndex > 0 && _selectedModelIndex < ModelIds.Length
			? ModelIds[_selectedModelIndex]
			: null;

	/// <summary>Display names for the profile selector. Rebuilt when profiles change.</summary>
	public ObservableCollection<string> AvailableProfiles { get; } = [];

	/// <summary>Selected profile index. 0=Default, 1..N=stored profiles, last="New...".</summary>
	public int SelectedProfileIndex
	{
		get => _selectedProfileIndex;
		set
		{
			// "New..." is always the last item
			if (value == AvailableProfiles.Count - 1)
			{
				_ = HandleNewProfileAsync();
				// Revert selection to previous value (don't persist "New...")
				this.RaisePropertyChanged();
				return;
			}

			this.RaiseAndSetIfChanged(ref _selectedProfileIndex, value);
			_appSettings.Settings.SelectedProfileIndex = value;
			_appSettings.Save();
		}
	}

	/// <summary>Returns the CLAUDE_CONFIG_DIR path for the selected profile, or null if Default is selected.</summary>
	public string? SelectedProfileConfigDir
	{
		get
		{
			if (_selectedProfileIndex <= 0)
				return null;
			var profiles = _appSettings.Settings.Profiles;
			var profileListIndex = _selectedProfileIndex - 1;
			if (profileListIndex < 0 || profileListIndex >= profiles.Count)
				return null;
			return System.IO.Path.Combine(_profileService.ProfilesRootDirectory, profiles[profileListIndex].ProfileId);
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
		ICodeIndexService codeIndexService,
		IClaudeProfileService profileService,
		IClaudeSessionImportService importService,
		ITessynRunService? runService = null,
		ITessynDaemonService? daemonService = null)
	{
		_node             = node;
		_fileService      = fileService;
		_processManager   = processManager;
		_appSettings      = appSettings;
		_draftService     = draftService;
		_codeIndexService = codeIndexService;
		_profileService   = profileService;
		_importService    = importService;
		_runService       = runService;
		_daemonService    = daemonService;
		_name             = node.Name;
		_selectedModelIndex = Math.Clamp(appSettings.Settings.SelectedModelIndex, 0, ModelIds.Length - 1);
		AutocompleteVm    = new AutocompleteViewModel(codeIndexService);
		OutputSearchVm    = new OutputSearchViewModel(Messages);

		RebuildProfileList();
		_selectedProfileIndex = Math.Clamp(appSettings.Settings.SelectedProfileIndex, 0, Math.Max(0, AvailableProfiles.Count - 2));

		node.WhenAnyValue(x => x.Name).Subscribe(n => Name = n);
		node.WhenAnyValue(x => x.IsExternallyActive).Subscribe(_ => this.RaisePropertyChanged(nameof(IsExternallyActive)));

		SendCommand           = ReactiveCommand.Create(() => { _ = SendAsync(); });
		ToggleMarkdownCommand = ReactiveCommand.Create(() => { IsMarkdownMode = !IsMarkdownMode; });
		ClearCommand          = ReactiveCommand.Create(() => { _pendingClear = true; });

		// Start background indexing for this session's working directory
		if (!string.IsNullOrWhiteSpace(WorkingDirectory))
			_ = codeIndexService.GetOrCreateIndexAsync(WorkingDirectory);

		_log.Debug("SessionViewModel created: UseDaemon={UseDaemon}, ExternalId={ExternalId}",
			UseDaemon, _node.ExternalId);
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
		{
			_inputText = draft;
			_node.HasDraftText = !string.IsNullOrWhiteSpace(draft);
		}

		// Initialize JSONL entry count for the watcher. Must reflect the JSONL's current
		// state, not the .txt file's, because the JSONL typically has more entries (tool_use, etc.)
		// and RefreshFromJsonl compares against this to find only truly new entries.
		_lastKnownEntryCount = InitializeJsonlEntryCount();
		StartFileWatcher();
	}

	/// <summary>
	/// Loads session content from the Tessyn daemon instead of local .txt files.
	/// Used when UseTessynDaemon is enabled.
	/// </summary>
	public async System.Threading.Tasks.Task LoadFromDaemonAsync()
	{
		if (_daemonService == null || _node.ExternalId == null) return;

		try
		{
			var result = await _daemonService.SessionsGetAsync(_node.ExternalId);
			foreach (var msg in result.Messages)
			{
				Messages.Add(new MessageEntryViewModel
				{
					Role      = msg.Role.ToUpperInvariant(),
					Content   = msg.Content,
					Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(msg.Timestamp),
				});
			}

			// Load draft from daemon
			var draft = await _daemonService.DraftGetAsync(_node.ExternalId);
			if (draft is not null)
			{
				_inputText = draft;
				_node.HasDraftText = !string.IsNullOrWhiteSpace(draft);
			}
		}
		catch (TessynRpcException ex) when (ex.Code == Constants.Tessyn.ErrorSessionNotFound)
		{
			_log.Warning("Session {ExternalId} not found in daemon", _node.ExternalId);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Failed to load session from daemon");
		}
	}

	/// <summary>
	/// Sends a message via the Tessyn daemon instead of spawning a local claude process.
	/// Used when UseTessynDaemon is enabled.
	/// </summary>
	private async System.Threading.Tasks.Task SendViaDaemonAsync()
	{
		if (_runService == null) return;

		var message = InputText.Trim();
		if (string.IsNullOrEmpty(message)) return;

		// For cross-project imported sessions, use the original project path for resume to work
		var projectPath = _node.Model.OriginalProjectPath ?? _node.Model.WorkingDirectory;

		_log.Debug("SendViaDaemonAsync: ProjectPath={Dir}, ExternalId={Id}", projectPath, _node.ExternalId);

		InputText = string.Empty;

		// Build augmented message with hidden instructions (same as legacy path)
		var instructionBlock = BuildInstructionBlock();
		var augmentedMessage = message + instructionBlock;

		// Capture and reset one-shot toggles
		_daemonPendingClear = _pendingClear;
		_daemonPendingAutoCompact = _isAutoCompact;
		if (_isNewBranch) IsNewBranch = false;
		_pendingClear = false;

		_busyCount++;
		IsBusy = true;
		_node.IsRunning = true;

		if (_thinkingTimer == null)
		{
			_thinkingStartedAt = DateTimeOffset.UtcNow;
			ThinkingDuration = "0:00";
			_thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			_thinkingTimer.Tick += OnThinkingTimerTick;
			_thinkingTimer.Start();
		}

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

		try
		{
			// Subscribe to ALL run events before sending, then filter by runId once known.
			// This prevents missing early events (run.system, initial deltas) that arrive
			// before run.send returns the runId.
			var pendingEvents = new List<TessynRunEvent>();
			_runEventSubscription?.Dispose();
			_runEventSubscription = _runService.RunEvents
				.Subscribe(evt =>
				{
					if (_activeRunId != null && evt.RunId == _activeRunId)
						HandleRunEvent(evt);
					else if (_activeRunId == null)
						pendingEvents.Add(evt); // Buffer until runId is known
				});

			var runId = await _runService.SendAsync(
				projectPath,
				augmentedMessage,
				_node.ExternalId,
				SelectedModelId,
				_appSettings.Settings.DaemonPermissionMode);

			_activeRunId = runId;

			// Replay any buffered events for this run
			foreach (var buffered in pendingEvents)
			{
				if (buffered.RunId == runId)
					HandleRunEvent(buffered);
			}
			pendingEvents.Clear();

			// Narrow subscription to only this run now that we have the runId
			_runEventSubscription?.Dispose();
			_runEventSubscription = _runService.RunEvents
				.Where(e => e.RunId == runId)
				.Subscribe(HandleRunEvent);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Failed to send message via daemon");
			Dispatcher.UIThread.Post(() =>
			{
				for (var i = Messages.Count - 1; i >= 0; i--)
					if (Messages[i].IsProgress) Messages.RemoveAt(i);

				Messages.Add(new MessageEntryViewModel
				{
					Role      = Constants.SessionFile.RoleSystem,
					Content   = $"Error: {ex.Message}",
					Timestamp = DateTimeOffset.UtcNow,
				});
			});

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

	/// <summary>
	/// Handles a single run event from the Tessyn daemon. Called on the background thread;
	/// posts UI updates to the dispatcher.
	/// </summary>
	private void HandleRunEvent(TessynRunEvent evt)
	{
		switch (evt.Type)
		{
			case "started":
				// Run is spawning — no UI action needed beyond what we already show
				break;

			case "system":
				// Capture ExternalId for new sessions
				if (evt.ExternalId != null && _node.Model.ExternalId == null)
				{
					Dispatcher.UIThread.Post(() =>
					{
						_node.Model.ExternalId = evt.ExternalId;
						_node.Model.ClaudeSessionId = evt.ExternalId;
						_appSettings.Save();
					});
				}
				break;

			case "delta" when evt.Delta != null:
				Dispatcher.UIThread.Post(() =>
				{
					// Remove "thinking" progress message and start accumulating assistant text
					var last = Messages.Count > 0 ? Messages[^1] : null;
					if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
						Messages.RemoveAt(Messages.Count - 1);

					// Append to existing assistant message or create new one
					last = Messages.Count > 0 ? Messages[^1] : null;
					if (last?.Role == Constants.SessionFile.RoleAssistant)
						last.Content += evt.Delta;
					else
						Messages.Add(new MessageEntryViewModel
						{
							Role      = Constants.SessionFile.RoleAssistant,
							Content   = evt.Delta,
							Timestamp = DateTimeOffset.UtcNow,
						});
				});
				break;

			case "block_start" when evt.BlockType == "tool_use":
				Dispatcher.UIThread.Post(() =>
				{
					Messages.Add(new MessageEntryViewModel
					{
						Role       = Constants.SessionFile.RoleSystem,
						Content    = $"Using tool: {evt.ToolName ?? "unknown"}",
						Timestamp  = DateTimeOffset.UtcNow,
						IsProgress = true,
					});
				});
				break;

			case "block_start":
				// Non-tool content blocks (text, thinking) — start new assistant message
				if (evt.BlockType == "text")
				{
					Dispatcher.UIThread.Post(() =>
					{
						// Remove progress messages
						var last = Messages.Count > 0 ? Messages[^1] : null;
						if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
							Messages.RemoveAt(Messages.Count - 1);

						Messages.Add(new MessageEntryViewModel
						{
							Role      = Constants.SessionFile.RoleAssistant,
							Content   = string.Empty,
							Timestamp = DateTimeOffset.UtcNow,
						});
					});
				}
				break;

			case "block_stop":
				// Content block ended — remove tool progress for tool_use blocks
				Dispatcher.UIThread.Post(() =>
				{
					for (var i = Messages.Count - 1; i >= 0; i--)
					{
						if (Messages[i].IsProgress && Messages[i].Content.StartsWith("Using tool:"))
						{
							Messages.RemoveAt(i);
							break;
						}
					}
				});
				break;

			case "message":
				// Full message received — used for reconnect catch-up, no action needed
				// during normal streaming since we accumulate via deltas
				break;

			case "completed":
				Dispatcher.UIThread.Post(() =>
				{
					// Remove any remaining progress messages
					for (var i = Messages.Count - 1; i >= 0; i--)
						if (Messages[i].IsProgress) Messages.RemoveAt(i);

					if (evt.Usage != null)
					{
						Messages.Add(new MessageEntryViewModel
						{
							Role      = Constants.SessionFile.RoleSystem,
							Content   = $"[{evt.Usage.InputTokens} in / {evt.Usage.OutputTokens} out, {evt.Usage.DurationMs / 1000.0:F1}s, ${evt.Usage.CostUsd:F4}]",
							Timestamp = DateTimeOffset.UtcNow,
						});
					}
				});
				FinishDaemonRun(success: true);
				break;

			case "failed":
				Dispatcher.UIThread.Post(() =>
				{
					for (var i = Messages.Count - 1; i >= 0; i--)
						if (Messages[i].IsProgress) Messages.RemoveAt(i);

					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = $"Error: {evt.Error ?? "Unknown error"}",
						Timestamp = DateTimeOffset.UtcNow,
					});
				});
				FinishDaemonRun(success: false);
				break;

			case "cancelled":
				Dispatcher.UIThread.Post(() =>
				{
					for (var i = Messages.Count - 1; i >= 0; i--)
						if (Messages[i].IsProgress) Messages.RemoveAt(i);

					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = "[Run cancelled]",
						Timestamp = DateTimeOffset.UtcNow,
					});
				});
				FinishDaemonRun(success: false);
				break;

			case "rate_limit":
				Dispatcher.UIThread.Post(() =>
				{
					var retryMs = evt.RetryAfterMs ?? 0;
					var last = Messages.Count > 0 ? Messages[^1] : null;
					if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
						last.Content = $"Rate limited — retrying in {retryMs / 1000.0:F0}s...";
					else
						Messages.Add(new MessageEntryViewModel
						{
							Role       = Constants.SessionFile.RoleSystem,
							Content    = $"Rate limited — retrying in {retryMs / 1000.0:F0}s...",
							Timestamp  = DateTimeOffset.UtcNow,
							IsProgress = true,
						});
				});
				break;
		}
	}

	private void FinishDaemonRun(bool success)
	{
		_activeRunId = null;
		_runEventSubscription?.Dispose();
		_runEventSubscription = null;

		// Trigger daemon reindex so new messages are persisted in the index.
		// Workaround: daemon's incremental reindex after run completion isn't reliable yet.
		if (_daemonService != null)
		{
			_ = Task.Run(async () =>
			{
				try { await _daemonService.ReindexAsync(); }
				catch (Exception ex) { _log.Debug(ex, "Post-run reindex failed"); }
			});
		}

		// Post-run behavior: clear session and auto-compact (mirrors legacy path)
		if (success)
		{
			if (_daemonPendingClear)
			{
				_daemonPendingClear = false;
				Dispatcher.UIThread.Post(() =>
				{
					_node.Model.ClaudeSessionId = null;
					_node.Model.ExternalId = null;
					_appSettings.Save();
					this.RaisePropertyChanged(nameof(CanClear));
				});
			}

			if (_daemonPendingAutoCompact)
			{
				_daemonPendingAutoCompact = false;
				// Auto-compact via daemon: send compaction prompt as a follow-up
				if (_runService != null && _node.ExternalId != null)
				{
					_ = Task.Run(async () =>
					{
						try
						{
							await _runService.SendAsync(
								_node.Model.WorkingDirectory,
								Constants.Instructions.CompactionPrompt,
								_node.ExternalId,
								permissionMode: _appSettings.Settings.DaemonPermissionMode);
						}
						catch (Exception ex)
						{
							_log.Warning(ex, "Auto-compact via daemon failed");
						}
					});
				}
				Dispatcher.UIThread.Post(() => IsAutoCompact = false);
			}
		}
		else
		{
			_daemonPendingClear = false;
			_daemonPendingAutoCompact = false;
		}

		Dispatcher.UIThread.Post(() =>
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
		});
	}

	/// <summary>Whether this SessionViewModel should use Tessyn daemon for operations.</summary>
	private bool UseDaemon => _appSettings.Settings.UseTessynDaemon && _runService != null && _daemonService != null;

	public void Dispose()
	{
		_runEventSubscription?.Dispose();
		_fileWatcher?.Dispose();
		_jsonlWatcher?.Dispose();
		_fileChangeDebounceTimer?.Dispose();
		_jsonlChangeDebounceTimer?.Dispose();
	}

	/// <summary>
	/// Counts the current displayable entries in the JSONL file so the watcher
	/// can detect only truly new entries. Falls back to Messages.Count if no JSONL exists.
	/// </summary>
	private int InitializeJsonlEntryCount()
	{
		var sessionId = _node.Model.ClaudeSessionId;
		if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(WorkingDirectory))
			return Messages.Count;

		try
		{
			var slug = Constants.ClaudeSessions.BuildProjectSlug(WorkingDirectory);
			var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var jsonlPath = Path.Combine(
				userProfile,
				Constants.ClaudeSessions.ClaudeHomeFolderName,
				Constants.ClaudeSessions.ProjectsFolderName,
				slug,
				sessionId + Constants.ClaudeSessions.SessionFileExtension);

			if (!File.Exists(jsonlPath))
				return Messages.Count;

			var entries = _importService.ParseJsonlSession(jsonlPath);
			var count = 0;
			foreach (var entry in entries)
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction
				    && !string.IsNullOrWhiteSpace(entry.Content))
					count++;
			}

			_log.Debug("InitializeJsonlEntryCount: {Count} displayable entries in JSONL for session {SessionId}",
				count, sessionId);
			return count;
		}
		catch (Exception ex)
		{
			_log.Debug("InitializeJsonlEntryCount: error reading JSONL: {Error}", ex.Message);
			return Messages.Count;
		}
	}

	private void StartFileWatcher()
	{
		// Watch the Maximus .txt session file
		try
		{
			var fullPath = _fileService.GetFullPath(_node.FileName);
			var directory = Path.GetDirectoryName(fullPath);
			var fileName = Path.GetFileName(fullPath);

			if (directory != null && Directory.Exists(directory))
			{
				_fileWatcher = new FileSystemWatcher(directory, fileName)
				{
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
					EnableRaisingEvents = true,
				};
				_fileWatcher.Changed += OnSessionFileChanged;
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to start .txt file watcher for session {FileName}", _node.FileName);
		}

		// Watch the Claude Code JSONL file (for sessions running externally)
		StartJsonlWatcher();
	}

	private void StartJsonlWatcher()
	{
		var sessionId = _node.Model.ClaudeSessionId;
		if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(WorkingDirectory))
			return;

		try
		{
			var slug = Constants.ClaudeSessions.BuildProjectSlug(WorkingDirectory);
			var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var jsonlDir = Path.Combine(
				userProfile,
				Constants.ClaudeSessions.ClaudeHomeFolderName,
				Constants.ClaudeSessions.ProjectsFolderName,
				slug);

			var jsonlFileName = sessionId + Constants.ClaudeSessions.SessionFileExtension;
			var jsonlPath = Path.Combine(jsonlDir, jsonlFileName);

			if (!Directory.Exists(jsonlDir))
				return;

			_log.Debug("Starting JSONL watcher for {Path}", jsonlPath);

			_jsonlWatcher = new FileSystemWatcher(jsonlDir, jsonlFileName)
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
				EnableRaisingEvents = true,
			};
			_jsonlWatcher.Changed += OnJsonlFileChanged;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to start JSONL watcher for session {SessionId}", sessionId);
		}
	}

	private void OnSessionFileChanged(object sender, FileSystemEventArgs e)
	{
		// Ignore changes caused by our own writes (IsBusy covers all write paths)
		if (IsBusy)
			return;

		_fileChangeDebounceTimer?.Dispose();
		_fileChangeDebounceTimer = new Timer(
			_ => RefreshFromFile(),
			null,
			500,
			Timeout.Infinite);
	}

	private void OnJsonlFileChanged(object sender, FileSystemEventArgs e)
	{
		// Ignore changes when we're actively sending (our own process writes to the JSONL too)
		if (IsBusy)
			return;

		// Quick check: read the last line to detect if Claude is actively working
		DetectExternalActivity(e.FullPath);

		_jsonlChangeDebounceTimer?.Dispose();
		_jsonlChangeDebounceTimer = new Timer(
			_ => RefreshFromJsonl(e.FullPath),
			null,
			1000,
			Timeout.Infinite);
	}

	private void DetectExternalActivity(string jsonlPath)
	{
		try
		{
			var lastLine = ReadLastLine(jsonlPath);
			if (lastLine == null)
				return;

			using var doc = System.Text.Json.JsonDocument.Parse(lastLine);
			var type = doc.RootElement.TryGetProperty("type", out var typeEl)
				? typeEl.GetString() : null;

			// progress, assistant (mid-stream), system with task_progress → active
			// result → done
			var isActive = type is "progress" or "assistant";

			Dispatcher.UIThread.Post(() => _node.IsExternallyActive = isActive);
		}
		catch
		{
			// Best effort — don't crash on parse errors
		}
	}

	/// <summary>Reads the last non-empty line from a file without loading it all into memory.</summary>
	private static string? ReadLastLine(string path)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (stream.Length == 0)
				return null;

			// Seek backwards from end to find the last newline
			var pos = stream.Length - 1;
			// Skip trailing newlines
			while (pos > 0)
			{
				stream.Position = pos;
				var b = stream.ReadByte();
				if (b != '\n' && b != '\r')
					break;
				pos--;
			}

			// Find the start of the last line
			while (pos > 0)
			{
				stream.Position = pos - 1;
				var b = stream.ReadByte();
				if (b == '\n')
					break;
				pos--;
			}

			stream.Position = pos;
			using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
			return reader.ReadLine();
		}
		catch
		{
			return null;
		}
	}

	private void RefreshFromFile()
	{
		try
		{
			var entries = _fileService.ReadEntries(_node.FileName);
			var currentCount = 0;

			// Count non-empty entries to compare with current Messages
			var newEntries = new List<SessionEntryModel>();
			foreach (var entry in entries)
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction
				    && string.IsNullOrWhiteSpace(entry.Content))
					continue;

				currentCount++;
				if (currentCount > Messages.Count)
					newEntries.Add(entry);
			}

			if (newEntries.Count == 0)
				return;

			_log.Information("FileWatcher: {Count} new entries detected in {FileName}",
				newEntries.Count, _node.FileName);

			Dispatcher.UIThread.Post(() =>
			{
				foreach (var entry in newEntries)
					Messages.Add(EntryToViewModel(entry));

				// Update last prompt time if new entries include a user message
				var lastUser = newEntries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser);
				if (lastUser != null)
				{
					_node.LastPromptTimestamp = lastUser.Timestamp;
					_node.LastPromptTime = lastUser.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
				}
			});
		}
		catch (Exception ex)
		{
			_log.Debug("FileWatcher: error reading {FileName}: {Error}", _node.FileName, ex.Message);
		}
	}

	private void RefreshFromJsonl(string jsonlPath)
	{
		try
		{
			// Parse the full JSONL and find entries beyond what we already have
			var allEntries = _importService.ParseJsonlSession(jsonlPath);

			// Filter to displayable entries (same logic as LoadFromFile)
			var displayable = new List<SessionEntryModel>();
			foreach (var entry in allEntries)
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction
				    && string.IsNullOrWhiteSpace(entry.Content))
					continue;
				displayable.Add(entry);
			}

			if (displayable.Count <= _lastKnownEntryCount)
				return;

			var newEntries = displayable.Skip(_lastKnownEntryCount).ToList();
			_log.Information("JSONL watcher: {Count} new entries detected from JSONL", newEntries.Count);

			// Append new entries to the Maximus .txt file for persistence
			foreach (var entry in newEntries)
				_fileService.AppendMessage(_node.FileName, entry.Role, entry.Content);

			_lastKnownEntryCount = displayable.Count;

			Dispatcher.UIThread.Post(() =>
			{
				foreach (var entry in newEntries)
					Messages.Add(EntryToViewModel(entry));

				var lastUser = newEntries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser);
				if (lastUser != null)
				{
					_node.LastPromptTimestamp = lastUser.Timestamp;
					_node.LastPromptTime = lastUser.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
				}
			});
		}
		catch (Exception ex)
		{
			_log.Debug("JSONL watcher: error reading {Path}: {Error}", jsonlPath, ex.Message);
		}
	}

	private async System.Threading.Tasks.Task SendAsync()
	{
		if (UseDaemon)
		{
			await SendViaDaemonAsync();
			return;
		}
		var message = InputText.Trim();
		if (string.IsNullOrEmpty(message))
			return;

		InputText = string.Empty;
		_draftService.DeleteDraft(_node.FileName);

		// Capture one-shot toggle states before resetting them
		var wasNewBranch = _isNewBranch;
		var wasAutoCompact = _isAutoCompact;
		var wasPendingClear = _pendingClear;
		_midRunAutoCompactState = _isAutoCompact;

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
				onEvent:          HandleStreamEvent,
				model:            SelectedModelId,
				profileConfigDir: SelectedProfileConfigDir);

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
					onEvent:          HandleStreamEvent,
					model:            SelectedModelId,
				profileConfigDir: SelectedProfileConfigDir);
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
			// Use _midRunAutoCompactState which reflects any mid-run toggle changes
			if (_midRunAutoCompactState)
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
				// Update JSONL entry count before IsBusy goes false, so the watcher
				// doesn't re-detect entries we already processed during this send cycle.
				_lastKnownEntryCount = InitializeJsonlEntryCount();

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

			if (UseDaemon && _node.ExternalId != null)
			{
				// Cancel any previous in-flight save to ensure last-write-wins ordering
				_draftSaveCts?.Cancel();
				var cts = new CancellationTokenSource();
				_draftSaveCts = cts;
				var externalId = _node.ExternalId;
				var content = string.IsNullOrEmpty(text) ? string.Empty : text;

				_ = Task.Run(async () =>
				{
					try
					{
						await _daemonService!.DraftSaveAsync(externalId, content, cts.Token);
					}
					catch (OperationCanceledException) { /* superseded by newer save */ }
					catch (Exception ex)
					{
						_log.Debug(ex, "Failed to save draft to daemon");
					}
				}, cts.Token);
			}
			else
			{
				if (string.IsNullOrEmpty(text))
					_draftService.DeleteDraft(_node.FileName);
				else
					_draftService.SaveDraft(_node.FileName, text);
			}
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
			model:            SelectedModelId,
			profileConfigDir: SelectedProfileConfigDir,
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
			// Write COMPACTION separator followed by the compacted conversation
			// (Claude outputs entries in session file format: [timestamp] ROLE\ncontent\n)
			var now = DateTimeOffset.UtcNow;
			var fileContent = new StringBuilder();
			fileContent.AppendLine($"[{now.ToString(Constants.SessionFile.TimestampFormat)}] {Constants.SessionFile.RoleCompaction}");
			fileContent.AppendLine(compacted);
			if (!compacted.EndsWith(Environment.NewLine))
				fileContent.AppendLine();

			_fileService.RewriteSessionFile(_node.FileName, fileContent.ToString());

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

	/// <summary>Sends a mid-run correction prompt when user toggles a flag while Claude is thinking.</summary>
	private void SendMidRunToggleCorrection(string toggleName, bool newValue, string onPrompt, string offPrompt)
	{
		var label = newValue ? "enabled" : "disabled";
		var prompt = newValue ? onPrompt : offPrompt;

		Messages.Add(new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleSystem,
			Content   = $"[{toggleName} was {label} for this run]",
			Timestamp = DateTimeOffset.UtcNow,
		});

		_log.Information("Mid-run {Toggle} toggle: {State}, sending correction prompt", toggleName, label);

		_ = _processManager.SendMessageAsync(
			workingDirectory: _node.Model.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        _node.Model.ClaudeSessionId,
			userMessage:      prompt,
			model:            SelectedModelId,
			profileConfigDir: SelectedProfileConfigDir,
			onEvent:          evt =>
			{
				// Capture session ID updates but don't write to file or UI
				if (evt.Type == "result" && !evt.IsError && evt.SessionId is not null)
				{
					_node.Model.ClaudeSessionId = evt.SessionId;
					_appSettings.Save();
				}
			});
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

	/// <summary>Rebuilds the AvailableProfiles list from appsettings. Always ends with "New...".</summary>
	private void RebuildProfileList()
	{
		AvailableProfiles.Clear();
		AvailableProfiles.Add(_defaultProfileDisplayName ?? "Default");
		foreach (var p in _appSettings.Settings.Profiles)
			AvailableProfiles.Add(p.DisplayName);
		AvailableProfiles.Add("New...");
	}

	private string? _defaultProfileDisplayName;

	/// <summary>Resolves the default profile email on first load (fire-and-forget).</summary>
	internal void ResolveDefaultProfileEmail()
	{
		_ = ResolveDefaultProfileEmailAsync();
	}

	private async Task ResolveDefaultProfileEmailAsync()
	{
		var email = await _profileService.GetAccountEmailAsync(_appSettings.Settings.ClaudePath, null);
		if (!string.IsNullOrEmpty(email))
		{
			_defaultProfileDisplayName = email;
			Dispatcher.UIThread.Post(() =>
			{
				if (AvailableProfiles.Count > 0)
					AvailableProfiles[0] = email;
			});
		}
	}

	private async Task HandleNewProfileAsync()
	{
		if (_isProfileAuthInProgress)
			return;

		_isProfileAuthInProgress = true;
		try
		{
			// Generate a unique profile ID
			var existingIds = _appSettings.Settings.Profiles.Select(p => p.ProfileId).ToHashSet();
			var profileId = "profile_1";
			for (var i = 2; existingIds.Contains(profileId); i++)
				profileId = $"profile_{i}";

			// Build the config directory path for this profile
			var configDir = System.IO.Path.Combine(_profileService.ProfilesRootDirectory, profileId);

			// Launch interactive auth in a visible terminal
			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = $"Launching auth login for new profile... Complete authentication in the opened window.",
				Timestamp = DateTimeOffset.UtcNow,
			});

			await _profileService.LaunchAuthLoginAsync(_appSettings.Settings.ClaudePath, configDir);

			// Verify auth succeeded by querying the profile status
			var email = await _profileService.GetAccountEmailAsync(_appSettings.Settings.ClaudePath, configDir);
			if (string.IsNullOrEmpty(email))
			{
				Messages.Add(new MessageEntryViewModel
				{
					Role      = Constants.SessionFile.RoleSystem,
					Content   = "Profile authentication was cancelled or failed.",
					Timestamp = DateTimeOffset.UtcNow,
				});
				return;
			}

			var displayName = email;

			// Add profile to settings
			_appSettings.Settings.Profiles.Add(new ClaudeProfileModel
			{
				ProfileId   = profileId,
				DisplayName = displayName,
			});

			// Select the newly added profile
			var newIndex = _appSettings.Settings.Profiles.Count; // 1-based (0 is Default)
			_appSettings.Settings.SelectedProfileIndex = newIndex;
			_appSettings.Save();

			Dispatcher.UIThread.Post(() =>
			{
				RebuildProfileList();
				_selectedProfileIndex = newIndex;
				this.RaisePropertyChanged(nameof(SelectedProfileIndex));
			});

			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = $"Profile '{displayName}' added and selected.",
				Timestamp = DateTimeOffset.UtcNow,
			});
		}
		finally
		{
			_isProfileAuthInProgress = false;
		}
	}

	private static MessageEntryViewModel EntryToViewModel(SessionEntryModel entry)
		=> new()
		{
			Role      = entry.Role,
			Content   = entry.Content,
			Timestamp = entry.Timestamp,
		};
}
