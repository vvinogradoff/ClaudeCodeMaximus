using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// The Claude Code CLI behind the session-engine seam (FR.33.1): today's one-shot (<see cref="IClaudeProcessManager"/>) and Streaming Session
/// Mode (<see cref="IClaudeStreamingSessionManager"/>, FR.19) paths with their Claude-specific retries (the OAuth refresh race ISSUE.010, the
/// "No conversation found" context retry FR.11.10) and the cross-profile adoption (FR.11.13). Moved from <c>TurnEngine</c> and
/// <c>SessionTurnService</c> with the same arguments, order, delays and log texts; the stream events become neutral calls through
/// <see cref="ClaudeEventTranslator"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionEngine : ISessionEngine
{
	private static readonly ILogger _log = Log.ForContext<ClaudeSessionEngine>();

	private static readonly EngineCapabilities ClaudeCapabilities = new(
		PersistentProcess: true,
		OneShotProcess:    true,
		InBandInterrupt:   false,
		ReportsCost:       true,
		NativeCompaction:  false,
		Chrome:            true,
		LocalModels:       true,
		MidTurnCorrection: true);

	private readonly IClaudeProcessManager _processManager;
	private readonly IClaudeStreamingSessionManager _streamingManager;
	private readonly ISessionFileService _files;
	private readonly IAppSettingsService _appSettings;
	private readonly IClaudeSessionStatusService _sessionStatus;
	private readonly Lazy<IAgentMcpServer> _mcpServer;

	public ClaudeSessionEngine(
		IClaudeProcessManager processManager,
		IClaudeStreamingSessionManager streamingManager,
		ISessionFileService files,
		IAppSettingsService appSettings,
		IClaudeSessionStatusService sessionStatus,
		Lazy<IAgentMcpServer> mcpServer)
	{
		_processManager   = processManager;
		_streamingManager = streamingManager;
		_files            = files;
		_appSettings      = appSettings;
		_sessionStatus    = sessionStatus;
		_mcpServer        = mcpServer;
	}

	/// <inheritdoc />
	public string EngineId => Constants.SessionEngines.Claude;

	/// <inheritdoc />
	public string DisplayName => Constants.SessionEngines.ClaudeDisplayName;

	/// <inheritdoc />
	public EngineCapabilities Capabilities => ClaudeCapabilities;

	/// <inheritdoc />
	public string? GetConversationId(SessionNodeModel node) => node.ClaudeSessionId;

	/// <inheritdoc />
	public void ClearConversation(SessionNodeModel node)
	{
		node.ClaudeSessionId = null;
		_appSettings.Save();
	}

	/// <inheritdoc />
	public bool DetachConversation(SessionNodeModel node)
	{
		lock (_appSettings.SyncRoot)
		{
			var current = node.ClaudeSessionId;
			if (string.IsNullOrEmpty(current))
				return false;

			node.PriorClaudeSessionIds.Add(current);
			node.ClaudeSessionId = null;
			_appSettings.Save();
			return true;
		}
	}

	/// <inheritdoc />
	public bool AdoptConversation(SessionNodeModel node, string conversationId, string? profileConfigDir) =>
		_sessionStatus.TryAdoptSessionFromOtherProfile(node.WorkingDirectory, conversationId, profileConfigDir);

	/// <summary>
	/// The MCP config and the Local Tool Profile of the turn (FR.12.21, FR.14.3, FR.14.11): a tool-capable local Ollama model gets the
	/// directory's curated <c>--tools</c> list plus a filtered, strict <c>--mcp-config</c>; MCP tools are a separate mechanism from the
	/// built-in <c>--tools</c> flag, so <c>--mcp-config</c> is skipped entirely for models that cannot call tools.
	/// </summary>
	public async Task<EngineTurnPreparation?> PrepareTurnAsync(SessionNodeModel node, TurnSettings settings)
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

		return new ClaudeTurnPreparation(mcpConfigPath, localToolSpec, strictMcpConfig);
	}

	/// <inheritdoc />
	public Task RunTurnAsync(EngineTurnRequest request, IEngineEventSink sink, CancellationToken cancellationToken)
	{
		var preparation = request.Preparation as ClaudeTurnPreparation ?? new ClaudeTurnPreparation(null, null, false);

		if (request.Mode == EngineTurnMode.Headless)
			return SendHeadlessOneShotAsync(request, preparation, sink, cancellationToken);

		// Streaming Session Mode (FR.19): no --resume "No conversation found" retry path - the same open process
		// carries the conversation, so context-retry doesn't apply.
		return request.Persistent
			? SendViaStreamingAsync(request, preparation, sink, cancellationToken)
			: SendOneShotAsync(request, preparation, sink, cancellationToken);
	}

	/// <summary>
	/// Routes a headless turn (scheduler/orchestration/Telegram) into an already-alive Streaming Session Mode process instead of a competing
	/// one-shot --resume process (FR.19.13). The process's primary event callback (bound by the interactive turn that started it) writes the
	/// transcript and stores the session id; this only observes the same events, additively, for the headless caller's result.
	/// </summary>
	public async Task RunTurnInLiveProcessAsync(SessionNodeModel node, string prompt, IEngineEventSink sink, CancellationToken cancellationToken)
	{
		var state = new ClaudeTurnState();
		var translator = new ClaudeEventTranslator(node, _appSettings, sink, EngineTurnMode.Headless, ownsConversation: false, state);

		await _streamingManager.SendTurnAsync(node.NodeId, prompt, cancellationToken, translator.Handle);

		if (state.NeedsAuthRaceRetry)
		{
			state.NeedsAuthRaceRetry = false;
			sink.Retrying(EngineRetryKind.TransientAuth);
			_log.Information("OAuth refresh race for node {NodeId} (streaming) — retrying turn once", node.NodeId);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, cancellationToken);
			sink.RetryRunning();
			await _streamingManager.SendTurnAsync(node.NodeId, prompt, cancellationToken, translator.Handle);
		}
	}

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

	/// <summary>Claude has no in-band interrupt: a one-shot turn is stopped through its cancellation token, a persistent process is killed.</summary>
	public Task<bool> InterruptAsync(string nodeId)
	{
		var alive = _streamingManager.IsAlive(nodeId);
		if (alive)
			_streamingManager.Stop(nodeId);

		return Task.FromResult(alive);
	}

	/// <inheritdoc />
	public bool IsAlive(string nodeId) => _streamingManager.IsAlive(nodeId);

	/// <inheritdoc />
	public void Stop(string nodeId) => _streamingManager.Stop(nodeId);

	/// <inheritdoc />
	public void TerminateAll()
	{
		_processManager.TerminateAll();
		_streamingManager.TerminateAll();
	}

	/// <summary>One-shot Mode of an interactive turn: one process per turn, then the single OAuth-race retry and the single context retry.</summary>
	private async Task SendOneShotAsync(EngineTurnRequest request, ClaudeTurnPreparation preparation, IEngineEventSink sink, CancellationToken ct)
	{
		var node       = request.Node;
		var state      = new ClaudeTurnState();
		sink.EngineTurnState = state;
		var translator = new ClaudeEventTranslator(node, _appSettings, sink, EngineTurnMode.Interactive, ownsConversation: true, state);

		await LaunchOneShotAsync(translator, node, request.Settings, request.ConversationId, request.Prompt, preparation, chrome: request.Settings.Chrome, ct);

		if (state.NeedsAuthRaceRetry)
		{
			state.NeedsAuthRaceRetry = false;
			_log.Information("Retrying turn after transient OAuth refresh race for session {FileName}", node.FileName);
			sink.Retrying(EngineRetryKind.TransientAuth);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			sink.RetryRunning();

			await LaunchOneShotAsync(translator, node, request.Settings, request.ConversationId, request.Prompt, preparation, chrome: request.Settings.Chrome, ct);
		}

		if (state.NeedsContextRetry)
		{
			state.NeedsContextRetry = false;
			var enrichedMessage = InstructionBlockBuilder.BuildContextPreamble(_files.ReadEntries(node.FileName), request.AugmentedPrompt);

			sink.Retrying(EngineRetryKind.LostConversation);
			sink.RetryRunning();

			await LaunchOneShotAsync(translator, node, request.Settings, null, enrichedMessage, preparation, chrome: request.Settings.Chrome, ct);
		}
	}

	/// <summary>A headless turn's one-shot --resume process (FR.19.11), with the single silent OAuth-race retry. No context retry: resumability was checked before.</summary>
	private async Task SendHeadlessOneShotAsync(EngineTurnRequest request, ClaudeTurnPreparation preparation, IEngineEventSink sink, CancellationToken ct)
	{
		var node       = request.Node;
		var state      = new ClaudeTurnState();
		var translator = new ClaudeEventTranslator(node, _appSettings, sink, EngineTurnMode.Headless, ownsConversation: true, state);

		await LaunchOneShotAsync(translator, node, request.Settings, request.ConversationId, request.Prompt, preparation, chrome: false, ct);

		if (state.NeedsAuthRaceRetry)
		{
			// ISSUE.010 — transient cross-process OAuth refresh-token race, not a real auth
			// failure (see Constants.Auth.OAuthRefreshRaceMarker). Retry once, silently.
			state.NeedsAuthRaceRetry = false;
			sink.Retrying(EngineRetryKind.TransientAuth);
			_log.Information("OAuth refresh race for node {NodeId} — retrying turn once", node.NodeId);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			sink.RetryRunning();

			await LaunchOneShotAsync(translator, node, request.Settings, request.ConversationId, request.Prompt, preparation, chrome: false, ct);
		}
	}

	private Task LaunchOneShotAsync(ClaudeEventTranslator translator, SessionNodeModel node, TurnSettings settings, string? sessionId, string message,
		ClaudeTurnPreparation preparation, bool chrome, CancellationToken ct) =>
		_processManager.SendMessageAsync(
			workingDirectory: node.WorkingDirectory,
			claudePath:       _appSettings.Settings.ClaudePath,
			sessionId:        sessionId,
			userMessage:      message,
			onEvent:          translator.Handle,
			model:            settings.ModelId,
			profileConfigDir: settings.ProfileConfigDir,
			effort:           settings.Effort,
			mcpConfigPath:    preparation.McpConfigPath,
			ollamaBaseUrl:    settings.OllamaBaseUrl,
			disableTools:     settings.DisableTools,
			chrome:           chrome,
			disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
			localToolSpec:    preparation.LocalToolSpec,
			strictMcpConfig:  preparation.StrictMcpConfig,
			cancellationToken: ct);

	/// <summary>
	/// Sends one turn via Streaming Session Mode (FR.19): ensures the session's persistent process is running (spawning it with the
	/// turn's model/profile/effort/mcp config if this is the first turn), then writes the turn to its open stdin and awaits the next
	/// result event. The persistent sink is bound as the process's primary callback; it reports this turn's events too.
	/// </summary>
	private async Task SendViaStreamingAsync(EngineTurnRequest request, ClaudeTurnPreparation preparation, IEngineEventSink sink, CancellationToken ct)
	{
		var node     = request.Node;
		var nodeId   = node.NodeId;
		var settings = request.Settings;
		var processSink = request.PersistentSink ?? sink;

		var spawnParams = new StreamingSpawnParams(
			WorkingDirectory: node.WorkingDirectory,
			ClaudePath:       _appSettings.Settings.ClaudePath,
			SessionId:        request.ConversationId,
			ProfileConfigDir: settings.ProfileConfigDir,
			Model:            settings.ModelId,
			Effort:           settings.Effort,
			McpConfigPath:    preparation.McpConfigPath,
			OllamaBaseUrl:    settings.OllamaBaseUrl,
			DisableTools:     settings.DisableTools,
			Chrome:           settings.Chrome,
			LocalToolSpec:    preparation.LocalToolSpec,
			StrictMcpConfig:  preparation.StrictMcpConfig);

		// The turn's retry state is kept by its sink (the run holding the turn lock), so it lives until the turn really ends - also through
		// what the caller does after the reply while it still holds the lock (Auto-Compact) - exactly as the flags on the former TurnRun did.
		var state = new ClaudeTurnState();
		sink.EngineTurnState = state;

		var justStarted = _streamingManager.EnsureStarted(nodeId, spawnParams, PersistentHandlerFor(node, processSink));
		if (justStarted)
			sink.PersistentProcessStarted();

		await _streamingManager.SendTurnAsync(nodeId, request.Prompt, ct);

		if (state.NeedsAuthRaceRetry)
		{
			// ISSUE.010 - same transient cross-process OAuth refresh race as the one-shot path; the streaming process is still
			// alive, so retrying just writes the turn again.
			state.NeedsAuthRaceRetry = false;
			_log.Information("Retrying streaming turn after transient OAuth refresh race for session {FileName}", node.FileName);
			sink.Retrying(EngineRetryKind.TransientAuth);
			await Task.Delay(Constants.Auth.OAuthRefreshRaceRetryDelayMs, ct);
			sink.RetryRunning();
			await _streamingManager.SendTurnAsync(nodeId, request.Prompt, ct);
		}
	}

	/// <summary>
	/// The callback bound to a node's streaming process (bound again on every turn). It translates each event with the retry state the process
	/// sink hands out: that of the turn now holding the lock, or fresh state outside one (background-task output, native schedules, FR.19.4),
	/// as the former per-event background TurnRun had.
	/// </summary>
	private Action<ClaudeStreamEvent> PersistentHandlerFor(SessionNodeModel node, IEngineEventSink processSink) =>
		evt =>
		{
			var state = processSink.EngineTurnState as ClaudeTurnState ?? new ClaudeTurnState();
			new ClaudeEventTranslator(node, _appSettings, processSink, EngineTurnMode.Interactive, ownsConversation: true, state).Handle(evt);
		};
}
