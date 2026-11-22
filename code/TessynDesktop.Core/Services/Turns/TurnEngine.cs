using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
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
	private readonly ISessionEngineRouter _engines;
	private readonly IAppSettingsService _appSettings;
	private readonly Lazy<ITelegramBridgeService> _telegramBridge;
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
		ISessionEngineRouter engines,
		IAppSettingsService appSettings,
		Lazy<ITelegramBridgeService> telegramBridge,
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
		_engines          = engines;
		_appSettings      = appSettings;
		_telegramBridge   = telegramBridge;
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

		// FR.33.1: the session's engine, resolved before anything is written to the transcript (today always Claude; the model->engine rule
		// comes with FR.33.4).
		var engine   = _engines.For(node);

		var context  = _nodes.GetOrAdd(nodeId, _ => new TurnNodeContext());
		var turnId   = request.TurnId ?? Guid.NewGuid().ToString(Constants.Turns.TurnIdFormat);
		observer   ??= new NullTurnObserver();

		var progress = new TurnProgressTracker(nodeId, turnId, _progressSinks);
		var run = new TurnRun(turnId, node, _files, observer, progress,
			settings.ModelId ?? Constants.Turns.DefaultOptionText, settings.Effort ?? Constants.Turns.DefaultOptionText, AuthErrorSink(nodeId), LimitErrorSink(nodeId));

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

			var sessionId = engine.GetConversationId(node);

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

			var preparation = await engine.PrepareTurnAsync(node, settings);

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
					engine.AdoptConversation(node, sessionId, settings.ProfileConfigDir);

				run.SendErrorText = null;
				context.Active = run;
				progress.Status(TurnState.Running);

				// Streaming Session Mode (FR.19) runs in the session's persistent process, whose sink routes every event to the active run.
				var persistent = node.IsStreamingMode;
				var engineRequest = new EngineTurnRequest(node, messageToSend, augmentedMessage, sessionId, settings, preparation,
					EngineTurnMode.Interactive, persistent, persistent ? StreamSinkFor(context, node) : null);
				await engine.RunTurnAsync(engineRequest, run, ct);

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
	private Action<string?>? LimitErrorSink(string nodeId) =>
		_authErrors == null ? null : text => _authErrors.SetLimitError(nodeId, text);

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
	/// The one sink bound to a node's persistent process (created once per node, bound again on every turn). It hands each event to the turn
	/// holding the lock, or to a fresh background run (see <see cref="TurnNodeStreamSink"/>).
	/// </summary>
	private IEngineEventSink StreamSinkFor(TurnNodeContext context, SessionNodeModel node) =>
		context.StreamSink ??= new TurnNodeStreamSink(context, () =>
			new TurnRun(string.Empty, node, _files, context.LastObserver ?? new NullTurnObserver(),
				new TurnProgressTracker(node.NodeId, string.Empty, []), null, null, AuthErrorSink(node.NodeId), LimitErrorSink(node.NodeId)));

	/// <inheritdoc />
	public void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt) =>
		_engines.For(node).SendCorrection(node, settings, prompt);

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

	/// <summary>Opens the node's share of the running flag if the node does not hold it (one share per node, however many sends are queued).</summary>
	private void OpenRunning(TurnNodeContext context, string nodeId)
	{
		if (string.IsNullOrEmpty(nodeId) || !context.TryOpenRunning())
			return;

		_registry.SetSessionRunning(nodeId, true);
	}
}
