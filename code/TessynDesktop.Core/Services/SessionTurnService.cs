using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
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
		IClaudeStreamingSessionManager streamingManager)
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
	}

	public SemaphoreSlim GetTurnLock(string nodeId) =>
		_turnLocks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));

	public bool CancelTurn(string nodeId)
	{
		if (!_activeCts.TryGetValue(nodeId, out var cts))
			return false;
		cts.Cancel();
		return true;
	}

	public async Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default)
	{
		var turnLock = GetTurnLock(node.NodeId);
		await turnLock.WaitAsync(cancellationToken);

		// Register a per-node CTS so CancelTurn can interrupt this turn.
		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_activeCts[node.NodeId] = linkedCts;
		var ct = linkedCts.Token;

		// Signal running state via the registry (SessionTreeViewModel observes TreeChanged).
		_registry.SetSessionRunning(node.NodeId, true);

		// FR.21.2 — lazily ensure a Telegram topic exists for this session on its first activity
		// since the bridge was enabled. Fire-and-forget: EnsureTopicForSessionAsync never throws
		// and must never delay or fail the turn it was piggybacked on.
		_ = _telegramBridge.Value.EnsureTopicForSessionAsync(node);

		try
		{
			// Write clean prompt (without instruction block) to the session file. Common to both
			// branches below — the streaming branch needs it recorded here too, since (unlike the
			// interactive desktop path) nothing else writes the user's own message for a headless turn.
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, prompt);

			// FR.19.13 — if a Streaming Session Mode process is already alive for this node (started
			// by the interactive desktop path), route this headless turn into it instead of spawning
			// a competing one-shot --resume process against the same ClaudeSessionId. Two independent
			// processes touching the same session caused a confirmed bug: the one-shot process reads
			// and extends the on-disk JSONL, but the already-running persistent process has no way to
			// notice — its own in-memory context silently falls behind, so its next answer looks like
			// it "forgot" the headless-sourced exchange. See docs/issues/Streaming-Headless-Context-Divergence.md.
			if (_streamingManager.IsAlive(node.NodeId))
				return await RunTurnViaStreamingAsync(node, prompt, ct);

			var profileConfigDir = ResolveProfileConfigDir(node);

			// Resolve effective model for this session (FR.16.5 bug fix):
			// node.ModelId → per-directory setting → app-level setting.
			var effectiveModelId = ResolveEffectiveModel(node);
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
			var options          = new InstructionOptionsModel(
				node.IsAutoCommit, false, node.IsAutoDocument,
				_appSettings.Settings.AgentToolsEnabled, node.ClaudeSessionId == null, IsStreamingMode: false,
				IsTelegramEnabled: _appSettings.Settings.TelegramEnabled);
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
			var pendingModelId      = effectiveModelId ?? "default";
			var needsAuthRaceRetry  = false;

			await _processManager.SendMessageAsync(
				workingDirectory: node.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        sessionId,
				userMessage:      messageToSend,
				onEvent:          evt => HandleEvent(evt, node, resultText, ref isError, ref errorMessage,
				                                     ref inputTokens, ref outputTokens, ref costUsd,
				                                     ref firstAssistant, ref needsAuthRaceRetry, pendingModelId),
				model:            effectiveModelId,
				profileConfigDir: profileConfigDir,
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
				_log.Information("OAuth refresh race for node {NodeId} — retrying turn once", node.NodeId);
				await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);

				await _processManager.SendMessageAsync(
					workingDirectory: node.WorkingDirectory,
					claudePath:       _appSettings.Settings.ClaudePath,
					sessionId:        sessionId,
					userMessage:      messageToSend,
					onEvent:          evt => HandleEvent(evt, node, resultText, ref isError, ref errorMessage,
					                                     ref inputTokens, ref outputTokens, ref costUsd,
					                                     ref firstAssistant, ref needsAuthRaceRetry, pendingModelId),
					model:            effectiveModelId,
					profileConfigDir: profileConfigDir,
					mcpConfigPath:    mcpConfigPath,
					ollamaBaseUrl:    ollamaBaseUrl,
					disableTools:     disableTools,
					disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
					localToolSpec:    localToolSpec,
					strictMcpConfig:  strictMcpConfig,
					cancellationToken: ct);
			}

			return new TurnResultModel(resultText.ToString(), node.ClaudeSessionId, isError, errorMessage,
			                           inputTokens, outputTokens, costUsd);
		}
		catch (OperationCanceledException)
		{
			_log.Information("Turn cancelled for node {NodeId}", node.NodeId);
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, "[Turn cancelled]");
			return new TurnResultModel(string.Empty, node.ClaudeSessionId, IsError: true, ErrorMessage: "Cancelled");
		}
		finally
		{
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
	private async Task<TurnResultModel> RunTurnViaStreamingAsync(SessionNodeModel node, string prompt, CancellationToken ct)
	{
		var options = new InstructionOptionsModel(
			node.IsAutoCommit, false, node.IsAutoDocument,
			_appSettings.Settings.AgentToolsEnabled, node.ClaudeSessionId == null, IsStreamingMode: true,
			IsTelegramEnabled: _appSettings.Settings.TelegramEnabled);
		var augmentedPrompt = prompt + InstructionBlockBuilder.Build(options);

		var resultText         = new StringBuilder();
		var isError            = false;
		string? errorMessage   = null;
		int inputTokens = 0, outputTokens = 0;
		double costUsd  = 0;
		var needsAuthRaceRetry = false;

		void ObserveEvent(ClaudeStreamEvent evt)
		{
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
			_log.Information("OAuth refresh race for node {NodeId} (streaming) — retrying turn once", node.NodeId);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
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
		string pendingModelId)
	{
		switch (evt.Type)
		{
			case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
				string? fileModelId = null, fileEffort = null;
				if (firstAssistant)
				{
					firstAssistant = false;
					fileModelId    = pendingModelId;
					fileEffort     = "default";
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
	/// Resolves the profile config dir to use for this turn, mapping the resolved profile name
	/// (see <see cref="ResolveProfileName"/>) to its credential store. Scheduled/orchestrated turns
	/// thereby use the same account as interactive turns on the same session, and spawned children
	/// use their supervisor's account rather than a possibly-revoked Default.
	/// </summary>
	private string? ResolveProfileConfigDir(SessionNodeModel node)
	{
		var name = ResolveProfileName(node);
		if (string.IsNullOrEmpty(name))
			return null; // No profile recorded → Default

		var profiles = _appSettings.Settings.Profiles;
		for (var i = 0; i < profiles.Count; i++)
		{
			if (string.Equals(profiles[i].DisplayName, name, StringComparison.OrdinalIgnoreCase))
				return _profileService.GetConfigDirForProfile(i + 1, profiles);
		}

		return null; // Profile was deleted or renamed → fall back to Default
	}
}
