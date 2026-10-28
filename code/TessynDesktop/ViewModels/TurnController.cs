using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Owns turn sending (interactive send + streaming), the thinking timer, mid-run corrections,
/// compaction, and JSONL detach for a single session (FR.11.2, FR.11.6-11.10, FR.11.12, FR.14.3).
/// Deliberately drives <see cref="IClaudeProcessManager"/> directly for live token streaming rather
/// than <c>IDaemonClient.RunTurnAsync</c> (the headless/orchestration path) — the two coordinate only
/// via the shared <see cref="IDaemonClient.GetTurnLock"/>. Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnController : ReactiveObject, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<TurnController>();

	private readonly SessionNodeViewModel _node;
	private readonly ISessionFileService _fileService;
	private readonly IDraftService _draftService;
	private readonly IClaudeProcessManager _processManager;
	private readonly IClaudeStreamingSessionManager _streamingManager;
	private readonly IAppSettingsService _appSettings;
	private readonly IAgentMcpServer _mcpServer;
	private readonly IDaemonClient _daemonClient;
	private readonly ITelegramBridgeService _telegramBridge;
	private readonly IClaudeSessionStatusService _sessionStatus;
	private readonly ISessionSoundService _soundService;
	private readonly SessionContentViewModel _content;
	private readonly ModelSelectionController _models;
	private readonly ProfileController _profiles;
	private readonly SessionPromptComposer _composer;

	private string? _pendingModelLabel;       // UI thread only (first assistant VM)
	private string? _pendingFileModelId;      // cross-thread; use Interlocked
	private string? _pendingFileEffort;       // cross-thread; use Interlocked
	private bool _isBusy;
	private bool _isNodeRunning;
	private string _thinkingDuration = string.Empty;
	private DispatcherTimer? _thinkingTimer;
	private DateTimeOffset _thinkingStartedAt;
	private int _busyCount;
	private bool _needsContextRetry;
	private bool _needsAuthRaceRetry;

	// FR.21.22 — error-result text of the send currently holding the turn lock, reported via
	// ISessionFileService.NotifyTurnCompleted. Only written by HandleStreamEvent while a send holds
	// the lock (sends are serialized per node), reset when a send acquires it and on every retry.
	private string? _sendErrorText;
	private readonly object _ctsLock = new();
	private readonly List<CancellationTokenSource> _activeCts = new();
	private bool _isNewBranch;
	private bool _isAutoCompact;
	private bool _midRunAutoCompactState;
	private bool _anyCancelledInBatch;

	public bool IsBusy
	{
		get => _isBusy;
		private set
		{
			this.RaiseAndSetIfChanged(ref _isBusy, value);
			this.RaisePropertyChanged(nameof(CanStop));
		}
	}

	/// <summary>True while the session's node has an orchestrated turn running (set by SessionTurnService).</summary>
	public bool IsNodeRunning
	{
		get => _isNodeRunning;
		private set
		{
			this.RaiseAndSetIfChanged(ref _isNodeRunning, value);
			this.RaisePropertyChanged(nameof(CanStop));
		}
	}

	/// <summary>
	/// True when the Stop button should be shown: a local send is in flight, the node has an
	/// active orchestrated turn, or a Streaming Session Mode process is alive for this session
	/// (FR.19.4) — shown for the process's whole lifetime, not only while it is actively streaming
	/// output, since the app cannot know in advance whether more output (a background task, a
	/// native schedule) is still pending.
	/// </summary>
	public bool CanStop => IsBusy || IsNodeRunning || IsStreamingProcessAlive;

	/// <summary>True when a Streaming Session Mode process is currently alive for this session (FR.19.3).</summary>
	public bool IsStreamingProcessAlive => _streamingManager.IsAlive(_node.Model.NodeId);

	/// <summary>
	/// Streaming Session Mode toggle (FR.19.1, FR.19.6): sticky, persisted on the session node.
	/// Turning it OFF while a process is alive does not apply immediately — it raises
	/// <see cref="StreamingDisableConfirmationRequested"/> instead, and the change only takes
	/// effect if the caller (the View, after user confirmation) calls <see cref="ConfirmStreamingModeOff"/>.
	/// </summary>
	public bool IsStreamingMode
	{
		get => _node.Model.IsStreamingMode;
		set
		{
			if (value == _node.Model.IsStreamingMode) return;

			if (!value && IsStreamingProcessAlive)
			{
				StreamingDisableConfirmationRequested?.Invoke();
				this.RaisePropertyChanged(); // re-pulls the still-true value, reverting the bound toggle
				return;
			}

			_node.Model.IsStreamingMode = value;
			_appSettings.Save();
			this.RaisePropertyChanged();
		}
	}

	/// <summary>Raised when the user tries to turn Streaming Session Mode off while its process is alive (FR.19.7).</summary>
	public event Action? StreamingDisableConfirmationRequested;

	/// <summary>
	/// Session Completion Sound toggle (FR.22.2): sticky, persisted on the session node, default on.
	/// When true, <see cref="ISessionSoundService.PlayCompletionSound"/> fires each time this
	/// session's "Claude is thinking…" state clears on natural completion (not on Stop).
	/// </summary>
	public bool PlayCompletionSound
	{
		get => _node.Model.PlayCompletionSound;
		set
		{
			if (value == _node.Model.PlayCompletionSound) return;
			_node.Model.PlayCompletionSound = value;
			_appSettings.Save();
			this.RaisePropertyChanged();
		}
	}

	/// <summary>Called by the View after the user confirms turning Streaming Session Mode off (FR.19.7). Kills the process and applies the toggle.</summary>
	public void ConfirmStreamingModeOff()
	{
		_streamingManager.Stop(_node.Model.NodeId);
		_node.Model.IsStreamingMode = false;
		_appSettings.Save();
		NotifyStreamingStateChanged();
	}

	private void NotifyStreamingStateChanged()
	{
		this.RaisePropertyChanged(nameof(IsStreamingMode));
		this.RaisePropertyChanged(nameof(IsStreamingProcessAlive));
		this.RaisePropertyChanged(nameof(CanStop));
	}

	public string ThinkingDuration
	{
		get => _thinkingDuration;
		private set => this.RaiseAndSetIfChanged(ref _thinkingDuration, value);
	}

	/// <summary>True when the session has a live ClaudeSessionId that can be cleared.</summary>
	public bool CanClear => _node.Model.ClaudeSessionId is not null;

	/// <summary>
	/// Per-session sticky tri-state toggle (FR.11.3): <c>true</c> = Commit, <c>false</c> =
	/// Do-not-commit, <c>null</c> = No preference. Persisted in appsettings.json.
	/// </summary>
	public bool? IsAutoCommit
	{
		get => _node.Model.IsAutoCommit;
		set
		{
			var oldValue = _node.Model.IsAutoCommit;
			_node.Model.IsAutoCommit = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
			if (IsBusy && value != oldValue)
				SendMidRunAutoCommitCorrection(value);
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
				_content.AddSystemMessage($"[AutoCompact was {label} for this run]");
				_log.Information("Mid-run AutoCompact toggle: {State}", label);
			}
		}
	}

	public TurnController(
		SessionNodeViewModel node,
		ISessionFileService fileService,
		IDraftService draftService,
		IClaudeProcessManager processManager,
		IClaudeStreamingSessionManager streamingManager,
		IAppSettingsService appSettings,
		IAgentMcpServer mcpServer,
		IDaemonClient daemonClient,
		ITelegramBridgeService telegramBridge,
		IClaudeSessionStatusService sessionStatus,
		ISessionSoundService soundService,
		SessionContentViewModel content,
		ModelSelectionController models,
		ProfileController profiles,
		SessionPromptComposer composer)
	{
		_node             = node;
		_fileService      = fileService;
		_draftService     = draftService;
		_processManager   = processManager;
		_streamingManager = streamingManager;
		_appSettings      = appSettings;
		_mcpServer        = mcpServer;
		_daemonClient     = daemonClient;
		_telegramBridge   = telegramBridge;
		_sessionStatus    = sessionStatus;
		_soundService     = soundService;
		_content          = content;
		_models           = models;
		_profiles         = profiles;
		_composer         = composer;

		node.WhenAnyValue(x => x.IsRunning).Subscribe(running => IsNodeRunning = running);
		_streamingManager.ProcessExited += OnStreamingProcessExited;
	}

	private void OnStreamingProcessExited(string nodeId)
	{
		if (nodeId != _node.Model.NodeId) return;
		Dispatcher.UIThread.Post(NotifyStreamingStateChanged);
	}

	public void Dispose()
	{
		_streamingManager.ProcessExited -= OnStreamingProcessExited;
		lock (_ctsLock) { foreach (var c in _activeCts) c.Dispose(); _activeCts.Clear(); }
	}

	/// <summary>
	/// Cancels an in-flight local send, requests cancellation of an orchestrated turn, or kills an
	/// alive Streaming Session Mode process (FR.19.5, bound to StopCommand). The streaming case
	/// takes priority since it terminates the whole process rather than just the current turn.
	/// </summary>
	public void Stop()
	{
		// Immediate UX safety valve: DetectExternalActivity (SessionContentViewModel) is the only
		// other writer of this flag and is purely reactive to JSONL file-change events, so it can
		// get stuck true if the process dies without writing a terminal "result" entry. Clearing it
		// here is safe even if a genuinely separate external process is still running — the very
		// next real JSONL write flips it back to true through the normal detection path.
		_node.IsExternallyActive = false;

		if (IsStreamingProcessAlive)
		{
			_streamingManager.Stop(_node.Model.NodeId);
			_log.Information("Stop requested — terminating streaming process for session {FileName}", _node.FileName);
		}
		else if (IsBusy)
		{
			List<CancellationTokenSource> snapshot;
			lock (_ctsLock) snapshot = [.._activeCts];
			foreach (var c in snapshot) c.Cancel();
			_log.Information("Stop requested for session {FileName}", _node.FileName);
		}
		else if (IsNodeRunning)
		{
			_daemonClient.CancelTurn(_node.Model.NodeId);
			_log.Information("Stop requested for orchestrated turn on node {NodeId}", _node.Model.NodeId);
		}
	}

	public async Task SendAsync(string inputText)
	{
		var message = inputText.Trim();
		if (string.IsNullOrEmpty(message) && !_isAutoCompact)
			return;

		await SendAsyncCore(message);
	}

	/// <summary>Raised after InputText should be cleared (the send has captured the message and started).</summary>
	public event Action? InputCleared;

	/// <summary>
	/// Schedules the message as a delayed self-wake turn (FR.14.4, FR.14.5) instead of sending it
	/// immediately — the UI counterpart to the schedule_wake MCP tool, reusing the exact same
	/// scheduling engine (right-click on Send, SessionView). Clears the input box the same way an
	/// immediate send does; the instruction block, context preamble, and everything else a normal
	/// turn needs is built later by SessionTurnService.RunTurnAsync when the schedule actually fires
	/// — nothing about that path needs to change to support this.
	/// </summary>
	public void ScheduleSend(string inputText, TimeSpan delay)
	{
		var message = inputText.Trim();
		if (string.IsNullOrEmpty(message))
			return;

		InputCleared?.Invoke();
		_draftService.DeleteDraft(_node.FileName);

		var fireAtUtc = DateTimeOffset.UtcNow.Add(delay);
		_daemonClient.AddSchedule(new ScheduleModel
		{
			ScheduleId   = Guid.NewGuid().ToString("N")[..8],
			TargetNodeId = _node.Model.NodeId,
			Prompt       = message,
			Note         = Constants.Agent.DelayedSendNote,
			Kind         = ScheduleKind.Delay,
			FireAtUtc    = fireAtUtc,
		});

		_content.AddSystemMessage($"[Delayed send scheduled — fires at {fireAtUtc.LocalDateTime:yyyy-MM-dd HH:mm}]");
		_log.Information("Delayed send scheduled for session {FileName}: fires at {FireAt}", _node.FileName, fireAtUtc);
	}

	private async Task SendAsyncCore(string message)
	{
		InputCleared?.Invoke();
		_draftService.DeleteDraft(_node.FileName);

		// Auto-Compact can be triggered with an empty prompt: skip straight to compaction (no turn sent).
		if (string.IsNullOrEmpty(message) && _isAutoCompact)
		{
			await SendCompactionPromptAsync();
			IsAutoCompact = false;
			return;
		}

		// Switch back to Tessyn view if currently showing Claude's JSONL
		if (_content.IsClaudeSessionView)
			_content.IsClaudeSessionView = false;

		// Capture one-shot toggle states before resetting them
		var wasNewBranch = _isNewBranch;
		_midRunAutoCompactState = _isAutoCompact;

		// Reset one-shot toggles immediately
		if (wasNewBranch) IsNewBranch = false;

		_busyCount++;
		IsBusy = true;
		_node.IsRunning = true;

		// FR.21.2 — lazily ensure a Telegram topic exists for this session on its first interactive
		// activity since the bridge was enabled. Fire-and-forget: never throws, never delays the turn.
		_ = _telegramBridge.EnsureTopicForSessionAsync(_node.Model);

		// Start timer only for the first concurrent send; subsequent sends keep the running clock
		if (_thinkingTimer == null)
		{
			_thinkingStartedAt = DateTimeOffset.UtcNow;
			ThinkingDuration = "0:00";
			_anyCancelledInBatch = false;
			_thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			_thinkingTimer.Tick += OnThinkingTimerTick;
			_thinkingTimer.Start();
		}

		// Store only the clean user message in file and UI (FR.11.2)
		var profileName = _profiles.SelectedProfileDisplayName;
		_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleUser, message, profileName: profileName);
		var now = DateTimeOffset.UtcNow;
		_content.Messages.Add(new MessageEntryViewModel
		{
			Role        = Constants.SessionFile.RoleUser,
			Content     = message,
			Timestamp   = now,
			ProfileName = profileName,
		});
		_pendingModelLabel = SessionPromptComposer.BuildModelLabel(_models.SelectedModelId, _models.SelectedEffort);
		Interlocked.Exchange(ref _pendingFileModelId, _models.SelectedModelId ?? "default");
		Interlocked.Exchange(ref _pendingFileEffort,  _models.SelectedEffort  ?? "default");
		_node.LastPromptTime = now.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
		_node.LastPromptTimestamp = now;
		_node.ProfileName = profileName;

		_content.Messages.Add(new MessageEntryViewModel
		{
			Role       = Constants.SessionFile.RoleSystem,
			Content    = "Claude is thinking...",
			Timestamp  = DateTimeOffset.UtcNow,
			IsProgress = true,
		});

		// Build augmented message with hidden instructions (FR.11.2, FR.11.9). IsNewSession reflects
		// whether this turn is about to start a fresh claude process (no ClaudeSessionId yet).
		var sessionId = _node.Model.ClaudeSessionId;

		// Cross-profile session adoption (FR.11.13): if this session was created under a different
		// profile than the one now active, locate and move its JSONL into the active profile's
		// store before attempting --resume, rather than falling straight to text reconstruction.
		if (sessionId != null)
			_sessionStatus.TryAdoptSessionFromOtherProfile(_node.Model.WorkingDirectory, sessionId, _profiles.SelectedProfileConfigDir);

		var instructionBlock = _composer.BuildInstructionBlock(IsAutoCommit, wasNewBranch, IsAutoDocument, sessionId == null, _node.Model.IsStreamingMode);
		var augmentedMessage = message + instructionBlock;

		// Proactive context reload (FR.11.10): if file has history but no session ID, wrap with context
		var messageToSend = augmentedMessage;
		if (sessionId == null)
		{
			var entries = _fileService.ReadEntries(_node.FileName);
			var priorEntries = entries
				.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
				.ToList();
			if (priorEntries.Count > 1) // More than just the current prompt
			{
				messageToSend = _composer.BuildContextPreamble(augmentedMessage);
				_log.Information("Proactive context reload for session {FileName}", _node.FileName);
			}
		}

		var cts = new CancellationTokenSource();
		lock (_ctsLock) _activeCts.Add(cts);
		var ct = cts.Token;

		// Local Tool Profile (FR.12.21): a tool-capable local Ollama model gets the directory's
		// curated --tools list plus a filtered, --strict-mcp-config'd --mcp-config instead of the
		// unrestricted Anthropic default. LocalToolProfileResolver is the single source of truth
		// shared with the headless SessionTurnService path.
		var localProfile = LocalToolProfileResolver.Resolve(_models.SelectedModelInfo, _models.DirectoryModel);

		string? mcpConfigPath = null;
		var strictMcpConfig = false;
		LocalToolSpec? localToolSpec = null;

		if (localProfile != null)
		{
			strictMcpConfig = true;
			localToolSpec   = localProfile.ToolSpec;
			mcpConfigPath   = await _mcpServer.EnsureConfigFileAsync(
				_node.Model.NodeId, _node.Model.AgentToken, localProfile.IncludeCmxMcp, localProfile.IncludedExternalMcpServerIds);
		}
		// FR.14.3 / FR.14.11 — ensure the per-node MCP config file exists so the claude process
		// receives --mcp-config and can reach the host scheduling/orchestration tools.
		// MCP tools are a separate mechanism from the built-in --tools flag — the CLI still sends
		// them to the model even when --tools "" is set — so skip --mcp-config entirely for models
		// that don't support tool calling (e.g. tool-incapable local Ollama models).
		else if (_appSettings.Settings.AgentToolsEnabled && !_models.SelectedModelDisablesTools)
			mcpConfigPath = await _mcpServer.EnsureConfigFileAsync(_node.Model.NodeId, _node.Model.AgentToken);

		// Serialize only the CLI dispatch, not the UI updates above. This preserves the
		// ability to queue a follow-up prompt while a previous turn is still in flight —
		// the input clears and the message appears in the pane immediately, and this
		// send's CLI spawn waits behind the in-flight one to avoid concurrent --resume.
		var nodeId   = _node.Model.NodeId;
		var turnLock = string.IsNullOrEmpty(nodeId) ? null : _daemonClient.GetTurnLock(nodeId);
		bool lockAcquired = false;
		var sendCancelled = false;
		string? sendErrorText = null;

		try
		{
			if (turnLock != null)
			{
				await turnLock.WaitAsync(ct);
				lockAcquired = true;
			}
			_sendErrorText = null;

			if (_node.Model.IsStreamingMode)
			{
				// Streaming Session Mode (FR.19): no --resume "No conversation found" retry path —
				// the same open process carries the conversation, so context-retry doesn't apply.
				await SendViaStreamingAsync(sessionId, messageToSend, mcpConfigPath, localToolSpec, strictMcpConfig, ct);
			}
			else
			{
				await _processManager.SendMessageAsync(
					workingDirectory: _node.Model.WorkingDirectory,
					claudePath:       _appSettings.Settings.ClaudePath,
					sessionId:        sessionId,
					userMessage:      messageToSend,
					onEvent:          HandleStreamEvent,
					model:            _models.SelectedModelId,
					profileConfigDir: _profiles.SelectedProfileConfigDir,
					effort:           _models.SelectedEffort,
					mcpConfigPath:    mcpConfigPath,
					ollamaBaseUrl:    _models.SelectedLocalBaseUrl,
					disableTools:     _models.SelectedModelDisablesTools,
					chrome:           _models.IsChromeEnabled,
					disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
					localToolSpec:    localToolSpec,
					strictMcpConfig:  strictMcpConfig,
					cancellationToken: ct);

				if (_needsAuthRaceRetry)
				{
					_needsAuthRaceRetry = false;
					_log.Information("Retrying turn after transient OAuth refresh race for session {FileName}", _node.FileName);
					await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);

					await _processManager.SendMessageAsync(
						workingDirectory: _node.Model.WorkingDirectory,
						claudePath:       _appSettings.Settings.ClaudePath,
						sessionId:        sessionId,
						userMessage:      messageToSend,
						onEvent:          HandleStreamEvent,
						model:            _models.SelectedModelId,
						profileConfigDir: _profiles.SelectedProfileConfigDir,
						effort:           _models.SelectedEffort,
						mcpConfigPath:    mcpConfigPath,
						ollamaBaseUrl:    _models.SelectedLocalBaseUrl,
						disableTools:     _models.SelectedModelDisablesTools,
						chrome:           _models.IsChromeEnabled,
						disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
						localToolSpec:    localToolSpec,
						strictMcpConfig:  strictMcpConfig,
						cancellationToken: ct);
				}

				if (_needsContextRetry)
				{
					_needsContextRetry = false;
					var enrichedMessage = _composer.BuildContextPreamble(augmentedMessage);

					Dispatcher.UIThread.Post(() =>
					{
						var last = _content.Messages.Count > 0 ? _content.Messages[^1] : null;
						if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
							last.Content = "Resuming session with conversation history...";
						else
							_content.Messages.Add(new MessageEntryViewModel
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
						model:            _models.SelectedModelId,
						profileConfigDir: _profiles.SelectedProfileConfigDir,
						effort:           _models.SelectedEffort,
						mcpConfigPath:    mcpConfigPath,
						ollamaBaseUrl:    _models.SelectedLocalBaseUrl,
						disableTools:     _models.SelectedModelDisablesTools,
						chrome:           _models.IsChromeEnabled,
						disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
						localToolSpec:    localToolSpec,
						strictMcpConfig:  strictMcpConfig,
						cancellationToken: ct);
				}
			}

			// Post-response: handle Auto-Compact (FR.11.6)
			// Use _midRunAutoCompactState which reflects any mid-run toggle changes
			if (_midRunAutoCompactState)
			{
				await SendCompactionPromptAsync();
				IsAutoCompact = false;
			}
		}
		catch (OperationCanceledException)
		{
			_anyCancelledInBatch = true;
			sendCancelled = true;
			_log.Information("Send interrupted by user for session {FileName}", _node.FileName);
			_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, "[Interrupted by user]");
			_content.Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = "[Interrupted by user]",
				Timestamp = DateTimeOffset.UtcNow,
			});
		}
		finally
		{
			// FR.21.22 — once per send, after its last entry is appended and before the lock is
			// released (the next queued send resets _sendErrorText as soon as it acquires the lock).
			if (lockAcquired) sendErrorText = sendCancelled ? null : _sendErrorText;
			try
			{
				_fileService.NotifyTurnCompleted(_node.FileName, sendCancelled, sendErrorText);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "TurnCompleted handler failed for session {FileName}", _node.FileName);
			}

			if (lockAcquired) turnLock?.Release();
			lock (_ctsLock) _activeCts.Remove(cts);
			cts.Dispose();

			_busyCount = Math.Max(0, _busyCount - 1);
			if (_busyCount == 0)
			{
				// Update JSONL entry count before IsBusy goes false, so the watcher
				// doesn't re-detect entries we already processed during this send cycle.
				_content.RefreshJsonlEntryCount();

				var t = _thinkingTimer;
				_thinkingTimer = null;
				t?.Stop();
				ThinkingDuration = string.Empty;
				IsBusy = false;
				_node.IsRunning = false;

				// FR.22.1: only on natural completion of the whole batch, never after user Stop.
				if (!_anyCancelledInBatch && _node.Model.PlayCompletionSound)
					_soundService.PlayCompletionSound(_node.Model.NodeId);
			}
		}
	}

	/// <summary>
	/// Sends one turn via Streaming Session Mode (FR.19): ensures the session's persistent process
	/// is running (spawning it with the current model/profile/effort/mcp config if this is the
	/// first turn), then writes the turn to its open stdin and awaits the next result event.
	/// <see cref="HandleStreamEvent"/> is reused unchanged — it processes a <see cref="ClaudeStreamEvent"/>
	/// the same way regardless of whether it came from a one-shot or a streaming process.
	/// </summary>
	private async Task SendViaStreamingAsync(string? sessionId, string messageToSend, string? mcpConfigPath,
		LocalToolSpec? localToolSpec, bool strictMcpConfig, CancellationToken ct)
	{
		var nodeId = _node.Model.NodeId;
		var spawnParams = new StreamingSpawnParams(
			WorkingDirectory: _node.Model.WorkingDirectory,
			ClaudePath:       _appSettings.Settings.ClaudePath,
			SessionId:        sessionId,
			ProfileConfigDir: _profiles.SelectedProfileConfigDir,
			Model:            _models.SelectedModelId,
			Effort:           _models.SelectedEffort,
			McpConfigPath:    mcpConfigPath,
			OllamaBaseUrl:    _models.SelectedLocalBaseUrl,
			DisableTools:     _models.SelectedModelDisablesTools,
			Chrome:           _models.IsChromeEnabled,
			LocalToolSpec:    localToolSpec,
			StrictMcpConfig:  strictMcpConfig);

		var justStarted = _streamingManager.EnsureStarted(nodeId, spawnParams, HandleStreamEvent);
		if (justStarted)
			Dispatcher.UIThread.Post(NotifyStreamingStateChanged);

		await _streamingManager.SendTurnAsync(nodeId, messageToSend, ct);

		if (_needsAuthRaceRetry)
		{
			// ISSUE.010 — same transient cross-process OAuth refresh race as the one-shot path;
			// the streaming process is still alive, so retrying just writes the turn again.
			_needsAuthRaceRetry = false;
			_log.Information("Retrying streaming turn after transient OAuth refresh race for session {FileName}", _node.FileName);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			await _streamingManager.SendTurnAsync(nodeId, messageToSend, ct);
		}
	}

	private void HandleStreamEvent(ClaudeStreamEvent evt)
	{
		switch (evt.Type)
		{
			case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
				var fileModelId = Interlocked.Exchange(ref _pendingFileModelId, null);
				var fileEffort  = Interlocked.Exchange(ref _pendingFileEffort, null);
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleAssistant, evt.Content,
					modelId: fileModelId, effort: fileEffort);
				break;
			case "system" when evt.Subtype is "compact":
				_fileService.AppendCompactionSeparator(_node.FileName);
				break;
			case "system" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				// When context retry or an auth-race retry is pending, suppress stderr-based errors
				// (they echo the same error the result event already handled).
				if (_needsContextRetry || _needsAuthRaceRetry)
					return;
				if (evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase))
				{
					_log.Information("No conversation found (system error) for session {FileName} — will retry with context", _node.FileName);
					_node.Model.ClaudeSessionId = null;
					_appSettings.Save();
					_needsContextRetry = true;
					_sendErrorText = null;
					return;
				}
				if (evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase))
				{
					_log.Information("OAuth refresh race (system error) for session {FileName} — will retry once", _node.FileName);
					_needsAuthRaceRetry = true;
					_sendErrorText = null;
					return;
				}
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				if (evt.Content.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
					Dispatcher.UIThread.Post(() => _profiles.HasAuthError = true);
				break;
			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase):
				// "No conversation found" — transient infrastructure error.
				// Set flag for auto-retry with context preamble; skip file write and UI post.
				_log.Information("No conversation found for session {FileName} — will retry with context", _node.FileName);
				_node.Model.ClaudeSessionId = null;
				_appSettings.Save();
				_needsContextRetry = true;
				_sendErrorText = null;
				return;
			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase):
				// ISSUE.010 — transient cross-process OAuth refresh-token race, not a real auth
				// failure (see Constants.Auth.OAuthRefreshRaceMarker). Set flag for a single silent
				// auto-retry; skip file write and UI post so it never reads as a real failure.
				_log.Information("OAuth refresh race for session {FileName} — will retry once", _node.FileName);
				_needsAuthRaceRetry = true;
				_sendErrorText = null;
				return;
			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				_log.Warning("Claude result error: {Error}", evt.Content);
				_sendErrorText = evt.Content;
				_fileService.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				if (evt.Content.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
					Dispatcher.UIThread.Post(() => _profiles.HasAuthError = true);
				break;
			case "result" when !evt.IsError && evt.SessionId is not null:
				Dispatcher.UIThread.Post(() => _profiles.HasAuthError = false);
				_node.Model.ClaudeSessionId = evt.SessionId;
				_appSettings.Save();
				Dispatcher.UIThread.Post(() =>
				{
					this.RaisePropertyChanged(nameof(CanClear));
					_content.NotifyClaudeSessionChanged();
				});
				break;
		}

		Dispatcher.UIThread.Post(() =>
		{
			switch (evt.Type)
			{
				case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
					var pendingLabel = _pendingModelLabel;
					_pendingModelLabel = null;
					_content.Messages.Add(new MessageEntryViewModel
					{
						Role       = Constants.SessionFile.RoleAssistant,
						Content    = evt.Content,
						Timestamp  = evt.Timestamp,
						ModelLabel = pendingLabel,
					});
					_node.LastResponseTimestamp = evt.Timestamp;
					break;

				case "system" when evt.Subtype is "compact":
					_content.Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleCompaction,
						Content   = string.Empty,
						Timestamp = evt.Timestamp,
					});
					break;

				case "system" when evt.Subtype is "task_progress" or "task_started"
				                   && !string.IsNullOrWhiteSpace(evt.Content):
					var last = _content.Messages.Count > 0 ? _content.Messages[^1] : null;
					if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
						last.Content = evt.Content;
					else
						_content.Messages.Add(new MessageEntryViewModel
						{
							Role       = Constants.SessionFile.RoleSystem,
							Content    = evt.Content,
							Timestamp  = evt.Timestamp,
							IsProgress = true,
						});
					break;

				case "system" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
					_content.Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = evt.Content,
						Timestamp = evt.Timestamp,
					});
					break;

				case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
					for (var i = _content.Messages.Count - 1; i >= 0; i--)
					{
						if (_content.Messages[i].IsProgress)
							_content.Messages.RemoveAt(i);
					}
					_content.Messages.Add(new MessageEntryViewModel
					{
						Role      = Constants.SessionFile.RoleSystem,
						Content   = evt.Content,
						Timestamp = evt.Timestamp,
					});
					break;

				case "result":
					for (var i = _content.Messages.Count - 1; i >= 0; i--)
					{
						if (_content.Messages[i].IsProgress)
							_content.Messages.RemoveAt(i);
					}
					break;
			}
		});
	}

	private void OnThinkingTimerTick(object? sender, EventArgs e)
	{
		var elapsed = DateTimeOffset.UtcNow - _thinkingStartedAt;
		ThinkingDuration = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
	}

	/// <summary>
	/// Immediately detaches the live Claude JSONL session (FR.11.7, FR.11.8).
	/// Moves ClaudeSessionId to PriorClaudeSessionIds so the JSONL is still viewable,
	/// but the next prompt will feed the text session as context instead of --resume.
	/// Thread-safe; posts UI updates to the dispatcher.
	/// </summary>
	public void DetachSession(string displayMessage = "[Session detached — next prompt will use text session as context]")
	{
		var currentId = _node.Model.ClaudeSessionId;
		if (string.IsNullOrEmpty(currentId))
			return;

		_node.Model.PriorClaudeSessionIds.Add(currentId);
		_node.Model.ClaudeSessionId = null;
		_appSettings.Save();
		_log.Information("Detached Claude session {SessionId} for {FileName}", currentId, _node.FileName);

		Dispatcher.UIThread.Post(() =>
		{
			_content.AddSystemMessage(displayMessage);
			this.RaisePropertyChanged(nameof(CanClear));
			_content.NotifyClaudeSessionChanged();
		});
	}

	/// <summary>Sends a follow-up compaction prompt and rewrites the session file (FR.11.6).</summary>
	private async Task SendCompactionPromptAsync()
	{
		_log.Information("Starting auto-compaction for session {FileName}", _node.FileName);

		// The compaction prompt embeds the full session transcript directly (FR.11.6), sourced
		// from the always-complete text Session File rather than relying on --resume — so this
		// call is intentionally stateless (sessionId: null) and needs no cross-profile lookup.
		var compactionPrompt = _composer.BuildCompactionPrompt();
		if (compactionPrompt == null)
		{
			_log.Information("Nothing to compact for session {FileName}", _node.FileName);
			Dispatcher.UIThread.Post(() => _content.AddSystemMessage("[Nothing to compact]"));
			return;
		}

		Dispatcher.UIThread.Post(() => _content.Messages.Add(new MessageEntryViewModel
		{
			Role       = Constants.SessionFile.RoleSystem,
			Content    = "Compacting session...",
			Timestamp  = DateTimeOffset.UtcNow,
			IsProgress = true,
		}));

		var compactedContent = new StringBuilder();
		var hadError = false;
		string? compactionErrorContent = null;

		// Local Tool Profile (FR.12.21): restrict native tools the same way an ordinary turn would —
		// a weak local model is just as prone to mis-calling tools on a compaction turn as on any
		// other. No --mcp-config is built for compaction either way (unchanged from before this
		// feature), so only the --tools/--strict-mcp-config pair applies here.
		var compactionLocalProfile = LocalToolProfileResolver.Resolve(_models.SelectedModelInfo, _models.DirectoryModel);

		await _processManager.SendMessageAsync(
			workingDirectory: _node.Model.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        null,
			userMessage:      compactionPrompt,
			model:            _models.SelectedModelId,
			profileConfigDir: _profiles.SelectedProfileConfigDir,
			effort:           _models.SelectedEffort,
			ollamaBaseUrl:    _models.SelectedLocalBaseUrl,
			disableTools:     _models.SelectedModelDisablesTools,
			disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
			localToolSpec:    compactionLocalProfile?.ToolSpec,
			strictMcpConfig:  compactionLocalProfile != null,
			onEvent:          evt =>
			{
				// Any error event (auth failure, exit-code failure, etc.) marks the whole
				// compaction turn as failed (FR.11.6) — even if some assistant text was
				// also captured, it must not be treated as a valid compacted result.
				if (evt.IsError)
				{
					hadError = true;
					compactionErrorContent ??= evt.Content;
					return;
				}
				if (evt.Type == "assistant" && !string.IsNullOrWhiteSpace(evt.Content))
					compactedContent.AppendLine(evt.Content);
				// Session ID from the compaction response is intentionally NOT stored:
				// after compaction the JSONL is detached (FR.11.8).
			});

		var compacted = compactedContent.ToString().Trim();
		if (!hadError && !string.IsNullOrEmpty(compacted))
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
				_content.Messages.Clear();
				var entries = _fileService.ReadEntries(_node.FileName);
				foreach (var entry in entries)
				{
					if (entry.Role != Constants.SessionFile.RoleCompaction
					    && string.IsNullOrWhiteSpace(entry.Content))
						continue;
					_content.Messages.Add(SessionContentViewModel.EntryToViewModel(entry));
				}
			});

			// Detach the JSONL session after compaction (FR.11.8):
			// next prompt will use the compacted text session as context via BuildContextPreamble.
			DetachSession("[Session compacted and JSONL detached — next prompt will use the compacted text session as context]");

			_log.Information("Session {FileName} compacted and JSONL detached", _node.FileName);
		}
		else
		{
			var statusMessage = hadError
				? $"[Compaction failed — session left unchanged: {compactionErrorContent}]"
				: "[Compaction returned no content — session left unchanged]";

			if (hadError)
			{
				_log.Warning("Compaction failed for session {FileName}; keeping original. Error: {Error}", _node.FileName, compactionErrorContent);
				if (!string.IsNullOrEmpty(compactionErrorContent)
					&& compactionErrorContent.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
					Dispatcher.UIThread.Post(() => _profiles.HasAuthError = true);
			}
			else
			{
				_log.Warning("Compaction returned empty content for session {FileName}; keeping original", _node.FileName);
			}

			Dispatcher.UIThread.Post(() =>
			{
				for (var i = _content.Messages.Count - 1; i >= 0; i--)
				{
					if (_content.Messages[i].IsProgress) _content.Messages.RemoveAt(i);
				}
				_content.AddSystemMessage(statusMessage);
			});
		}
	}

	/// <summary>Sends a mid-run correction prompt when user toggles a flag while Claude is thinking.</summary>
	private void SendMidRunToggleCorrection(string toggleName, bool newValue, string onPrompt, string offPrompt)
	{
		var label = newValue ? "enabled" : "disabled";
		var prompt = newValue ? onPrompt : offPrompt;
		SendMidRunToggleCorrection(toggleName, label, prompt);
	}

	/// <summary>Sends a mid-run correction prompt for the tri-state Auto-Commit toggle (FR.11.12).</summary>
	private void SendMidRunAutoCommitCorrection(bool? newValue)
	{
		var (label, prompt) = newValue switch
		{
			true  => ("enabled", Constants.Instructions.MidRunAutoCommitOn),
			false => ("disabled", Constants.Instructions.MidRunAutoCommitOff),
			null  => ("set to no preference", Constants.Instructions.MidRunAutoCommitSilent),
		};
		SendMidRunToggleCorrection("AutoCommit", label, prompt);
	}

	private void SendMidRunToggleCorrection(string toggleName, string label, string prompt)
	{
		_content.AddSystemMessage($"[{toggleName} was {label} for this run]");

		_log.Information("Mid-run {Toggle} toggle: {State}, sending correction prompt", toggleName, label);

		// Note: like headless turns (FR.19.11), a mid-run correction always spawns a one-shot
		// --resume process against ClaudeSessionId regardless of whether the session is in
		// Streaming Session Mode — if a streaming process is also holding that same session ID
		// open, this is the same class of known v1 limitation, not solved here.
		// Local Tool Profile (FR.12.21) applies here too — see SendCompactionPromptAsync's comment.
		var correctionLocalProfile = LocalToolProfileResolver.Resolve(_models.SelectedModelInfo, _models.DirectoryModel);

		_ = _processManager.SendMessageAsync(
			workingDirectory: _node.Model.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        _node.Model.ClaudeSessionId,
			userMessage:      prompt,
			model:            _models.SelectedModelId,
			profileConfigDir: _profiles.SelectedProfileConfigDir,
			effort:           _models.SelectedEffort,
			ollamaBaseUrl:    _models.SelectedLocalBaseUrl,
			disableTools:     _models.SelectedModelDisablesTools,
			disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
			localToolSpec:    correctionLocalProfile?.ToolSpec,
			strictMcpConfig:  correctionLocalProfile != null,
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
}
