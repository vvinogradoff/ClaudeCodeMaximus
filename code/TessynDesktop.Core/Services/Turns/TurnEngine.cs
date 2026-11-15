using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITurnEngine" />
/// <remarks>
/// The flow of <see cref="RunAsync"/> is the former <c>TurnController.SendAsyncCore</c> minus its UI: same order of transcript
/// writes, same lock, retry and cancellation behaviour. The per-controller retry and metadata fields became per-turn state in
/// <see cref="TurnRun"/>. Created by Claude.
/// </remarks>
public sealed class TurnEngine : ITurnEngine
{
	private static readonly ILogger _log = Log.ForContext<TurnEngine>();

	private readonly ISessionFileService _files;
	private readonly ISessionRegistry _registry;
	private readonly ISessionTurnService _turns;
	private readonly IClaudeProcessManager _processManager;
	private readonly IClaudeStreamingSessionManager _streamingManager;
	private readonly IAppSettingsService _appSettings;
	private readonly Lazy<IAgentMcpServer> _mcpServer;
	private readonly Lazy<ITelegramBridgeService> _telegramBridge;
	private readonly IClaudeSessionStatusService _sessionStatus;
	private readonly IAttentionService _attention;
	private readonly ISessionTagService _tags;
	private readonly IReadOnlyList<ITurnProgressSink> _progressSinks;
	private readonly IProfileSwitchService? _profileSwitch;
	private readonly ISessionCompactionService? _compaction;
	private readonly ISessionAuthErrorTracker? _authErrors;

	private readonly ConcurrentDictionary<string, TurnNodeContext> _nodes = new();

	public TurnEngine(
		ISessionFileService files,
		ISessionRegistry registry,
		ISessionTurnService turns,
		IClaudeProcessManager processManager,
		IClaudeStreamingSessionManager streamingManager,
		IAppSettingsService appSettings,
		Lazy<IAgentMcpServer> mcpServer,
		Lazy<ITelegramBridgeService> telegramBridge,
		IClaudeSessionStatusService sessionStatus,
		IAttentionService attention,
		ISessionTagService tags,
		IEnumerable<ITurnProgressSink>? progressSinks = null,
		IProfileSwitchService? profileSwitch = null,
		ISessionCompactionService? compaction = null,
		ISessionAuthErrorTracker? authErrors = null)
	{
		_files            = files;
		_registry         = registry;
		_turns            = turns;
		_processManager   = processManager;
		_streamingManager = streamingManager;
		_appSettings      = appSettings;
		_mcpServer        = mcpServer;
		_telegramBridge   = telegramBridge;
		_sessionStatus    = sessionStatus;
		_attention        = attention;
		_tags             = tags;
		_progressSinks    = progressSinks?.ToArray() ?? [];
		_profileSwitch    = profileSwitch;
		_compaction       = compaction;
		_authErrors       = authErrors;
	}

	/// <inheritdoc />
	public bool CancelRuns(string nodeId) =>
		_nodes.TryGetValue(nodeId, out var context) && context.CancelAll();

	/// <inheritdoc />
	public void ReleaseObserver(string nodeId, ITurnObserver observer)
	{
		if (_nodes.TryGetValue(nodeId, out var context))
			context.ReleaseObserver(observer);
	}

	/// <inheritdoc />
	public bool IsBusy(string nodeId) =>
		_nodes.TryGetValue(nodeId, out var context) && context.IsBusy;

