using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
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
	private MessageEntryViewModel? _currentAssistantMessage;
	private readonly Dictionary<int, MessageBlockViewModel> _activeBlocks = new();
	private bool _daemonPendingClear;
	private bool _daemonPendingAutoCompact;
	private CancellationTokenSource? _draftSaveCts;
	private string _name;
	private string _inputText = string.Empty;
	private bool _isBusy;
	private bool _isMarkdownMode = true;
	private string _thinkingDuration = string.Empty;
	private string _thinkingStatusText = "Claude is thinking\u2026";
	private DispatcherTimer? _thinkingTimer;
	private DateTimeOffset _thinkingStartedAt;
	private int _busyCount;
	private bool _needsContextRetry;
	private bool _pendingClear;
	private bool _isNewBranch;
	private bool _isAutoCompact;
	private int _autoCommitState = 1; // 1=on, -1=off, 0=neutral
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
	private int _selectedDaemonProfileIndex;
	private int _selectedEffortIndex = 1; // 1 = Medium (CLI default)
	private string _currentModelText = string.Empty;
	private string _contextUsageText = string.Empty;
	private string _sessionCostText = string.Empty;
	private decimal _sessionTotalCost;
	private List<TessynProfile> _daemonProfiles = [];

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

	public string ThinkingStatusText
	{
		get => _thinkingStatusText;
		private set => this.RaiseAndSetIfChanged(ref _thinkingStatusText, value);
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

	/// <summary>Ternary auto-commit state: 1=on, -1=off, 0=neutral. Cycles ON→OFF→Neutral→ON.</summary>
	public int AutoCommitState
	{
		get => _autoCommitState;
		set
		{
			var oldValue = _autoCommitState;
			this.RaiseAndSetIfChanged(ref _autoCommitState, value);
			// Sync the bool model for backward compat
			_node.Model.IsAutoCommit = value > 0;
			_appSettings.Save();
			this.RaisePropertyChanged(nameof(AutoCommitLabel));
			if (IsBusy && value != oldValue)
			{
				if (value > 0)
					SendMidRunToggleCorrection("AutoCommit", true,
						Constants.Instructions.MidRunAutoCommitOn,
						Constants.Instructions.MidRunAutoCommitOff);
				else if (value < 0)
					SendMidRunToggleCorrection("AutoCommit", false,
						Constants.Instructions.MidRunAutoCommitOn,
						Constants.Instructions.MidRunAutoCommitOff);
				// neutral: no mid-run correction needed
			}
		}
	}

	public string AutoCommitLabel => _autoCommitState switch
	{
		1 => "\u2713 Commit",
		-1 => "\u2717 No commit",
		_ => "\u2014 Commit",
	};

	public ReactiveCommand<Unit, Unit> CycleAutoCommitCommand { get; }

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

	// --- Daemon profile selector ---

	/// <summary>Display names for the daemon profile dropdown.</summary>
	public ObservableCollection<string> DaemonProfileNames { get; } = [];

	/// <summary>Selected daemon profile index.</summary>
	public int SelectedDaemonProfileIndex
	{
		get => _selectedDaemonProfileIndex;
		set
		{
			// "Add account..." is always the last item
			if (value == DaemonProfileNames.Count - 1 && DaemonProfileNames.Count > 0
				&& DaemonProfileNames[value] == "Add account...")
			{
				_ = HandleAddDaemonProfileAsync();
				this.RaisePropertyChanged(); // revert selection visually
				return;
			}

			// Clicking an unauthenticated profile triggers login for it
			if (value >= 0 && value < _daemonProfiles.Count
				&& _daemonProfiles[value].Auth is not { LoggedIn: true })
			{
				_ = HandleLoginForProfileAsync(_daemonProfiles[value]);
				this.RaisePropertyChanged(); // revert selection visually
				return;
			}

			this.RaiseAndSetIfChanged(ref _selectedDaemonProfileIndex, value);
			var profileName = value >= 0 && value < _daemonProfiles.Count
				? (_daemonProfiles[value].IsDefault ? null : _daemonProfiles[value].Name)
				: null;
			_appSettings.Settings.DaemonProfile = profileName;
			_appSettings.Save();
		}
	}

	/// <summary>Returns the daemon profile name for run.send, or null for default.</summary>
	public string? SelectedDaemonProfile =>
		_selectedDaemonProfileIndex >= 0 && _selectedDaemonProfileIndex < _daemonProfiles.Count
			? (_daemonProfiles[_selectedDaemonProfileIndex].IsDefault ? null : _daemonProfiles[_selectedDaemonProfileIndex].Name)
			: _appSettings.Settings.DaemonProfile;

	// --- Reasoning effort ---

	/// <summary>Display names for reasoning effort selector.</summary>
	public static string[] AvailableEfforts { get; } = ["Low", "Medium", "High"];

	private static readonly string[] EffortIds = ["low", "medium", "high"];

	public int SelectedEffortIndex
	{
		get => _selectedEffortIndex;
		set => this.RaiseAndSetIfChanged(ref _selectedEffortIndex, value);
	}

	/// <summary>Returns the reasoning effort for run.send.</summary>
	public string? SelectedReasoningEffort =>
		_selectedEffortIndex >= 0 && _selectedEffortIndex < EffortIds.Length
			? EffortIds[_selectedEffortIndex]
			: "medium";

	/// <summary>Actual model name reported by the daemon in run.system events.</summary>
	public string CurrentModelText
	{
		get => _currentModelText;
		private set => this.RaiseAndSetIfChanged(ref _currentModelText, value);
	}

	// --- Context and cost display ---

	public string ContextUsageText
	{
		get => _contextUsageText;
		private set => this.RaiseAndSetIfChanged(ref _contextUsageText, value);
	}

	public string SessionCostText
	{
		get => _sessionCostText;
		private set => this.RaiseAndSetIfChanged(ref _sessionCostText, value);
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
		_autoCommitState  = node.Model.IsAutoCommit ? 1 : -1;
		_selectedModelIndex = Math.Clamp(appSettings.Settings.SelectedModelIndex, 0, ModelIds.Length - 1);
		AutocompleteVm    = new AutocompleteViewModel(codeIndexService);
		OutputSearchVm    = new OutputSearchViewModel(Messages);

		RebuildProfileList();
		_selectedProfileIndex = Math.Clamp(appSettings.Settings.SelectedProfileIndex, 0, Math.Max(0, AvailableProfiles.Count - 2));

		// Load daemon profiles and commands asynchronously
		if (_daemonService != null)
		{
			// Pre-populate with saved profile so the dropdown isn't empty while loading
			var savedProfile = _appSettings.Settings.DaemonProfile;
			DaemonProfileNames.Add(savedProfile ?? "Default");

			_ = LoadDaemonProfilesAsync();
			_ = LoadCommandsAsync();
		}

		node.WhenAnyValue(x => x.Name).Subscribe(n => Name = n);
		node.WhenAnyValue(x => x.IsExternallyActive).Subscribe(_ => this.RaisePropertyChanged(nameof(IsExternallyActive)));

		SendCommand           = ReactiveCommand.Create(() => { _ = SendAsync(); });
		ToggleMarkdownCommand = ReactiveCommand.Create(() => { IsMarkdownMode = !IsMarkdownMode; });
		ClearCommand          = ReactiveCommand.Create(() => { _pendingClear = true; });
		CycleAutoCommitCommand = ReactiveCommand.Create(() =>
		{
			AutoCommitState = _autoCommitState switch { 1 => -1, -1 => 0, _ => 1 };
		});

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
				var content = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
					? StripInstructionBlock(msg.Content)
					: msg.Content;
				Messages.Add(new MessageEntryViewModel
				{
					Role      = msg.Role.ToUpperInvariant(),
					Content   = content,
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

		// The dedicated thinking indicator (Row 2 in AXAML) with timer
		// already shows "Claude is thinking... 0:16" — no need for a
		// duplicate progress bubble in the message list.

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
				_appSettings.Settings.DaemonPermissionMode,
				SelectedDaemonProfile,
				SelectedReasoningEffort);

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
				ThinkingStatusText = "Claude is thinking\u2026";
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
				// Show actual model name in the status bar
				if (!string.IsNullOrEmpty(evt.Model))
				{
					Dispatcher.UIThread.Post(() =>
					{
						CurrentModelText = FormatModelName(evt.Model!);
					});
				}
				break;

			case "delta" when evt.Delta != null:
				Dispatcher.UIThread.Post(() =>
				{
					EnsureCurrentAssistantMessage();

					var blockIdx = evt.BlockIndex ?? -1;
					if (blockIdx >= 0 && _activeBlocks.TryGetValue(blockIdx, out var block))
					{
						// Append to the specific block
						if (block is TextBlockViewModel tb) tb.AppendText(evt.Delta);
						else if (block is ThinkingBlockViewModel thb) thb.AppendText(evt.Delta);
					}
					else
					{
						// No block tracked — fallback: append to Content directly
						_currentAssistantMessage!.Content += evt.Delta;
					}
				});
				break;

			case "block_start" when evt.BlockType == "tool_use":
				Dispatcher.UIThread.Post(() =>
				{
					ThinkingStatusText = $"Running: {evt.ToolName ?? "tool"}\u2026";
					EnsureCurrentAssistantMessage();

					var toolBlock = new ToolUseBlockViewModel
					{
						BlockIndex = evt.BlockIndex ?? -1,
						ToolName = evt.ToolName ?? "unknown",
						InputSummary = ToolUseBlockViewModel.SummarizeInput(evt.ToolName ?? "", evt.ToolInput),
						FullInput = evt.ToolInput,
						IsStreaming = true,
					};
					if (evt.BlockIndex.HasValue)
						_activeBlocks[evt.BlockIndex.Value] = toolBlock;
					_currentAssistantMessage!.Blocks.Add(toolBlock);
					this.RaisePropertyChanged(nameof(_currentAssistantMessage.HasBlocks));
				});
				break;

			case "block_start" when evt.BlockType == "thinking":
				Dispatcher.UIThread.Post(() =>
				{
					EnsureCurrentAssistantMessage();

					var thinkBlock = new ThinkingBlockViewModel
					{
						BlockIndex = evt.BlockIndex ?? -1,
						IsStreaming = true,
						IsExpanded = true, // expanded while streaming
					};
					if (evt.BlockIndex.HasValue)
						_activeBlocks[evt.BlockIndex.Value] = thinkBlock;
					_currentAssistantMessage!.Blocks.Add(thinkBlock);
				});
				break;

			case "block_start" when evt.BlockType == "text":
				Dispatcher.UIThread.Post(() =>
				{
					ThinkingStatusText = "Claude is responding\u2026";
					EnsureCurrentAssistantMessage();

					var textBlock = new TextBlockViewModel
					{
						BlockIndex = evt.BlockIndex ?? -1,
						IsStreaming = true,
					};
					if (evt.BlockIndex.HasValue)
						_activeBlocks[evt.BlockIndex.Value] = textBlock;
					_currentAssistantMessage!.Blocks.Add(textBlock);
				});
				break;

			case "block_stop":
				Dispatcher.UIThread.Post(() =>
				{
					var blockIdx = evt.BlockIndex ?? -1;
					if (blockIdx >= 0 && _activeBlocks.TryGetValue(blockIdx, out var stoppedBlock))
					{
						stoppedBlock.IsStreaming = false;
						if (stoppedBlock is ToolUseBlockViewModel tu)
						{
							if (evt.IsError == true)
								tu.Fail(evt.ToolResult);
							else
								tu.Complete(evt.ToolResult);
						}
						if (stoppedBlock is ThinkingBlockViewModel th)
							th.IsExpanded = false; // Auto-collapse thinking when done
					}
					// Sync Content for search
					_currentAssistantMessage?.SyncContentFromBlocks();
				});
				break;

			case "message" when evt.Role == "assistant" && evt.RawContent != null:
				// Extract tool input from the full message content blocks
				Dispatcher.UIThread.Post(() => ParseToolInputsFromMessage(evt.RawContent));
				break;

			case "message":
				break;

			case "completed":
				Dispatcher.UIThread.Post(() =>
				{
					// Clean up block tracking
					_activeBlocks.Clear();
					_currentAssistantMessage?.SyncContentFromBlocks();
					_currentAssistantMessage = null;

					// Remove any remaining progress messages
					for (var i = Messages.Count - 1; i >= 0; i--)
						if (Messages[i].IsProgress) Messages.RemoveAt(i);

					if (evt.Usage != null)
					{
						var usageText = evt.Usage.InputTokens == 0 && evt.Usage.OutputTokens == 0
							? "Command completed."
							: $"[{evt.Usage.InputTokens} in / {evt.Usage.OutputTokens} out, {evt.Usage.DurationMs / 1000.0:F1}s, ${evt.Usage.CostUsd:F4}]";
						Messages.Add(new MessageEntryViewModel
						{
							Role      = Constants.SessionFile.RoleSystem,
							Content   = usageText,
							Timestamp = DateTimeOffset.UtcNow,
						});
						// Only update cost/context for real API calls
						if (evt.Usage.InputTokens > 0)
						{
							UpdateSessionCost((decimal)evt.Usage.CostUsd);
							UpdateContextUsage(evt.Usage.InputTokens);
						}
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

			case "auth_required":
				Dispatcher.UIThread.Post(() =>
				{
					for (var i = Messages.Count - 1; i >= 0; i--)
						if (Messages[i].IsProgress) Messages.RemoveAt(i);

					Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = "Not logged in — please run  claude login  in a terminal, or type /login here.",
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

	/// <summary>
	/// Parse tool_use content blocks from a run.message event to extract tool inputs.
	/// Updates existing ToolUseBlockViewModels with the input summary.
	/// </summary>
	private void ParseToolInputsFromMessage(string rawContentJson)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(rawContentJson);
			foreach (var block in doc.RootElement.EnumerateArray())
			{
				if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tool_use"
					&& block.TryGetProperty("name", out var nameEl)
					&& block.TryGetProperty("input", out var inputEl))
				{
					var toolName = nameEl.GetString() ?? "";
					var inputJson = inputEl.GetRawText();

					// Find the matching ToolUseBlockViewModel (by tool name, since we may not have a direct index match)
					foreach (var tracked in _activeBlocks.Values)
					{
						if (tracked is ToolUseBlockViewModel tu
							&& tu.ToolName == toolName
							&& string.IsNullOrEmpty(tu.InputSummary))
						{
							var summary = ToolUseBlockViewModel.SummarizeInput(toolName, inputJson);
							if (!string.IsNullOrEmpty(summary))
							{
								tu.InputSummary = summary;
								tu.FullInput = inputJson;
							}
							break;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Failed to parse tool inputs from run.message");
		}
	}

	/// <summary>
	/// Ensures there's a current assistant message to add blocks to.
	/// Creates one if the last message isn't an assistant message.
	/// </summary>
	private void EnsureCurrentAssistantMessage()
	{
		if (_currentAssistantMessage != null) return;

		var last = Messages.Count > 0 ? Messages[^1] : null;
		if (last is { IsAssistant: true })
		{
			_currentAssistantMessage = last;
			return;
		}

		// Remove trailing progress messages
		while (Messages.Count > 0 && Messages[^1].IsProgress)
			Messages.RemoveAt(Messages.Count - 1);

		_currentAssistantMessage = new MessageEntryViewModel
		{
			Role = Constants.SessionFile.RoleAssistant,
			Content = string.Empty,
			Timestamp = DateTimeOffset.UtcNow,
		};
		Messages.Add(_currentAssistantMessage);
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
								permissionMode: _appSettings.Settings.DaemonPermissionMode,
								profile: SelectedDaemonProfile);
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
				ThinkingStatusText = "Claude is thinking\u2026";
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
		var trimmed = InputText.Trim();

		// Intercept slash commands — some handled client-side, others go to daemon
		if (trimmed.StartsWith('/') && !trimmed.Contains('\n'))
		{
			var cmdLower = trimmed.Split(' ', 2)[0].ToLowerInvariant();

			if (HandleClientSideCommand(cmdLower))
			{
				InputText = string.Empty;
				return;
			}

			if (cmdLower == "/login")
			{
				InputText = string.Empty;
				await HandleLoginCommandAsync();
				return;
			}

			if (cmdLower == "/mcp")
			{
				InputText = string.Empty;
				await HandleMcpCommandAsync();
				return;
			}

			if (cmdLower == "/usage")
			{
				InputText = string.Empty;
				await HandleUsageCommandAsync();
				return;
			}

			// Skills and API-bound commands go to daemon
			if (UseDaemon)
			{
				await ExecuteSlashCommandAsync(trimmed);
				return;
			}
		}

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
				ThinkingStatusText = "Claude is thinking\u2026";
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

	/// <summary>
	/// Handles built-in commands that the CLI processes locally (no API call).
	/// Returns true if the command was handled, false to fall through to daemon.
	/// </summary>
	private bool HandleClientSideCommand(string cmdLower)
	{
		string? response = cmdLower switch
		{
			"/help" => FormatHelpOutput(),
			"/cost" => string.IsNullOrEmpty(SessionCostText)
				? "No cost data yet. Send a message first."
				: $"Session cost: {SessionCostText}",
			"/context" => string.IsNullOrEmpty(ContextUsageText)
				? "No context data yet. Send a message first."
				: $"Context usage: {ContextUsageText}\nModel: {CurrentModelText}",
			"/model" => string.IsNullOrEmpty(CurrentModelText)
				? "Model not yet known. Send a message to see which model is used."
				: $"Current model: {CurrentModelText}\nOverride: {SelectedModelId ?? "none (using default)"}",
			"/status" => FormatStatusOutput(),
			"/clear" => null, // special handling below
			_ => null,
		};

		if (cmdLower == "/clear")
		{
			Messages.Add(new MessageEntryViewModel
			{
				Role = Constants.SessionFile.RoleSystem,
				Content = "Session context cleared.",
				Timestamp = DateTimeOffset.UtcNow,
			});
			_node.Model.ClaudeSessionId = null;
			_node.Model.ExternalId = null;
			_appSettings.Save();
			this.RaisePropertyChanged(nameof(CanClear));
			CurrentModelText = string.Empty;
			_sessionTotalCost = 0;
			SessionCostText = string.Empty;
			ContextUsageText = string.Empty;
			return true;
		}

		if (response == null)
			return false;

		Messages.Add(new MessageEntryViewModel
		{
			Role = Constants.SessionFile.RoleSystem,
			Content = $"{cmdLower}\n{response}",
			Timestamp = DateTimeOffset.UtcNow,
		});
		return true;
	}

	private string FormatHelpOutput()
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("Available commands:\n");
		var commands = AutocompleteVm.GetCommands();
		var builtins = commands.Where(c => c.Type == "builtin").ToList();
		var skills = commands.Where(c => c.Type == "skill").ToList();

		if (builtins.Count > 0)
		{
			foreach (var cmd in builtins)
				sb.AppendLine($"  /{cmd.Name,-16} {cmd.Description}");
		}
		if (skills.Count > 0)
		{
			sb.AppendLine("\nSkills:");
			foreach (var cmd in skills)
				sb.AppendLine($"  /{cmd.Name,-16} {cmd.Description}");
		}
		return sb.ToString().TrimEnd();
	}

	private string FormatStatusOutput()
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"Session:   {_node.Name}");
		sb.AppendLine($"Profile:   {SelectedDaemonProfile ?? "default"}");
		if (!string.IsNullOrEmpty(CurrentModelText))
			sb.AppendLine($"Model:     {CurrentModelText}");
		sb.AppendLine($"Effort:    {SelectedReasoningEffort ?? "default"}");
		if (!string.IsNullOrEmpty(ContextUsageText))
			sb.AppendLine($"Context:   {ContextUsageText}");
		if (!string.IsNullOrEmpty(SessionCostText))
			sb.AppendLine($"Cost:      {SessionCostText}");
		sb.AppendLine($"Directory: {WorkingDirectory}");
		return sb.ToString().TrimEnd();
	}

	private async System.Threading.Tasks.Task ExecuteSlashCommandAsync(string input)
	{
		// Parse "/command args" format
		var parts = input.Substring(1).Split(' ', 2);
		var command = parts[0];
		var args = parts.Length > 1 ? parts[1] : null;

		InputText = string.Empty;

		var projectPath = _node.Model.OriginalProjectPath ?? _node.Model.WorkingDirectory;

		Messages.Add(new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleSystem,
			Content   = $"/{command}{(args != null ? " " + args : "")}",
			Timestamp = DateTimeOffset.UtcNow,
		});

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

		try
		{
			_runEventSubscription?.Dispose();
			var pendingEvents = new List<TessynRunEvent>();
			_runEventSubscription = _runService!.RunEvents
				.Subscribe(evt =>
				{
					if (_activeRunId != null && evt.RunId == _activeRunId)
						HandleRunEvent(evt);
					else if (_activeRunId == null)
						pendingEvents.Add(evt);
				});

			var runId = await _daemonService!.CommandsExecuteAsync(
				command, args, _node.ExternalId, projectPath, SelectedDaemonProfile);

			_activeRunId = runId;

			foreach (var buffered in pendingEvents)
			{
				if (buffered.RunId == runId)
					HandleRunEvent(buffered);
			}
			pendingEvents.Clear();

			_runEventSubscription?.Dispose();
			_runEventSubscription = _runService.RunEvents
				.Where(e => e.RunId == runId)
				.Subscribe(HandleRunEvent);
		}
		catch (TessynRpcException ex) when (ex.Message.Contains("Unknown command"))
		{
			Dispatcher.UIThread.Post(() =>
			{
				Messages.Add(new MessageEntryViewModel
				{
					Role      = Constants.SessionFile.RoleSystem,
					Content   = $"Unknown command: /{command}",
					Timestamp = DateTimeOffset.UtcNow,
				});
			});
			FinishDaemonRun(success: false);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Failed to execute command /{Command}", command);
			Dispatcher.UIThread.Post(() =>
			{
				Messages.Add(new MessageEntryViewModel
				{
					Role      = Constants.SessionFile.RoleSystem,
					Content   = $"Command failed: {ex.Message}",
					Timestamp = DateTimeOffset.UtcNow,
				});
			});
			FinishDaemonRun(success: false);
		}
	}

	private async System.Threading.Tasks.Task HandleLoginCommandAsync()
	{
		var statusMsg = new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleSystem,
			Content   = "Launching Claude authentication...",
			Timestamp = DateTimeOffset.UtcNow,
		};
		Messages.Add(statusMsg);

		try
		{
			var claudePath = _appSettings.Settings.ClaudePath;

			// Determine config dir: use selected profile's dir, or default (~/.claude)
			var configDir = SelectedProfileConfigDir
				?? System.IO.Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".claude");

			// Launch visible auth login process
			await _profileService.LaunchAuthLoginAsync(claudePath, configDir);

			// Re-check auth via daemon if available
			if (_daemonService != null)
			{
				try
				{
					var authInfo = await _daemonService.AuthStatusAsync();
					Dispatcher.UIThread.Post(() =>
					{
						if (authInfo.LoggedIn)
						{
							statusMsg.Content = $"Logged in as {authInfo.Email}";

							// Clear the auth warning in main window
							if (Avalonia.Application.Current?.ApplicationLifetime
								is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
								&& desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
								mainVm.SetAuthStatus(true, authInfo.Email);
						}
						else
						{
							statusMsg.Content = "Authentication was not completed. Try again with /login or run  claude login  in a terminal.";
						}
					});
				}
				catch (Exception ex)
				{
					_log.Debug(ex, "Failed to verify auth after /login");
					Dispatcher.UIThread.Post(() =>
						statusMsg.Content = "Authentication window closed. If login succeeded, try sending a message.");
				}
			}
			else
			{
				Dispatcher.UIThread.Post(() =>
					statusMsg.Content = "Authentication window closed. If login succeeded, try sending a message.");
			}
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Failed to launch auth login");
			Dispatcher.UIThread.Post(() =>
				statusMsg.Content = $"Failed to launch authentication: {ex.Message}");
		}
	}

	private async System.Threading.Tasks.Task HandleMcpCommandAsync()
	{
		var externalId = _node.ExternalId;
		if (string.IsNullOrEmpty(externalId))
		{
			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = "/mcp\nSend a message first to initialize MCP servers.",
				Timestamp = DateTimeOffset.UtcNow,
			});
			return;
		}

		if (_daemonService == null)
		{
			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = "/mcp\nDaemon not connected.",
				Timestamp = DateTimeOffset.UtcNow,
			});
			return;
		}

		try
		{
			var result = await _daemonService.McpListAsync(externalId);
			var sb = new StringBuilder();
			sb.AppendLine("/mcp");
			sb.AppendLine("MCP Servers:");

			if (result.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
			{
				foreach (var server in servers.EnumerateArray())
				{
					var name = server.TryGetProperty("name", out var n) ? n.GetString() ?? "unknown" : "unknown";
					var status = server.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
					var toolCount = server.TryGetProperty("toolCount", out var tc) ? tc.GetInt32() : 0;

					var indicator = status switch
					{
						"connected" => "\u25cf",
						"error" => "\u2717",
						_ => "\u25cb",
					};

					sb.AppendLine($"  {indicator} {name,-28} {status,-12} ({toolCount} tools)");
				}
			}
			else
			{
				sb.AppendLine("  No MCP servers configured.");
			}

			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = sb.ToString().TrimEnd(),
				Timestamp = DateTimeOffset.UtcNow,
			});
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to get MCP list");
			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = $"/mcp\nFailed to get MCP list: {ex.Message}",
				Timestamp = DateTimeOffset.UtcNow,
			});
		}
	}

	private async System.Threading.Tasks.Task HandleUsageCommandAsync()
	{
		if (_daemonService == null)
		{
			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = "/usage\nDaemon not connected.",
				Timestamp = DateTimeOffset.UtcNow,
			});
			return;
		}

		try
		{
			var result = await _daemonService.UsageGetAsync(SelectedDaemonProfile);
			var sb = new StringBuilder();
			sb.AppendLine("/usage");

			// Rate limit info — fields: rateLimit.type, rateLimit.status, rateLimit.resetsAt (unix ms)
			if (result.TryGetProperty("rateLimit", out var rl) && rl.ValueKind == JsonValueKind.Object)
			{
				var limitType = rl.TryGetProperty("type", out var lt) ? lt.GetString() ?? "unknown" : "unknown";
				var status = rl.TryGetProperty("status", out var st) ? st.GetString() ?? "unknown" : "unknown";

				var resetText = "";
				if (rl.TryGetProperty("resetsAt", out var ra) && ra.ValueKind == JsonValueKind.Number)
				{
					var resetsAtMs = ra.GetInt64();
					var resetTime = DateTimeOffset.FromUnixTimeMilliseconds(resetsAtMs);
					var remaining = resetTime - DateTimeOffset.UtcNow;
					if (remaining.TotalSeconds > 0)
					{
						resetText = remaining.TotalHours >= 1
							? $", resets in {(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
							: $", resets in {remaining.Minutes}m {remaining.Seconds:D2}s";
					}
				}

				var typeLabel = limitType.Replace("_", " ");
				sb.AppendLine($"Rate limit: {typeLabel}, {status}{resetText}");
			}

			// Token usage — top-level fields: inputTokens, outputTokens, cacheReadInputTokens, etc.
			{
				var input = result.TryGetProperty("inputTokens", out var ti) ? ti.GetInt64() : 0;
				var output = result.TryGetProperty("outputTokens", out var to2) ? to2.GetInt64() : 0;
				var cacheRead = result.TryGetProperty("cacheReadInputTokens", out var cr) ? cr.GetInt64() : 0;
				var cacheCreated = result.TryGetProperty("cacheCreationInputTokens", out var cc) ? cc.GetInt64() : 0;

				var parts = new List<string>();
				if (cacheRead > 0) parts.Add($"{FormatTokenCount(cacheRead)} cache read");
				if (cacheCreated > 0) parts.Add($"{FormatTokenCount(cacheCreated)} cache created");
				var cacheInfo = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";

				sb.AppendLine($"Tokens: {FormatTokenCount(input)} in / {FormatTokenCount(output)} out{cacheInfo}");
			}

			// Cost — top-level field: totalCostUsd
			if (result.TryGetProperty("totalCostUsd", out var cost) && cost.ValueKind == JsonValueKind.Number)
			{
				sb.AppendLine($"Cost: ${cost.GetDouble():F4}");
			}

			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = sb.ToString().TrimEnd(),
				Timestamp = DateTimeOffset.UtcNow,
			});
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to get usage info");
			Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = $"/usage\nFailed to get usage info: {ex.Message}",
				Timestamp = DateTimeOffset.UtcNow,
			});
		}
	}

	private static string FormatTokenCount(long tokens)
	{
		if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:F1}M";
		if (tokens >= 1_000) return $"{tokens / 1_000.0:F1}k";
		return tokens.ToString();
	}

	/// <summary>Launch auth login for an unauthenticated daemon profile, then reload profiles.</summary>
	private async System.Threading.Tasks.Task HandleLoginForProfileAsync(TessynProfile profile)
	{
		var statusMsg = new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleSystem,
			Content   = $"Launching authentication for profile '{profile.Name}'...",
			Timestamp = DateTimeOffset.UtcNow,
		};
		Messages.Add(statusMsg);

		try
		{
			var claudePath = _appSettings.Settings.ClaudePath;
			await _profileService.LaunchAuthLoginAsync(claudePath, profile.ConfigDir ?? System.IO.Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"));

			// Reload profiles to pick up the new auth state
			await LoadDaemonProfilesAsync();

			// Check if it worked
			var p = _daemonProfiles.FirstOrDefault(dp => dp.Name == profile.Name);
			Dispatcher.UIThread.Post(() =>
			{
				statusMsg.Content = p?.Auth is { LoggedIn: true }
					? $"Logged in as {p.Auth.Email}"
					: "Authentication was not completed.";
			});
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Failed to launch auth for profile {Profile}", profile.Name);
			Dispatcher.UIThread.Post(() =>
				statusMsg.Content = $"Failed to launch authentication: {ex.Message}");
		}
	}

	/// <summary>Add a new daemon profile: create config dir, register with daemon, launch auth.</summary>
	private async System.Threading.Tasks.Task HandleAddDaemonProfileAsync()
	{
		// Generate a unique profile name and config dir
		var baseName = "account";
		var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var name = baseName;
		var configDir = System.IO.Path.Combine(baseDir, $".claude-{name}");
		var counter = 2;
		while (System.IO.Directory.Exists(configDir))
		{
			name = $"{baseName}{counter}";
			configDir = System.IO.Path.Combine(baseDir, $".claude-{name}");
			counter++;
		}

		var statusMsg = new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleSystem,
			Content   = "Adding new account — launching authentication...",
			Timestamp = DateTimeOffset.UtcNow,
		};
		Messages.Add(statusMsg);

		try
		{
			// Create the config dir
			System.IO.Directory.CreateDirectory(configDir);

			// Register with daemon
			await _daemonService!.ProfilesAddAsync(name, configDir, default);

			// Launch auth
			var claudePath = _appSettings.Settings.ClaudePath;
			await _profileService.LaunchAuthLoginAsync(claudePath, configDir);

			// Reload profiles
			await LoadDaemonProfilesAsync();

			var p = _daemonProfiles.FirstOrDefault(dp => dp.Name == name);
			Dispatcher.UIThread.Post(() =>
			{
				if (p?.Auth is { LoggedIn: true })
				{
					statusMsg.Content = $"Account added: {p.Auth.Email}";
					// Auto-select the new profile
					var idx = _daemonProfiles.IndexOf(p);
					if (idx >= 0)
					{
						_selectedDaemonProfileIndex = idx;
						this.RaisePropertyChanged(nameof(SelectedDaemonProfileIndex));
						_appSettings.Settings.DaemonProfile = name;
						_appSettings.Save();
					}
				}
				else
				{
					statusMsg.Content = "Authentication was not completed. The profile was created but not logged in.";
				}
			});
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Failed to add daemon profile");
			Dispatcher.UIThread.Post(() =>
				statusMsg.Content = $"Failed to add account: {ex.Message}");
		}
	}

	private async System.Threading.Tasks.Task LoadDaemonProfilesAsync()
	{
		try
		{
			var result = await _daemonService!.ProfilesListAsync(checkAuth: true);
			_daemonProfiles = result.Profiles;

			Dispatcher.UIThread.Post(() =>
			{
				DaemonProfileNames.Clear();
				var savedProfile = _appSettings.Settings.DaemonProfile;
				var selectedIdx = 0;
				var firstAuthIdx = -1;

				for (var i = 0; i < _daemonProfiles.Count; i++)
				{
					var p = _daemonProfiles[i];
					string label;
					if (p.Auth is { LoggedIn: true, Email: not null })
					{
						var sub = p.Auth.SubscriptionType;
						label = !string.IsNullOrEmpty(sub)
							? $"{p.Auth.Email} ({char.ToUpperInvariant(sub[0])}{sub.Substring(1)})"
							: p.Auth.Email;
						if (firstAuthIdx < 0) firstAuthIdx = i;
					}
					else
					{
						label = $"{p.Name} (login)";
					}
					DaemonProfileNames.Add(label);

					if (savedProfile != null && p.Name == savedProfile)
						selectedIdx = i;
				}

				// If no saved preference, prefer the first authenticated profile
				if (savedProfile == null && firstAuthIdx >= 0)
					selectedIdx = firstAuthIdx;

				// Always add "Add account..." as the last entry
				DaemonProfileNames.Add("Add account...");

				_selectedDaemonProfileIndex = selectedIdx;
				this.RaisePropertyChanged(nameof(SelectedDaemonProfileIndex));
			});
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Failed to load daemon profiles");
		}
	}

	private async System.Threading.Tasks.Task LoadCommandsAsync()
	{
		try
		{
			var workDir = WorkingDirectory;
			if (string.IsNullOrEmpty(workDir)) return;

			var result = await _daemonService!.CommandsListAsync(workDir);
			AutocompleteVm.SetCommands(result.Commands);
			_log.Debug("Loaded {Count} commands for autocomplete", result.Commands.Count);
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Failed to load commands from daemon");
		}
	}

	/// <summary>
	/// Formats a raw model ID like "claude-opus-4-6[1m]" into a human-friendly name like "Opus 4.6 (1M)".
	/// </summary>
	private static string FormatModelName(string modelId)
	{
		// Strip "claude-" prefix
		var s = modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)
			? modelId.Substring(7) : modelId;

		// Extract context window suffix like "[1m]"
		string ctx = "";
		var bracketIdx = s.IndexOf('[');
		if (bracketIdx >= 0)
		{
			ctx = s.Substring(bracketIdx).Trim('[', ']').ToUpperInvariant();
			s = s.Substring(0, bracketIdx);
		}

		// Parse "opus-4-6" → "Opus 4.6"
		var parts = s.Split('-');
		if (parts.Length >= 1)
		{
			var name = char.ToUpperInvariant(parts[0][0]) + parts[0].Substring(1);
			var version = parts.Length >= 3
				? $" {parts[1]}.{parts[2]}"
				: parts.Length >= 2 ? $" {parts[1]}" : "";
			var ctxSuffix = !string.IsNullOrEmpty(ctx) ? $" ({ctx})" : "";
			return $"{name}{version}{ctxSuffix}";
		}

		return modelId;
	}

	private void UpdateSessionCost(decimal cost)
	{
		_sessionTotalCost += cost;
		SessionCostText = $"${_sessionTotalCost:F4}";
	}

	private void UpdateContextUsage(int inputTokens)
	{
		if (inputTokens <= 0) return;
		ContextUsageText = inputTokens < 1000
			? $"ctx: {inputTokens}"
			: $"ctx: {inputTokens / 1000.0:F0}k";
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

	/// <summary>
	/// Strips the hidden instruction block from a user message loaded from history.
	/// The CLI stores the full augmented prompt; we don't want to display the instructions.
	/// </summary>
	private static string StripInstructionBlock(string content)
	{
		// The delimiter is "\n\n---\n[Additional instructions..."
		var delimIdx = content.IndexOf("\n---\n[Additional instructions", StringComparison.Ordinal);
		if (delimIdx >= 0)
			return content[..delimIdx].TrimEnd();
		return content;
	}

	/// <summary>Builds the hidden instruction block appended to the user's message for claude stdin (FR.11.9).</summary>
	private string BuildInstructionBlock()
	{
		var sb = new StringBuilder();

		// Auto-commit: inject only if not neutral
		if (_autoCommitState > 0)
			sb.AppendLine($"- {Constants.Instructions.AutoCommitOn}");
		else if (_autoCommitState < 0)
			sb.AppendLine($"- {Constants.Instructions.AutoCommitOff}");
		// neutral (0): inject nothing about committing

		if (_isNewBranch)
			sb.AppendLine($"- {Constants.Instructions.NewBranch}");

		if (IsAutoDocument)
			sb.AppendLine($"- {Constants.Instructions.AutoDocument}");

		if (_pendingClear)
			sb.AppendLine($"- {Constants.Instructions.Clear}");

		// Don't build the delimiter/block at all if no instructions were added
		if (sb.Length == 0)
			return string.Empty;

		return Constants.Instructions.Delimiter + "\n" + sb.ToString();
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
			Content   = entry.Role == Constants.SessionFile.RoleUser
				? StripInstructionBlock(entry.Content)
				: entry.Content,
			Timestamp = entry.Timestamp,
		};
}
