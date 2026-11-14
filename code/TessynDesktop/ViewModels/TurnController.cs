using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Owns turn sending (interactive send + streaming), the thinking timer, mid-run corrections,
/// compaction, and JSONL detach for a single session (FR.11.2, FR.11.6-11.10, FR.11.12, FR.14.3).
/// A thin adapter (P1.I): the turn itself runs in the Core <see cref="ITurnEngine"/> (launch, stream parsing, transcript writes,
/// running flag, retries, cancellation, TurnCompleted), and this class renders the engine's progress through its
/// <see cref="ITurnObserver"/> (see TurnController.Observer.cs) and keeps the UI-only state: busy flag, thinking timer, completion
/// sound, input clearing, toggles. The headless path (<c>IDaemonClient.RunTurnAsync</c>) coordinates with it only via the shared
/// turn lock. Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TurnController : ReactiveObject, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<TurnController>();

	private readonly SessionNodeViewModel _node;
	private readonly ISessionFileService _fileService;
	private readonly IDraftService _draftService;
	private readonly ITurnEngine _engine;
	private readonly IClaudeStreamingSessionManager _streamingManager;
	private readonly IAppSettingsService _appSettings;
	private readonly IDaemonClient _daemonClient;
	private readonly ISessionSoundService _soundService;
	private readonly ISessionCompactionService _compaction;
	private readonly SessionContentViewModel _content;
	private readonly ModelSelectionController _models;
	private readonly ProfileController _profiles;

	private bool _isBusy;
	private bool _isNodeRunning;
	private string _thinkingDuration = string.Empty;
	private DispatcherTimer? _thinkingTimer;

	/// <summary>Subscription on the (long-lived) tree node; must be disposed or the node keeps this controller alive.</summary>
	private IDisposable? _nodeRunningSubscription;
	private DateTimeOffset _thinkingStartedAt;
	private int _busyCount;

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

	/// <summary>Re-reads the streaming flag and the process state after a daemon method (the web client) changed them.</summary>
	public void RefreshStreamingState() => NotifyStreamingStateChanged();

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
		ITurnEngine engine,
		IClaudeStreamingSessionManager streamingManager,
		IAppSettingsService appSettings,
		IDaemonClient daemonClient,
		ISessionSoundService soundService,
		ISessionCompactionService compaction,
		SessionContentViewModel content,
		ModelSelectionController models,
		ProfileController profiles)
	{
		_node             = node;
		_fileService      = fileService;
		_draftService     = draftService;
		_engine           = engine;
		_streamingManager = streamingManager;
		_appSettings      = appSettings;
		_daemonClient     = daemonClient;
		_soundService     = soundService;
		_compaction       = compaction;
		_content          = content;
		_models           = models;
		_profiles         = profiles;

		_nodeRunningSubscription = node.WhenAnyValue(x => x.IsRunning).Subscribe(running => IsNodeRunning = running);
		_streamingManager.ProcessExited += OnStreamingProcessExited;
	}

	private void OnStreamingProcessExited(string nodeId)
	{
		if (nodeId != _node.Model.NodeId) return;
		Dispatcher.UIThread.Post(NotifyStreamingStateChanged);
	}

	public void Dispose()
	{
		_nodeRunningSubscription?.Dispose();
		_nodeRunningSubscription = null;
		_streamingManager.ProcessExited -= OnStreamingProcessExited;
		if (_thinkingTimer != null)
		{
			_thinkingTimer.Tick -= OnThinkingTimerTick;
			_thinkingTimer.Stop();
			_thinkingTimer = null;
		}
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
			_engine.CancelRuns(_node.Model.NodeId);
			_log.Information("Stop requested for session {FileName}", _node.FileName);
		}
		else if (IsNodeRunning)
		{
			// A headless (orchestrated) turn, or a turn the daemon's engine runs for a remote client.
			_daemonClient.CancelTurn(_node.Model.NodeId);
			_engine.CancelRuns(_node.Model.NodeId);
			_log.Information("Stop requested for orchestrated turn on node {NodeId}", _node.Model.NodeId);
		}
	}

	/// <param name="preserveInput">
	/// When true the input box is not cleared and its draft is not deleted — for callers that send a
	/// prompt which did not come from the input box (the Focus view's "Send answers", FR.23.15).
	/// </param>
	public async Task SendAsync(string inputText, bool preserveInput = false)
	{
		var message = inputText.Trim();
		if (string.IsNullOrEmpty(message) && !_isAutoCompact)
			return;

		await SendAsyncCore(message, preserveInput);
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

	private async Task SendAsyncCore(string message, bool preserveInput)
	{
		if (!preserveInput)
		{
			InputCleared?.Invoke();
			_draftService.DeleteDraft(_node.FileName);
		}

		// Auto-Compact can be triggered with an empty prompt: skip straight to compaction (no turn sent).
		if (string.IsNullOrEmpty(message) && _isAutoCompact)
		{
			// No turn is running here, so the lock is taken by the service.
			await SendCompactionPromptAsync(turnLockHeld: false);
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

		try
		{
			// The engine writes the user entry (its observer callback adds the rows, synchronously, as the controller did), runs the
			// turn, and returns once the turn was reported complete.
			await _engine.RunAsync(new TurnRequest(_node.Model, message, BuildTurnSettings(), wasNewBranch), new RunObserver(this, message));
		}
		finally
		{
			_busyCount = Math.Max(0, _busyCount - 1);
			if (_busyCount == 0)
			{
				// Update JSONL entry count before IsBusy goes false, so the watcher
				// doesn't re-detect entries we already processed during this send cycle.
				// The parse runs off the UI thread; IsBusy stays true (watcher suppressed) while we await it.
				await _content.RefreshJsonlEntryCountAsync();
			}

			// A send that started during the await owns the idle transition now (its own finally runs it).
			if (_busyCount == 0)
			{
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

	/// <summary>The command bar's current choices as the launch settings of a turn (or a correction).</summary>
	private TurnSettings BuildTurnSettings() => new(
		_models.SelectedModelId,
		_models.SelectedEffort,
		_profiles.SelectedProfileConfigDir,
		_profiles.SelectedProfileDisplayName,
		_models.SelectedLocalBaseUrl,
		_models.SelectedModelDisablesTools,
		_models.IsChromeEnabled,
		_models.SelectedModelInfo,
		_models.DirectoryModel);

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

	/// <summary>
	/// Compacts the session through the shared Core service (FR.11.6) and does the UI-only parts: the progress row, the
	/// reload of Messages, the in-memory outcome lines and the auth-error flag. <paramref name="turnLockHeld"/> is true when
	/// the caller (the send flow) still holds the node's turn lock, so the service must not take it again.
	/// </summary>
	private async Task SendCompactionPromptAsync(bool turnLockHeld)
	{
		var progressRow = new MessageEntryViewModel
		{
			Role       = Constants.SessionFile.RoleSystem,
			Content    = "Compacting session...",
			Timestamp  = DateTimeOffset.UtcNow,
			IsProgress = true,
		};
		Dispatcher.UIThread.Post(() => _content.Messages.Add(progressRow));

		var options = new SessionCompactionOptionsModel(
			_models.SelectedModelId, _models.SelectedEffort, _profiles.SelectedProfileConfigDir, _models.SelectedModelInfo);

		var result = turnLockHeld
			? await _compaction.CompactWhileHoldingTurnLockAsync(_node.Model, options)
			: await _compaction.CompactAsync(_node.Model, options);

		switch (result.Outcome)
		{
			case SessionCompactionOutcome.NothingToCompact:
				// Only this run's own row: nothing was sent, so other progress rows (a queued send) stay.
				Dispatcher.UIThread.Post(() =>
				{
					_content.Messages.Remove(progressRow);
					_content.AddSystemMessage(result.SystemMessage);
				});
				break;

			case SessionCompactionOutcome.Compacted:
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

					// The service detached the JSONL (FR.11.8); only when it had a live session is there anything to announce.
					if (result.SessionDetached)
					{
						_content.AddSystemMessage(result.SystemMessage);
						this.RaisePropertyChanged(nameof(CanClear));
						_content.NotifyClaudeSessionChanged();
					}
				});
				break;

			default:
				if (!string.IsNullOrEmpty(result.Error)
				    && result.Error.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
					Dispatcher.UIThread.Post(() => _profiles.HasAuthError = true);

				Dispatcher.UIThread.Post(() =>
				{
					RemoveProgressRows();
					_content.AddSystemMessage(result.SystemMessage);
				});
				break;
		}
	}

	private void RemoveProgressRows()
	{
		for (var i = _content.Messages.Count - 1; i >= 0; i--)
		{
			if (_content.Messages[i].IsProgress) _content.Messages.RemoveAt(i);
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

		// The engine spawns the one-shot --resume process (also in Streaming Session Mode, FR.19.11) and keeps only its session id.
		_engine.SendCorrection(_node.Model, BuildTurnSettings(), prompt);
	}
}