	/// <inheritdoc />
	public async Task<TurnOutcome> RunAsync(TurnRequest request, ITurnObserver? observer = null, CancellationToken cancellationToken = default)
	{
		var node     = request.Node;
		var settings = request.Settings;
		var nodeId   = node.NodeId;

		// Auto-Compact with an empty prompt (FR.11.6): no turn is sent, the session is only compacted.
		if (request.AutoCompact && string.IsNullOrEmpty(request.Message))
			return await RunCompactionOnlyAsync(request, observer ?? new NullTurnObserver(), cancellationToken);

		var context  = _nodes.GetOrAdd(nodeId, _ => new TurnNodeContext());
		var turnId   = request.TurnId ?? Guid.NewGuid().ToString(Constants.Turns.TurnIdFormat);
		observer   ??= new NullTurnObserver();

		var progress = new TurnProgressTracker(nodeId, turnId, _progressSinks);
		var run = new TurnRun(turnId, node, _files, _appSettings, observer, progress,
			settings.ModelId ?? Constants.Turns.DefaultOptionText, settings.Effort ?? Constants.Turns.DefaultOptionText, AuthErrorSink(nodeId));

		context.Enter();
		if (observer is not NullTurnObserver)
			context.LastObserver = observer;

		var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		context.Add(cts);
		var ct = cts.Token;

		SemaphoreSlim? turnLock = null;
		var lockAcquired  = false;
		var sendCancelled = false;
		string? sendErrorText = null;

		try
		{
			progress.Started(request.ClientRequestId);

			// FR.21.2 - lazily ensure a Telegram topic exists for this session on its first interactive activity since the
			// bridge was enabled. Fire-and-forget: never throws, never delays the turn.
			_ = _telegramBridge.Value.EnsureTopicForSessionAsync(node);

			// Store only the clean user message in the file (FR.11.2).
			_files.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, request.Message, profileName: settings.ProfileDisplayName);
			observer.UserEntryAppended(DateTimeOffset.UtcNow, settings.ProfileDisplayName);

			var sessionId = node.ClaudeSessionId;

			// The cross-profile adoption (FR.11.13) moves the transcript and session folder, so it runs below, under the turn lock:
			// before it a queued message could move the files from under the turn that is still running.

			var augmentedMessage = request.Message + BuildInstructionBlock(node, request.IsNewBranch, sessionId == null);

			// Proactive context reload (FR.11.10): if the file has history but no session id, wrap with context.
			var messageToSend = augmentedMessage;
			if (sessionId == null)
			{
				var entries = _files.ReadEntries(node.FileName);
				var priorEntries = entries
					.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
					.ToList();
				if (priorEntries.Count > 1) // more than just the current prompt
				{
					messageToSend = InstructionBlockBuilder.BuildContextPreamble(entries, augmentedMessage);
					_log.Information("Proactive context reload for session {FileName}", node.FileName);
				}
			}

			var launch = await PrepareLaunchAsync(node, settings);

			// Serialize only the CLI dispatch: the message is already in the transcript, and this send's spawn waits behind an
			// in-flight one to avoid concurrent --resume.
			turnLock = string.IsNullOrEmpty(nodeId) ? null : _turns.GetTurnLock(nodeId);

			// Opened right before the try that closes it, so nothing that throws in between can leak the share.
			OpenRunning(context, nodeId);
			try
			{
				if (turnLock != null)
				{
					if (turnLock.CurrentCount == 0)
						progress.Status(TurnState.Queued);

					await turnLock.WaitAsync(ct);

					// SemaphoreSlim.WaitAsync(token) can still hand the lock to a waiter whose token was cancelled meanwhile. A send that was
					// stopped while queued must not run: give the lock back and end as interrupted, as if the wait had been cancelled.
					if (ct.IsCancellationRequested)
					{
						turnLock.Release();
						ct.ThrowIfCancellationRequested();
					}

					lockAcquired = true;
				}

				// ISSUE.013: the turn runs on the profile of its settings. A session that was on another profile is switched (no
				// cascade; transcript and session folder move, a live streaming process stops) and the ProfileId pin is recorded - the
				// one switch path of the Avalonia profile change. Under the turn lock, so no other turn of the node is using its files.
				_profileSwitch?.ApplyPending(node);
				_profileSwitch?.RecordTurnProfile(node, settings.ProfileDisplayName);

				// Cross-profile session adoption (FR.11.13): if this session was created under a different profile than the one now
				// active, move its JSONL into the active profile's store before attempting --resume. Under the turn lock (ISSUE.013 N1).
				if (sessionId != null)
					_sessionStatus.TryAdoptSessionFromOtherProfile(node.WorkingDirectory, sessionId, settings.ProfileConfigDir);

				run.SendErrorText = null;
				context.Active = run;
				progress.Status(TurnState.Running);

				if (node.IsStreamingMode)
				{
					// Streaming Session Mode (FR.19): no --resume "No conversation found" retry path - the same open process
					// carries the conversation, so context-retry doesn't apply.
					await SendViaStreamingAsync(run, observer, context, node, settings, sessionId, messageToSend, launch, ct);
				}
				else
				{
					await SendOneShotAsync(run, observer, node, settings, sessionId, messageToSend, augmentedMessage, launch, ct);
				}

				// Post-response step of the client (Auto-Compact, FR.11.6), still holding the turn lock.
				await observer.AfterResponseWhileLockedAsync();

				// Auto-Compact (FR.11.6): after the answer, still holding the turn lock; the observer may have a live toggle state.
				if (observer.ShouldAutoCompact(request.AutoCompact))
				{
					// A Stop that arrived between the answer and here must not be followed by a compaction (V1).
					ct.ThrowIfCancellationRequested();
					await CompactAsync(node, settings, observer, turnLockHeld: true, ct);
				}
			}
			catch (OperationCanceledException)
			{
				sendCancelled = true;
				_log.Information("Send interrupted by user for session {FileName}", node.FileName);
				_files.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, Constants.Turns.InterruptedByUserText);
				observer.Interrupted(DateTimeOffset.UtcNow);
			}
			finally
			{
				// From here on the process's output belongs to no turn.
				context.Active = null;

				// FR.21.22 - once per send that actually ran, after its last entry is appended and before the lock is released
				// (the next queued send starts as soon as it acquires the lock). A send stopped while still queued behind
				// another turn never ran one, so it reports nothing - reporting it cancelled would drop the running turn's
				// pending answer.
				if (lockAcquired || turnLock == null)
				{
					sendErrorText = sendCancelled ? null : run.SendErrorText;

					// The sinks flush first, so a turn's last notifications precede its turn.completed.
					progress.Finished(sendCancelled, sendErrorText, ran: true);
					try
					{
						_files.NotifyTurnCompleted(node.FileName, sendCancelled, sendErrorText, turnId);
					}
					catch (Exception ex)
					{
						_log.Warning(ex, "TurnCompleted handler failed for session {FileName}", node.FileName);
					}
				}
				else
					progress.Finished(sendCancelled, null, ran: false);

				if (lockAcquired) turnLock?.Release();
			}

