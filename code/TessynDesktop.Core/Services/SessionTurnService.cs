using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnService : ISessionTurnService
{
	private static readonly ILogger _log = Log.ForContext<SessionTurnService>();

	private readonly IClaudeProcessManager           _processManager;
	private readonly ISessionFileService             _fileService;
	private readonly IAppSettingsService             _appSettings;
	private readonly IClaudeProfileService           _profileService;
	private readonly Lazy<IAgentMcpServer>           _mcpServer;
	private readonly ISessionRegistry                _registry;
	private readonly IClaudeSessionStatusService     _sessionStatus;
	private readonly IClaudeModelService             _modelService;
	private readonly Lazy<ITelegramBridgeService>    _telegramBridge;
	private readonly IClaudeStreamingSessionManager  _streamingManager;
	private readonly IAttentionService               _attention;
	private readonly ISessionTagService              _tags;
	private readonly IProfileLabelService            _profileLabels;
	private readonly IProfileResolver                _profileResolver;
	private readonly IProfileSwitchService?          _profileSwitch;
	private readonly IReadOnlyList<ITurnProgressSink> _progressSinks;

	// Per-node SemaphoreSlim(1,1): prevents concurrent --resume on the same ClaudeSessionId.
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _turnLocks = new();

	// Per-node CancellationTokenSource for CancelTurn support.
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCts = new();

	public SessionTurnService(
		IClaudeProcessManager processManager,
		ISessionFileService fileService,
		IAppSettingsService appSettings,
		IClaudeProfileService profileService,
		Lazy<IAgentMcpServer> mcpServer,
		ISessionRegistry registry,
		IClaudeSessionStatusService sessionStatus,
		IClaudeModelService modelService,
		Lazy<ITelegramBridgeService> telegramBridge,
		IClaudeStreamingSessionManager streamingManager,
		IAttentionService attention,
		ISessionTagService tags,
		IProfileLabelService profileLabels,
		IEnumerable<ITurnProgressSink>? progressSinks = null,
		IProfileResolver? profileResolver = null,
		IProfileSwitchService? profileSwitch = null)
	{
		_processManager   = processManager;
		_fileService      = fileService;
		_appSettings      = appSettings;
		_profileService   = profileService;
		_mcpServer        = mcpServer;
		_registry         = registry;
		_sessionStatus    = sessionStatus;
		_modelService     = modelService;
		_telegramBridge   = telegramBridge;
		_streamingManager = streamingManager;
		_attention        = attention;
		_tags             = tags;
		_profileLabels    = profileLabels;
		_progressSinks    = progressSinks?.ToArray() ?? [];
		_profileResolver  = profileResolver ?? new ProfileResolver(appSettings, profileService);
		_profileSwitch    = profileSwitch;
	}

	/// <summary>
	/// FR.23.11 — the session's items for the Pinned list. Only a top-level session with agent tools
	/// enabled gets them (the same gate the builder applies), so nothing is read for a worker.
	/// </summary>
	private IReadOnlyList<AttentionItemModel>? PinnedItemsFor(SessionNodeModel node) =>
		HasAttentionTools(node) ? _attention.GetItems(node.NodeId) : null;

	/// <summary>
	/// FR.23.11 — the user's Focus-view closures since the last turn. Taking clears their flag, so it is
	/// done only when the block will actually report them.
	/// </summary>
	private IReadOnlyList<AttentionItemModel>? TakeUserClosuresFor(SessionNodeModel node) =>
		HasAttentionTools(node) ? _attention.TakeUnreportedUserClosures(node.NodeId) : null;

	/// <summary>
	/// FR.24.6 — true on the first turn of this top-level session in this app run. Consumed only when the
	/// builder can emit the line (agent tools on, not a Worker Session), so a turn that cannot show it keeps it due.
	/// </summary>
	private bool TakeTagGuidanceDueFor(SessionNodeModel node) =>
		HasAttentionTools(node) && _tags.TakeTagGuidanceDue(node.NodeId);

	private bool HasAttentionTools(SessionNodeModel node) =>
		_appSettings.Settings.AgentToolsEnabled && node.OrchestrationDepth == 0;

	public SemaphoreSlim GetTurnLock(string nodeId) =>
		_turnLocks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));

	public bool CancelTurn(string nodeId)
	{
		if (!_activeCts.TryGetValue(nodeId, out var cts))
			return false;
		cts.Cancel();
		return true;
	}

	public Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default) =>
		RunTurnAsync(node, prompt, source, turnOptions: null, cancellationToken);

	public async Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		TurnOptionsModel? turnOptions,
		CancellationToken cancellationToken = default)
	{
		// P1.I: the same progress notifications as an engine turn (the sinks decide who sees them).
		var turnId   = Guid.NewGuid().ToString(Constants.Turns.TurnIdFormat);
		var progress = new TurnProgressTracker(node.NodeId, turnId, _progressSinks);
		progress.Started(null);

		var turnLock = GetTurnLock(node.NodeId);
		if (turnLock.CurrentCount == 0)
			progress.Status(TurnState.Queued);

		try
		{
			await turnLock.WaitAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			// Stopped while queued: the turn never ran, so the progress sink reports its end.
			progress.Finished(ex is OperationCanceledException, ex is OperationCanceledException ? null : ex.Message, ran: false);
			throw;
		}

		progress.Status(TurnState.Running);

		// Register a per-node CTS so CancelTurn can interrupt this turn.
		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_activeCts[node.NodeId] = linkedCts;
		var ct = linkedCts.Token;

		// Signal running state via the registry (SessionTreeViewModel observes TreeChanged).
		_registry.SetSessionRunning(node.NodeId, true);

		// FR.21.22 — reported once from the finally block, after the turn's last entry is appended.
		var turnCancelled = false;
		string? turnErrorText = null;

		try
		{
			// FR.21.2 — lazily ensure a Telegram topic exists for this session on its first activity
			// since the bridge was enabled. Fire-and-forget: EnsureTopicForSessionAsync never throws
			// and must never delay or fail the turn it was piggybacked on. Inside the try so that a failing
			// Lazy can never leave the node marked running with the turn lock held.
			_ = _telegramBridge.Value.EnsureTopicForSessionAsync(node);

			// ISSUE.013: a switch queued while this session was running is applied now (nothing else uses its files).
			_profileSwitch?.ApplyPending(node);

			// The profile this turn uses: the per-turn choice, else the pinned (ProfileId) / last-recorded one, else Default.
			// A pinned profile that no longer resolves is an error, never a silent fallback to Default.
			// A live streaming process ignores the per-turn options (below), so a per-turn profile is not resolved for it.
			var perTurnProfile = _streamingManager.IsAlive(node.NodeId) ? null : turnOptions?.ProfileName;
			var profile = _profileResolver.Resolve(node, ResolveProfileName(node), perTurnProfile);
			if (!profile.IsResolved)
			{
				_log.Warning("Turn for node {NodeId} refused: {Error}", node.NodeId, profile.Error);
				turnErrorText = profile.Error;
				return new TurnResultModel(string.Empty, node.ClaudeSessionId, IsError: true, ErrorMessage: profile.Error, Refused: true);
			}

			// Write clean prompt (without instruction block) to the session file. Common to both
			// branches below — the streaming branch needs it recorded here too, since (unlike the
			// interactive desktop path) nothing else writes the user's own message for a headless turn.
			// Stamped with the profile display name actually used, as the desktop does, so that
			// "last used profile" restores correctly afterwards.
			var stampedProfileName = profile.IsDefault ? await _profileLabels.GetDefaultLabelAsync() : profile.DisplayName;
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, prompt, profileName: stampedProfileName,
				source: EntrySources.FromTurnSource(source));

			// FR.19.13 — if a Streaming Session Mode process is already alive for this node (started
			// by the interactive desktop path), route this headless turn into it instead of spawning
			// a competing one-shot --resume process against the same ClaudeSessionId. Two independent
			// processes touching the same session caused a confirmed bug: the one-shot process reads
			// and extends the on-disk JSONL, but the already-running persistent process has no way to
			// notice — its own in-memory context silently falls behind, so its next answer looks like
			// it "forgot" the headless-sourced exchange. See docs/issues/Streaming-Headless-Context-Divergence.md.
			if (_streamingManager.IsAlive(node.NodeId))
			{
				if (turnOptions != null && (turnOptions.ModelId != null || turnOptions.Effort != null || turnOptions.ProfileName != null))
					_log.Information("Turn for node {NodeId} is redirected into the live streaming process: per-turn model/effort/profile options are ignored", node.NodeId);

				var streamed  = await RunTurnViaStreamingAsync(node, prompt, source, progress, ct);
				turnErrorText = streamed.IsError ? streamed.ErrorMessage : null;
				return streamed;
			}

			// ISSUE.013 R1/R3/R7: a per-turn profile change moves the session (no cascade) and records the ProfileId pin.
			_profileSwitch?.RecordTurnProfile(node, stampedProfileName);
			var profileConfigDir = profile.ConfigDir;

			// Resolve effective model for this session (FR.16.5 bug fix):
			// per-turn choice → node.ModelId → per-directory setting → app-level setting.
			var effectiveModelId = ResolveTurnModel(node, turnOptions);
			var effort           = NormalizeEffort(turnOptions?.Effort);
			var modelInfo        = string.IsNullOrEmpty(effectiveModelId)
				? null
				: _modelService.GetCachedModels().FirstOrDefault(m => m.Id == effectiveModelId);
			var ollamaBaseUrl    = modelInfo?.Provider == ModelProvider.Ollama
				? _appSettings.Settings.OllamaBaseUrl
				: null;
			var disableTools     = modelInfo is { Provider: ModelProvider.Ollama, SupportsTools: false };

			// Local Tool Profile (FR.12.21): a tool-capable local Ollama model gets the directory's
			// curated --tools list plus a filtered, --strict-mcp-config'd --mcp-config instead of the
			// unrestricted Anthropic default. LocalToolProfileResolver is the single source of truth
			// shared with the interactive TurnController path.
			var directoryModel = _appSettings.Settings.Tree.FirstOrDefault(d =>
				string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
			var localProfile   = LocalToolProfileResolver.Resolve(modelInfo, directoryModel);

			string? mcpConfigPath;
			var strictMcpConfig  = false;
			LocalToolSpec? localToolSpec = null;

			if (localProfile != null)
			{
				strictMcpConfig = true;
				localToolSpec   = localProfile.ToolSpec;
				mcpConfigPath   = await _mcpServer.Value.EnsureConfigFileAsync(
					node.NodeId, node.AgentToken, localProfile.IncludeCmxMcp, localProfile.IncludedExternalMcpServerIds);
			}
			else
			{
				// MCP tools (e.g. the cmx orchestration server) are a separate mechanism from the
				// built-in --tools flag — the CLI still sends them to the model even when --tools ""
				// is set. Skip --mcp-config entirely for models that don't support tool calling.
				mcpConfigPath = _appSettings.Settings.AgentToolsEnabled && !disableTools
					? await _mcpServer.Value.EnsureConfigFileAsync(node.NodeId, node.AgentToken)
					: null;
			}

			// Cross-profile session adoption (FR.11.13): before validating resumability, try to
			// locate the session's JSONL under the resolved profile and, if it's actually sitting
			// under a different profile, move it into place so --resume works there natively.
			// If the session was wiped (compaction, cleanup, context window reset) or truly cannot
			// be found under any profile, fall through to the context-preamble path which rebuilds
			// context from the TessynDesktop JSONL file.
			if (node.ClaudeSessionId != null
			    && !_sessionStatus.TryAdoptSessionFromOtherProfile(node.WorkingDirectory, node.ClaudeSessionId, profileConfigDir))
			{
				_log.Information("Session {SessionId} is no longer resumable — clearing to use context preamble", node.ClaudeSessionId);
				node.ClaudeSessionId = null;
				_appSettings.Save();
			}

			// Build instruction block using persisted per-session toggle states. IsNewSession is
			// evaluated here (after the resumability check above) so the tool-usage bans re-inject
			// whenever a fresh claude process is actually about to start — including a context reset —
			// not just on the session's very first-ever turn.
			// IsStreamingMode is false here — this is the one-shot --resume branch, reached only when
			// no Streaming Session Mode process is alive for this node (FR.19.11/19.13); when one is
			// alive, RunTurnViaStreamingAsync (above) handles the turn instead, with IsStreamingMode true.
			var tagGuidanceDue   = TakeTagGuidanceDueFor(node);
			var options          = new InstructionOptionsModel(
				node.IsAutoCommit, false, node.IsAutoDocument,
				_appSettings.Settings.AgentToolsEnabled, node.ClaudeSessionId == null, IsStreamingMode: false,
				IsTelegramEnabled: _appSettings.Settings.TelegramEnabled, IsWorkerSession: node.OrchestrationDepth > 0,
				IsScheduledTurn: source == TurnSource.Scheduled,
				PinnedAttentionItems: PinnedItemsFor(node), UserClosedAttentionItems: TakeUserClosuresFor(node),
				IsTagGuidanceDue: tagGuidanceDue, SessionTags: tagGuidanceDue ? _tags.GetTags(node.NodeId, node.Name) : null);
			var instructionBlock = InstructionBlockBuilder.Build(options);
			var augmentedPrompt  = prompt + instructionBlock;

			// Proactive context reload: if no ClaudeSessionId but the file has history, prepend preamble.
			var sessionId      = node.ClaudeSessionId;
			var messageToSend  = augmentedPrompt;
			if (sessionId == null)
			{
				var entries     = _fileService.ReadEntries(node.FileName);
				var priorTurns  = entries.Where(e => e.Role is Constants.SessionFile.RoleUser
				                                            or Constants.SessionFile.RoleAssistant).ToList();
				// More than just the message we just appended means there is prior history.
				if (priorTurns.Count > 1)
					messageToSend = InstructionBlockBuilder.BuildContextPreamble(entries, augmentedPrompt);
			}

			var resultText          = new StringBuilder();
			var isError             = false;
			string? errorMessage    = null;
			int inputTokens         = 0, outputTokens = 0;
			double costUsd          = 0;
			var firstAssistant      = true;
			var pendingModelId      = effectiveModelId ?? Constants.Remote.DefaultOptionId;
			var pendingEffort       = effort ?? Constants.Remote.DefaultOptionId;
			var needsAuthRaceRetry  = false;

			await _processManager.SendMessageAsync(
				workingDirectory: node.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        sessionId,
				userMessage:      messageToSend,
				onEvent:          evt =>
				{
					HandleEvent(evt, node, resultText, ref isError, ref errorMessage,
					            ref inputTokens, ref outputTokens, ref costUsd,
					            ref firstAssistant, ref needsAuthRaceRetry, pendingModelId, pendingEffort);
					progress.Observe(evt);
				},
				model:            effectiveModelId,
				profileConfigDir: profileConfigDir,
				effort:           effort,
				mcpConfigPath:    mcpConfigPath,
				ollamaBaseUrl:    ollamaBaseUrl,
				disableTools:     disableTools,
				disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
				localToolSpec:    localToolSpec,
				strictMcpConfig:  strictMcpConfig,
				cancellationToken: ct);

			if (needsAuthRaceRetry)
			{
				// ISSUE.010 — transient cross-process OAuth refresh-token race, not a real auth
				// failure (see Constants.Auth.OAuthRefreshRaceMarker). Retry once, silently.
				needsAuthRaceRetry = false;
				progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryAuth);
				_log.Information("OAuth refresh race for node {NodeId} — retrying turn once", node.NodeId);
				await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
				progress.Status(TurnState.Running);

				await _processManager.SendMessageAsync(
					workingDirectory: node.WorkingDirectory,
					claudePath:       _appSettings.Settings.ClaudePath,
					sessionId:        sessionId,
					userMessage:      messageToSend,
					onEvent:          evt =>
					{
						HandleEvent(evt, node, resultText, ref isError, ref errorMessage,
						            ref inputTokens, ref outputTokens, ref costUsd,
						            ref firstAssistant, ref needsAuthRaceRetry, pendingModelId, pendingEffort);
						progress.Observe(evt);
					},
					model:            effectiveModelId,
					profileConfigDir: profileConfigDir,
					effort:           effort,
					mcpConfigPath:    mcpConfigPath,
					ollamaBaseUrl:    ollamaBaseUrl,
					disableTools:     disableTools,
					disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
					localToolSpec:    localToolSpec,
					strictMcpConfig:  strictMcpConfig,
					cancellationToken: ct);
			}

			// Errors the retry above recovered from never reach errorMessage (the race result is not
			// recorded as an error), so a successful retry reports no error text.
			turnErrorText = isError ? errorMessage : null;
			return new TurnResultModel(resultText.ToString(), node.ClaudeSessionId, isError, errorMessage,
			                           inputTokens, outputTokens, costUsd, Note: profile.Note);
		}
		catch (OperationCanceledException)
		{
			_log.Information("Turn cancelled for node {NodeId}", node.NodeId);
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, "[Turn cancelled]");
			turnCancelled = true;
			turnErrorText = null;
			return new TurnResultModel(string.Empty, node.ClaudeSessionId, IsError: true, ErrorMessage: "Cancelled");
		}
		catch (Exception ex)
		{
			// Unexpected failure: the turn still ended, so TurnCompleted fires (not cancelled) with the
			// exception message as its error text; the exception itself propagates unchanged.
			turnErrorText = ex.Message;
			throw;
		}
		finally
		{
			// The progress sinks flush first, so a turn's last notifications precede its turn.completed.
			progress.Finished(turnCancelled, turnErrorText, ran: true);

			// FR.21.22 — before the turn lock is released, so the event stays ordered ahead of the next turn.
			try
			{
				_fileService.NotifyTurnCompleted(node.FileName, turnCancelled, turnErrorText, turnId);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "TurnCompleted handler failed for node {NodeId}", node.NodeId);
			}

			_activeCts.TryRemove(node.NodeId, out _);
			linkedCts.Dispose();
			turnLock.Release();

			_registry.SetSessionRunning(node.NodeId, false);
		}
	}

	/// <summary>
	/// Routes a headless turn (scheduler/orchestration/Telegram) into an already-alive Streaming
	/// Session Mode process instead of a competing one-shot --resume process (FR.19.13). Deliberately
	/// does NOT write the assistant/system output to the session file or touch node.ClaudeSessionId
	/// here — the process's primary event callback (bound by the SessionViewModel/TurnController that
	/// started it and keeps it alive for as long as the session node exists) already does both for
	/// every event on this process, exactly as if the message had been typed on the desktop. This
	/// method only observes the same events, additively, to build the TurnResultModel this headless
	/// caller needs to return — see IClaudeStreamingSessionManager.SendTurnAsync's onTurnEvent param.
	/// </summary>
	private async Task<TurnResultModel> RunTurnViaStreamingAsync(SessionNodeModel node, string prompt, TurnSource source, TurnProgressTracker progress, CancellationToken ct)
	{
		var tagGuidanceDue = TakeTagGuidanceDueFor(node);
		var options = new InstructionOptionsModel(
			node.IsAutoCommit, false, node.IsAutoDocument,
			_appSettings.Settings.AgentToolsEnabled, node.ClaudeSessionId == null, IsStreamingMode: true,
			IsTelegramEnabled: _appSettings.Settings.TelegramEnabled, IsWorkerSession: node.OrchestrationDepth > 0,
			IsScheduledTurn: source == TurnSource.Scheduled,
			PinnedAttentionItems: PinnedItemsFor(node), UserClosedAttentionItems: TakeUserClosuresFor(node),
			IsTagGuidanceDue: tagGuidanceDue, SessionTags: tagGuidanceDue ? _tags.GetTags(node.NodeId, node.Name) : null);
		var augmentedPrompt = prompt + InstructionBlockBuilder.Build(options);

		var resultText         = new StringBuilder();
		var isError            = false;
		string? errorMessage   = null;
		int inputTokens = 0, outputTokens = 0;
		double costUsd  = 0;
		var needsAuthRaceRetry = false;

		void ObserveEvent(ClaudeStreamEvent evt)
		{
			progress.Observe(evt);

			switch (evt.Type)
			{
				case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
					resultText.Append(evt.Content);
					break;
				case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
					&& evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase):
					// ISSUE.010 — transient cross-process OAuth refresh-token race, not a real auth
					// failure. TurnController's own HandleStreamEvent (the process's primary callback)
					// already suppressed the file write for this event — just flag the retry here too.
					needsAuthRaceRetry = true;
					break;
				case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
					isError      = true;
					errorMessage = evt.Content;
					break;
				case "result" when !evt.IsError:
					inputTokens  = evt.InputTokens;
					outputTokens = evt.OutputTokens;
					costUsd      = evt.CostUsd;
					break;
			}
		}

		await _streamingManager.SendTurnAsync(node.NodeId, augmentedPrompt, ct, ObserveEvent);

		if (needsAuthRaceRetry)
		{
			needsAuthRaceRetry = false;
			progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryAuth);
			_log.Information("OAuth refresh race for node {NodeId} (streaming) — retrying turn once", node.NodeId);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			progress.Status(TurnState.Running);
			await _streamingManager.SendTurnAsync(node.NodeId, augmentedPrompt, ct, ObserveEvent);
		}

		return new TurnResultModel(resultText.ToString(), node.ClaudeSessionId, isError, errorMessage,
		                           inputTokens, outputTokens, costUsd);
	}

	private void HandleEvent(
		ClaudeStreamEvent evt,
		SessionNodeModel node,
		StringBuilder resultText,
		ref bool isError,
		ref string? errorMessage,
		ref int inputTokens,
		ref int outputTokens,
		ref double costUsd,
		ref bool firstAssistant,
		ref bool needsAuthRaceRetry,
		string pendingModelId,
		string pendingEffort)
	{
		switch (evt.Type)
		{
			case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
				string? fileModelId = null, fileEffort = null;
				if (firstAssistant)
				{
					firstAssistant = false;
					fileModelId    = pendingModelId;
					fileEffort     = pendingEffort;
				}
				_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleAssistant, evt.Content,
					modelId: fileModelId, effort: fileEffort);
				resultText.Append(evt.Content);
				break;

			case "system" when evt.Subtype is "compact":
				_fileService.AppendCompactionSeparator(node.FileName);
				break;

			case "system" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				// Suppress "No conversation found" (context reload not yet implemented for headless
				// turns, backlog item) and the ISSUE.010 OAuth refresh-race marker (the result event
				// below already flags the retry) — both are transient infrastructure noise, not
				// user-facing failures.
				if (!evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker,
				        StringComparison.OrdinalIgnoreCase)
				    && !evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker,
				        StringComparison.OrdinalIgnoreCase))
					_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				break;

			case "result" when !evt.IsError && evt.SessionId is not null:
				node.ClaudeSessionId = evt.SessionId;
				inputTokens  = evt.InputTokens;
				outputTokens = evt.OutputTokens;
				costUsd      = evt.CostUsd;
				_appSettings.Save();
				break;

			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase):
				// ISSUE.010 — transient cross-process OAuth refresh-token race, not a real auth
				// failure. Flag for the caller's single silent auto-retry; don't record as an error.
				needsAuthRaceRetry = true;
				break;

			case "result" when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				isError      = true;
				errorMessage = evt.Content;
				_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				break;
		}
	}

	/// <summary>
	/// Resolves the effective model ID for a session: node.ModelId → directory SelectedModelId
	/// → app-level SelectedModelId. Returns null/empty if nothing is configured.
	/// </summary>
	private string? ResolveEffectiveModel(SessionNodeModel node)
	{
		if (!string.IsNullOrEmpty(node.ModelId))
			return node.ModelId;

		var dirModel = _appSettings.Settings.Tree.FirstOrDefault(d =>
			string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrEmpty(dirModel?.SelectedModelId))
			return dirModel.SelectedModelId;

		return string.IsNullOrEmpty(_appSettings.Settings.SelectedModelId)
			? null
			: _appSettings.Settings.SelectedModelId;
	}

	/// <summary>
	/// Resolves the profile display name for a session: the pinned/inherited
	/// <see cref="SessionNodeModel.ProfileName"/> takes precedence (orchestrated children inherit
	/// their supervisor's profile at spawn — ISSUE-002); otherwise the last user entry in the
	/// session's own file history that recorded a profile name, mirroring
	/// SessionViewModel.RestoreLastUsedSettings (ISSUE-001). Null when nothing is recorded → Default.
	/// </summary>
	public string? ResolveProfileName(SessionNodeModel node)
	{
		if (!string.IsNullOrEmpty(node.ProfileName))
			return node.ProfileName;

		var entries = _fileService.ReadEntries(node.FileName);
		var lastUserWithProfile = entries.LastOrDefault(
			e => e.Role == Constants.SessionFile.RoleUser && e.ProfileName != null);

		return lastUserWithProfile?.ProfileName;
	}

	/// <summary>
	/// The model of one turn: the per-turn choice (<c>"default"</c> = no --model flag), else
	/// <see cref="ResolveEffectiveModel"/>.
	/// </summary>
	private string? ResolveTurnModel(SessionNodeModel node, TurnOptionsModel? options)
	{
		var requested = options?.ModelId;
		if (string.IsNullOrEmpty(requested))
			return ResolveEffectiveModel(node);

		return string.Equals(requested, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase) ? null : requested;
	}

	/// <summary>The --effort value for the turn; null (no flag) for a blank or "default" effort.</summary>
	private static string? NormalizeEffort(string? effort) =>
		string.IsNullOrWhiteSpace(effort) || string.Equals(effort, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase)
			? null
			: effort;
}
