using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;
using TessynDesktop.Models.Remote;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnService : ISessionTurnService
{
	private static readonly ILogger _log = Log.ForContext<SessionTurnService>();

	private readonly ISessionEngineRouter            _engines;
	private readonly ISessionFileService             _fileService;
	private readonly IAppSettingsService             _appSettings;
	private readonly IClaudeProfileService           _profileService;
	private readonly ISessionRegistry                _registry;
	private readonly IClaudeModelService             _modelService;
	private readonly Lazy<ITelegramBridgeService>    _telegramBridge;
	private readonly IAttentionService               _attention;
	private readonly ISessionTagService              _tags;
	private readonly IProfileLabelService            _profileLabels;
	private readonly IProfileResolver                _profileResolver;
	private readonly IProfileSwitchService?          _profileSwitch;
	private readonly ISessionModelSettings           _modelSettings;
	private readonly IReadOnlyList<ITurnProgressSink> _progressSinks;
	private readonly TessynDesktop.Codex.ICodexProfileResolver? _codexProfiles;
	private readonly UserEntryIdLocator _userEntries;

	// Per-node SemaphoreSlim(1,1): prevents concurrent --resume on the same ClaudeSessionId.
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _turnLocks = new();

	// Per-node CancellationTokenSource for CancelTurn support.
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCts = new();

	public SessionTurnService(
		ISessionEngineRouter engines,
		ISessionFileService fileService,
		IAppSettingsService appSettings,
		IClaudeProfileService profileService,
		ISessionRegistry registry,
		IClaudeModelService modelService,
		Lazy<ITelegramBridgeService> telegramBridge,
		IAttentionService attention,
		ISessionTagService tags,
		IProfileLabelService profileLabels,
		IEnumerable<ITurnProgressSink>? progressSinks = null,
		IProfileResolver? profileResolver = null,
		IProfileSwitchService? profileSwitch = null,
		ISessionModelSettings? modelSettings = null,
		TessynDesktop.Codex.ICodexProfileResolver? codexProfiles = null,
		ISessionEntryReader? entries = null)
	{
		_codexProfiles    = codexProfiles;
		_userEntries      = new UserEntryIdLocator(entries);
		_engines          = engines;
		_fileService      = fileService;
		_appSettings      = appSettings;
		_profileService   = profileService;
		_registry         = registry;
		_modelService     = modelService;
		_telegramBridge   = telegramBridge;
		_attention        = attention;
		_tags             = tags;
		_profileLabels    = profileLabels;
		_progressSinks    = progressSinks?.ToArray() ?? [];
		_profileResolver  = profileResolver ?? new ProfileResolver(appSettings, profileService);
		_profileSwitch    = profileSwitch;
		_modelSettings    = modelSettings ?? new SessionModelSettings(appSettings, modelService);
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
		// FR.33.1, FR.33.4: the turn's model and effort (FR.16.5, FR.12.35: the per-turn choice, else the session's own setting, else (model only) the
		// directory's and the app's setting), then the engine of that model, resolved (and stored) before anything is written to the transcript.
		var turnModel = _modelSettings.ResolveTurn(node, turnOptions);
		// Looked at only: the engine is stored and an engine change applied under the turn lock below, never while another turn runs.
		var engine    = _engines.Peek(node, turnModel.ModelId);

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

			// FR.33.4: the turn's engine, stored on the session now that no other turn of it runs.
			engine      = _engines.ForTurn(node, turnModel.ModelId);
			var isCodex = string.Equals(engine.EngineId, Constants.SessionEngines.Codex, StringComparison.OrdinalIgnoreCase);

			// The profile this turn uses: the per-turn choice, else the pinned (ProfileId) / last-recorded one, else Default.
			// A pinned profile that no longer resolves is an error, never a silent fallback to Default.
			// A live streaming process ignores the per-turn options (below), so a per-turn profile is not resolved for it.
			var perTurnProfile = engine.IsAlive(node.NodeId) ? null : turnOptions?.ProfileName;

			// FR.33.5: a Codex turn runs on a ChatGPT profile (its pin, exact, else the inherit rule); a Claude turn on a Claude profile.
			TessynDesktop.Codex.CodexProfileChoice? codexProfile = null;
			ProfileResolution? profile = null;
			string? refusal;
			if (isCodex && _codexProfiles != null)
			{
				codexProfile = _codexProfiles.ResolveForTurn(node, perTurnProfile);
				refusal      = codexProfile.IsResolved ? null : codexProfile.Error;
			}
			else
			{
				profile = _profileResolver.Resolve(node, ResolveProfileName(node), perTurnProfile);
				refusal = profile.IsResolved ? null : profile.Error;
			}

			if (refusal != null)
			{
				_log.Warning("Turn for node {NodeId} refused: {Error}", node.NodeId, refusal);
				turnErrorText = refusal;
				return new TurnResultModel(string.Empty, engine.GetConversationId(node), IsError: true, ErrorMessage: refusal, Refused: true);
			}

			// Write clean prompt (without instruction block) to the session file. Common to both
			// branches below — the streaming branch needs it recorded here too, since (unlike the
			// interactive desktop path) nothing else writes the user's own message for a headless turn.
			// Stamped with the profile display name actually used, as the desktop does, so that
			// "last used profile" restores correctly afterwards.
			var stampedProfileName = codexProfile?.DisplayName
				?? (profile!.IsDefault ? await _profileLabels.GetDefaultLabelAsync() : profile.DisplayName);

			// FR.33.4: the first turn on a new engine detaches the other engine's conversation (the new one is seeded from the transcript below).
			if (_engines.ApplyEngineChange(node, engine) is { } engineChanged)
				_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, engineChanged);

			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, prompt, profileName: stampedProfileName,
				source: EntrySources.FromTurnSource(source));
			var userEntryId = _userEntries.Locate(node.FileName, prompt);

			// FR.19.13 — if a Streaming Session Mode process is already alive for this node (started
			// by the interactive desktop path), route this headless turn into it instead of spawning
			// a competing one-shot --resume process against the same ClaudeSessionId. Two independent
			// processes touching the same session caused a confirmed bug: the one-shot process reads
			// and extends the on-disk JSONL, but the already-running persistent process has no way to
			// notice — its own in-memory context silently falls behind, so its next answer looks like
			// it "forgot" the headless-sourced exchange. See docs/issues/Streaming-Headless-Context-Divergence.md.
			if (engine.IsAlive(node.NodeId))
			{
				if (turnOptions != null && (turnOptions.ModelId != null || turnOptions.Effort != null || turnOptions.ProfileName != null))
					_log.Information("Turn for node {NodeId} is redirected into the live streaming process: per-turn model/effort/profile options are ignored", node.NodeId);

				var streamed  = await RunTurnViaStreamingAsync(engine, node, prompt, source, progress, ct);
				turnErrorText = streamed.IsError ? streamed.ErrorMessage : null;
				return streamed;
			}

			// ISSUE.013 R1/R3/R7: a per-turn profile change moves the session (no cascade) and records the ProfileId pin.
			if (!isCodex)
				_profileSwitch?.RecordTurnProfile(node, stampedProfileName);
			var profileConfigDir = profile?.ConfigDir;

			var effectiveModelId = turnModel.ModelId;
			var effort           = turnModel.Effort;
			var modelInfo        = string.IsNullOrEmpty(effectiveModelId)
				? null
				: _modelService.GetCachedModels().FirstOrDefault(m => m.Id == effectiveModelId);
			var ollamaBaseUrl    = modelInfo?.Provider == ModelProvider.Ollama
				? _appSettings.Settings.OllamaBaseUrl
				: null;
			var disableTools     = modelInfo is { Provider: ModelProvider.Ollama, SupportsTools: false };

			// Local Tool Profile (FR.12.21): a tool-capable local Ollama model gets the directory's
			// curated --tools list plus a filtered, --strict-mcp-config'd --mcp-config instead of the
			// unrestricted Anthropic default; the engine prepares it (and the MCP config) from these settings,
			// the same way as for the interactive path.
			var directoryModel = _appSettings.Settings.Tree.FirstOrDefault(d =>
				string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
			var settings       = new TurnSettings(effectiveModelId, effort, profileConfigDir, stampedProfileName, ollamaBaseUrl, disableTools,
				Chrome: false, modelInfo, directoryModel, codexProfile?.Profile);
			var preparation    = await engine.PrepareTurnAsync(node, settings);

			// Cross-profile session adoption (FR.11.13): before validating resumability, try to
			// locate the session's JSONL under the resolved profile and, if it's actually sitting
			// under a different profile, move it into place so --resume works there natively.
			// If the session was wiped (compaction, cleanup, context window reset) or truly cannot
			// be found under any profile, fall through to the context-preamble path which rebuilds
			// context from the TessynDesktop JSONL file.
			var resumedId = engine.GetConversationId(node);
			if (resumedId != null && !engine.AdoptConversation(node, resumedId, profileConfigDir))
			{
				_log.Information("Session {SessionId} is no longer resumable — clearing to use context preamble", resumedId);
				engine.ClearConversation(node);
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
				_appSettings.Settings.AgentToolsEnabled, engine.GetConversationId(node) == null, IsStreamingMode: false,
				IsTelegramEnabled: _appSettings.Settings.TelegramEnabled, IsWorkerSession: node.OrchestrationDepth > 0,
				IsScheduledTurn: source == TurnSource.Scheduled,
				PinnedAttentionItems: PinnedItemsFor(node), UserClosedAttentionItems: TakeUserClosuresFor(node),
				IsTagGuidanceDue: tagGuidanceDue, SessionTags: tagGuidanceDue ? _tags.GetTags(node.NodeId, node.Name) : null,
				EngineId: engine.EngineId);
			var instructionBlock = InstructionBlockBuilder.Build(options);
			var augmentedPrompt  = prompt + instructionBlock;

			// Proactive context reload: if no ClaudeSessionId but the file has history, prepend preamble.
			var sessionId      = engine.GetConversationId(node);
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

			var sink = new HeadlessTurnSink(node, _fileService, progress, persists: true,
				effectiveModelId ?? Constants.Remote.DefaultOptionId, effort ?? Constants.Remote.DefaultOptionId);

			// The one-shot --resume process with its single silent OAuth-race retry (ISSUE.010) runs in the engine.
			await engine.RunTurnAsync(
				new EngineTurnRequest(node, messageToSend, augmentedPrompt, sessionId, settings, preparation, EngineTurnMode.Headless, UserEntryId: userEntryId),
				sink, ct);

			// Errors the retry above recovered from never reach errorMessage (the race result is not
			// recorded as an error), so a successful retry reports no error text.
			turnErrorText = sink.IsError ? sink.ErrorMessage : null;
			return new TurnResultModel(sink.ResultText, engine.GetConversationId(node), sink.IsError, sink.ErrorMessage,
			                           sink.InputTokens, sink.OutputTokens, sink.CostUsd, Note: profile?.Note);
		}
		catch (OperationCanceledException)
		{
			_log.Information("Turn cancelled for node {NodeId}", node.NodeId);
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, "[Turn cancelled]");
			turnCancelled = true;
			turnErrorText = null;
			return new TurnResultModel(string.Empty, engine.GetConversationId(node), IsError: true, ErrorMessage: "Cancelled");
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
	/// here — the process's primary event sink (bound by the interactive turn that started it, for as
	/// long as the process lives) already does both for every event on this process, exactly as if the
	/// message had been typed on the desktop. This method only observes the same events, additively, to
	/// build the TurnResultModel this headless caller needs to return — see
	/// ISessionEngine.RunTurnInLiveProcessAsync.
	/// </summary>
	private async Task<TurnResultModel> RunTurnViaStreamingAsync(ISessionEngine engine, SessionNodeModel node, string prompt, TurnSource source, TurnProgressTracker progress, CancellationToken ct)
	{
		var tagGuidanceDue = TakeTagGuidanceDueFor(node);
		var options = new InstructionOptionsModel(
			node.IsAutoCommit, false, node.IsAutoDocument,
			_appSettings.Settings.AgentToolsEnabled, engine.GetConversationId(node) == null, IsStreamingMode: true,
			IsTelegramEnabled: _appSettings.Settings.TelegramEnabled, IsWorkerSession: node.OrchestrationDepth > 0,
			IsScheduledTurn: source == TurnSource.Scheduled,
			PinnedAttentionItems: PinnedItemsFor(node), UserClosedAttentionItems: TakeUserClosuresFor(node),
			IsTagGuidanceDue: tagGuidanceDue, SessionTags: tagGuidanceDue ? _tags.GetTags(node.NodeId, node.Name) : null,
			EngineId: engine.EngineId);
		var augmentedPrompt = prompt + InstructionBlockBuilder.Build(options);

		var sink = new HeadlessTurnSink(node, _fileService, progress, persists: false);
		await engine.RunTurnInLiveProcessAsync(node, augmentedPrompt, sink, ct);

		return new TurnResultModel(sink.ResultText, engine.GetConversationId(node), sink.IsError, sink.ErrorMessage,
		                           sink.InputTokens, sink.OutputTokens, sink.CostUsd);
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
}