			return new TurnOutcome(turnId, sendCancelled, sendErrorText);
		}
		catch (Exception ex)
		{
			// A failure before the dispatch (or one rethrown from it): the turn is over. A no-op when the dispatch already reported it.
			progress.Finished(false, ex.Message, ran: false);
			throw;
		}
		finally
		{
			context.Remove(cts);
			cts.Dispose();
			context.Leave();
			await EndBusyAsync(context, nodeId);
		}
	}

	/// <summary>Where a turn of the node reports its auth outcome so that the session state carries it (null when the engine has no tracker).</summary>
	private Action<bool>? AuthErrorSink(string nodeId) =>
		_authErrors == null ? null : hasAuthError => _authErrors.Set(nodeId, hasAuthError);

	/// <summary>
	/// An Auto-Compact without a prompt: nothing is sent, the compaction takes the node's turn lock itself. It is still reported as a turn of its
	/// own (V2): <c>turn.started</c> first, then <c>turn.completed</c> with the failure of the compaction as its error. A failure is also appended to the
	/// transcript as a system line, like the other turn errors. A Stop reaches it through <see cref="CancelRuns"/> (V1).
	/// </summary>
	private async Task<TurnOutcome> RunCompactionOnlyAsync(TurnRequest request, ITurnObserver observer, CancellationToken cancellationToken)
	{
		var node   = request.Node;
		var turnId = request.TurnId ?? Guid.NewGuid().ToString(Constants.Turns.TurnIdFormat);
		var context = _nodes.GetOrAdd(node.NodeId, _ => new TurnNodeContext());

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		context.Add(cts);

		var progress = new TurnProgressTracker(node.NodeId, turnId, _progressSinks);
		progress.Started(request.ClientRequestId);

		// The engine takes the node's turn lock itself (the service then runs with it held), so it knows whether the compaction ever ran: a Stop
		// that arrives while the run still waits behind another turn is reported as "never ran", like a queued ordinary send (R3).
		var turnLock = _turns.GetTurnLock(node.NodeId);
		var lockAcquired = false;
		var cancelled = false;
		string? error = null;
		try
		{
			await turnLock.WaitAsync(cts.Token);
			lockAcquired = true;
			var result = await CompactAsync(node, request.Settings, observer, turnLockHeld: true, cts.Token);
			if (result is { Outcome: SessionCompactionOutcome.Failed })
			{
				error = result.Error ?? result.SystemMessage;
				_files.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, result.SystemMessage);
			}
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
			_files.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, Constants.Turns.InterruptedByUserText);
			observer.Interrupted(DateTimeOffset.UtcNow);
		}
		catch (Exception ex)
		{
			error = ex.Message;
			throw;
		}
		finally
		{
			context.Remove(cts);
			if (lockAcquired)
			{
				progress.Finished(cancelled, error, ran: true);
				try
				{
					_files.NotifyTurnCompleted(node.FileName, cancelled, error, turnId);
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "TurnCompleted handler failed for session {FileName}", node.FileName);
				}

				turnLock.Release();
			}
			else
				progress.Finished(cancelled, error, ran: false);
		}

		return new TurnOutcome(turnId, cancelled, error);
	}

	/// <summary>
	/// Compacts the session through the shared <see cref="ISessionCompactionService"/> (FR.11.6) with the model, effort and profile of the turn
	/// and reports to the observer. A failure for invalid credentials raises the auth error. Returns null when the engine has no compaction service.
	/// </summary>
	private async Task<SessionCompactionResultModel?> CompactAsync(SessionNodeModel node, TurnSettings settings, ITurnObserver observer, bool turnLockHeld, CancellationToken ct)
	{
		if (_compaction == null)
		{
			_log.Warning("Auto-Compact requested for session {FileName} but the engine has no compaction service", node.FileName);
			return null;
		}

		observer.CompactionStarted();

		var options = new SessionCompactionOptionsModel(settings.ModelId, settings.Effort, settings.ProfileConfigDir, settings.ModelInfo);
		SessionCompactionResultModel result;
		try
		{
			result = turnLockHeld
				? await _compaction.CompactWhileHoldingTurnLockAsync(node, options, ct)
				: await _compaction.CompactAsync(node, options, ct);
		}
		catch (OperationCanceledException)
		{
			// Stopped: the transcript is untouched (the service rewrites it only after the CLI call returned). The client drops its progress row.
			observer.CompactionFinished(new SessionCompactionResultModel(
				SessionCompactionOutcome.Failed, null, Constants.Compaction.StoppedMessage, SessionDetached: false));
			throw;
		}

		if (result.Outcome == SessionCompactionOutcome.Failed
		    && !string.IsNullOrEmpty(result.Error)
		    && result.Error.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
		{
			observer.AuthErrorChanged(true);
			_authErrors?.Set(node.NodeId, true);
		}

		observer.CompactionFinished(result);
		return result;
	}

	/// <summary>
	/// After the node's last send: the client finishes its bookkeeping, then the node becomes idle (client state first, then the daemon's
	/// running flag - the order the controller always used). A send that started meanwhile owns the idle transition instead.
	/// </summary>
	private async Task EndBusyAsync(TurnNodeContext context, string nodeId)
	{
		if (context.IsBusy)
			return;

		var idleObserver = context.LastObserver;
		if (idleObserver != null)
		{
			try
			{
				await idleObserver.BeforeIdleAsync();
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "A turn observer failed before the idle transition of node {NodeId}", nodeId);
			}
		}

		if (!context.TryBecomeIdle(out var shareWasOpen))
			return;

		try
		{
			idleObserver?.Idle();
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "A turn observer failed in the idle transition of node {NodeId}", nodeId);
		}

		if (shareWasOpen && !string.IsNullOrEmpty(nodeId))
			_registry.SetSessionRunning(nodeId, false);
	}

	/// <summary>
	/// The one callback bound to a node's streaming process (bound again on every turn, like the controller's method was). It hands each event
	/// to the turn holding the lock; events outside any turn - background-task output, native schedules (FR.19.4), anything arriving while the
	/// next turn still waits for the lock - go to a fresh background run: persisted and rendered by the last observer exactly as the controller
	/// always did, but without any state of a finished turn (retry flags, error text, model stamp) and without progress.
	/// </summary>
	private Action<ClaudeStreamEvent> StreamHandlerFor(TurnNodeContext context, SessionNodeModel node) =>
		context.StreamHandler ??= evt =>
		{
			var run = context.Active
				?? new TurnRun(string.Empty, node, _files, _appSettings, context.LastObserver ?? new NullTurnObserver(),
					new TurnProgressTracker(node.NodeId, string.Empty, []), null, null, AuthErrorSink(node.NodeId));
			run.HandleStreamEvent(evt);
		};

	/// <inheritdoc />
	public void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt)
	{
		// Like headless turns (FR.19.11), a mid-run correction always spawns a one-shot --resume process against the Claude session
		// id regardless of Streaming Session Mode - if a streaming process holds that same session id open, this is the same class of
		// known v1 limitation, not solved here. The Local Tool Profile (FR.12.21) applies here too.
		var correctionLocalProfile = LocalToolProfileResolver.Resolve(settings.ModelInfo, settings.DirectoryModel);

		_ = _processManager.SendMessageAsync(
			workingDirectory: node.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        node.ClaudeSessionId,
			userMessage:      prompt,
			model:            settings.ModelId,
			profileConfigDir: settings.ProfileConfigDir,
			effort:           settings.Effort,
			ollamaBaseUrl:    settings.OllamaBaseUrl,
			disableTools:     settings.DisableTools,
			disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
			localToolSpec:    correctionLocalProfile?.ToolSpec,
			strictMcpConfig:  correctionLocalProfile != null,
			onEvent:          evt =>
			{
				// Capture session ID updates but don't write to file or UI
				if (evt.Type == Constants.Turns.EventTypes.Result && !evt.IsError && evt.SessionId is not null)
				{
					node.ClaudeSessionId = evt.SessionId;
					_appSettings.Save();
				}
			});
	}

	/// <summary>
	/// The hidden instruction block (FR.11.9) via the shared <see cref="InstructionBlockBuilder"/>, so interactive and headless turns
	/// emit identical instructions. <paramref name="isNewSession"/> is true only when this turn is about to start a fresh claude
	/// process, because the tool-usage bans are sent once per fresh process.
	/// </summary>
	private string BuildInstructionBlock(SessionNodeModel node, bool isNewBranch, bool isNewSession)
	{
		// FR.23.11 - pinned items and the user's Focus-view closures. Taking the closures clears their flag, so it is done only when
		// the block will actually report them.
		var isWorker     = node.OrchestrationDepth > 0;
		var hasAttention = _appSettings.Settings.AgentToolsEnabled && !isWorker;
		var nodeId       = node.NodeId;

		// FR.24.6 - same gate as the attention lines; taking it consumes the once-per-run flag, so only when it can be shown.
		var tagGuidanceDue = hasAttention && _tags.TakeTagGuidanceDue(nodeId);

		var options = new InstructionOptionsModel(
			node.IsAutoCommit, isNewBranch, node.IsAutoDocument,
			_appSettings.Settings.AgentToolsEnabled, isNewSession, node.IsStreamingMode,
			IsTelegramEnabled: _appSettings.Settings.TelegramEnabled,
			IsWorkerSession: isWorker,
			PinnedAttentionItems: hasAttention ? _attention.GetItems(nodeId) : null,
			UserClosedAttentionItems: hasAttention ? _attention.TakeUnreportedUserClosures(nodeId) : null,
			IsTagGuidanceDue: tagGuidanceDue,
			SessionTags: tagGuidanceDue ? _tags.GetTags(nodeId, node.Name) : null);
		return InstructionBlockBuilder.Build(options);
	}

	/// <summary>
	/// The MCP config and the Local Tool Profile of the turn (FR.12.21, FR.14.3, FR.14.11): a tool-capable local Ollama model gets the
	/// directory's curated <c>--tools</c> list plus a filtered, strict <c>--mcp-config</c>; MCP tools are a separate mechanism from the
	/// built-in <c>--tools</c> flag, so <c>--mcp-config</c> is skipped entirely for models that cannot call tools.
	/// </summary>
	private async Task<TurnLaunch> PrepareLaunchAsync(SessionNodeModel node, TurnSettings settings)
	{
		var localProfile = LocalToolProfileResolver.Resolve(settings.ModelInfo, settings.DirectoryModel);

		string? mcpConfigPath = null;
		var strictMcpConfig = false;
		LocalToolSpec? localToolSpec = null;

		if (localProfile != null)
		{
			strictMcpConfig = true;
			localToolSpec   = localProfile.ToolSpec;
			mcpConfigPath   = await _mcpServer.Value.EnsureConfigFileAsync(
				node.NodeId, node.AgentToken, localProfile.IncludeCmxMcp, localProfile.IncludedExternalMcpServerIds);
		}
		else if (_appSettings.Settings.AgentToolsEnabled && !settings.DisableTools)
			mcpConfigPath = await _mcpServer.Value.EnsureConfigFileAsync(node.NodeId, node.AgentToken);

		return new TurnLaunch(mcpConfigPath, localToolSpec, strictMcpConfig);
	}

	/// <summary>One-shot Mode: one process per turn, then the single OAuth-race retry and the single context retry.</summary>
	private async Task SendOneShotAsync(TurnRun run, ITurnObserver observer, SessionNodeModel node, TurnSettings settings,
		string? sessionId, string messageToSend, string augmentedMessage, TurnLaunch launch, CancellationToken ct)
	{
		await LaunchOneShotAsync(run, node, settings, sessionId, messageToSend, launch, ct);

		if (run.NeedsAuthRaceRetry)
		{
			run.NeedsAuthRaceRetry = false;
			_log.Information("Retrying turn after transient OAuth refresh race for session {FileName}", node.FileName);
			run.Progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryAuth);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			run.Progress.Status(TurnState.Running);

			await LaunchOneShotAsync(run, node, settings, sessionId, messageToSend, launch, ct);
		}

		if (run.NeedsContextRetry)
		{
			run.NeedsContextRetry = false;
			var enrichedMessage = InstructionBlockBuilder.BuildContextPreamble(_files.ReadEntries(node.FileName), augmentedMessage);

			observer.ContextRetrying();
			run.Progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryContext);
			run.Progress.Status(TurnState.Running);

			await LaunchOneShotAsync(run, node, settings, null, enrichedMessage, launch, ct);
		}
	}

	private Task LaunchOneShotAsync(TurnRun run, SessionNodeModel node, TurnSettings settings, string? sessionId, string message,
		TurnLaunch launch, CancellationToken ct) =>
		_processManager.SendMessageAsync(
			workingDirectory: node.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        sessionId,
			userMessage:      message,
			onEvent:          run.HandleStreamEvent,
			model:            settings.ModelId,
			profileConfigDir: settings.ProfileConfigDir,
			effort:           settings.Effort,
			mcpConfigPath:    launch.McpConfigPath,
			ollamaBaseUrl:    settings.OllamaBaseUrl,
			disableTools:     settings.DisableTools,
			chrome:           settings.Chrome,
			disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
			localToolSpec:    launch.LocalToolSpec,
			strictMcpConfig:  launch.StrictMcpConfig,
			cancellationToken: ct);

	/// <summary>
	/// Sends one turn via Streaming Session Mode (FR.19): ensures the session's persistent process is running (spawning it with the
	/// turn's model/profile/effort/mcp config if this is the first turn), then writes the turn to its open stdin and awaits the next
	/// result event. The run's event handler is bound as the process's primary callback, as a one-shot launch binds it.
	/// </summary>
	private async Task SendViaStreamingAsync(TurnRun run, ITurnObserver observer, TurnNodeContext context, SessionNodeModel node, TurnSettings settings,
		string? sessionId, string messageToSend, TurnLaunch launch, CancellationToken ct)
	{
		var nodeId = node.NodeId;
		var spawnParams = new StreamingSpawnParams(
			WorkingDirectory: node.WorkingDirectory,
			ClaudePath:       _appSettings.Settings.ClaudePath,
			SessionId:        sessionId,
			ProfileConfigDir: settings.ProfileConfigDir,
			Model:            settings.ModelId,
			Effort:           settings.Effort,
			McpConfigPath:    launch.McpConfigPath,
			OllamaBaseUrl:    settings.OllamaBaseUrl,
			DisableTools:     settings.DisableTools,
			Chrome:           settings.Chrome,
			LocalToolSpec:    launch.LocalToolSpec,
			StrictMcpConfig:  launch.StrictMcpConfig);

		var justStarted = _streamingManager.EnsureStarted(nodeId, spawnParams, StreamHandlerFor(context, node));
		if (justStarted)
			observer.StreamingProcessStarted();

		await _streamingManager.SendTurnAsync(nodeId, messageToSend, ct);

		if (run.NeedsAuthRaceRetry)
		{
			// ISSUE.010 - same transient cross-process OAuth refresh race as the one-shot path; the streaming process is still
			// alive, so retrying just writes the turn again.
			run.NeedsAuthRaceRetry = false;
			_log.Information("Retrying streaming turn after transient OAuth refresh race for session {FileName}", node.FileName);
			run.Progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryAuth);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			run.Progress.Status(TurnState.Running);
			await _streamingManager.SendTurnAsync(nodeId, messageToSend, ct);
		}
	}

	/// <summary>Opens the node's share of the running flag if the node does not hold it (one share per node, however many sends are queued).</summary>
	private void OpenRunning(TurnNodeContext context, string nodeId)
	{
		if (string.IsNullOrEmpty(nodeId) || !context.TryOpenRunning())
			return;

		_registry.SetSessionRunning(nodeId, true);
	}
}
