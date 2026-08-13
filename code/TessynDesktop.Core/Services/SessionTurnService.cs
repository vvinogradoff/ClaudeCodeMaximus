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

	private readonly IClaudeProcessManager        _processManager;
	private readonly ISessionFileService          _fileService;
	private readonly IAppSettingsService          _appSettings;
	private readonly IClaudeProfileService        _profileService;
	private readonly Lazy<IAgentMcpServer>        _mcpServer;
	private readonly ISessionRegistry             _registry;
	private readonly IClaudeSessionStatusService  _sessionStatus;
	private readonly IClaudeModelService          _modelService;

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
		IClaudeModelService modelService)
	{
		_processManager = processManager;
		_fileService    = fileService;
		_appSettings    = appSettings;
		_profileService = profileService;
		_mcpServer      = mcpServer;
		_registry       = registry;
		_sessionStatus  = sessionStatus;
		_modelService   = modelService;
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

		try
		{
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

			// MCP tools (e.g. the cmx orchestration server) are a separate mechanism from the
			// built-in --tools flag — the CLI still sends them to the model even when --tools ""
			// is set. Skip --mcp-config entirely for models that don't support tool calling.
			var mcpConfigPath = _appSettings.Settings.AgentToolsEnabled && !disableTools
				? await _mcpServer.Value.EnsureConfigFileAsync(node.NodeId, node.AgentToken)
				: null;

			// Write clean prompt (without instruction block) to the session file.
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, prompt);

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
			// IsStreamingMode is always false here — headless/scheduled/orchestrated turns always run
			// via the one-shot --resume path regardless of the target session's own streaming toggle
			// (FR.19.11), so the instruction block should describe this spawn's actual (one-shot) mode.
			var options          = new InstructionOptionsModel(
				node.IsAutoCommit, false, node.IsAutoDocument,
				_appSettings.Settings.AgentToolsEnabled, node.ClaudeSessionId == null, IsStreamingMode: false);
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

			var resultText        = new StringBuilder();
			var isError           = false;
			string? errorMessage  = null;
			int inputTokens       = 0, outputTokens = 0;
			double costUsd        = 0;
			var firstAssistant    = true;
			var pendingModelId    = effectiveModelId ?? "default";

			await _processManager.SendMessageAsync(
				workingDirectory: node.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        sessionId,
				userMessage:      messageToSend,
				onEvent:          evt => HandleEvent(evt, node, resultText, ref isError, ref errorMessage,
				                                     ref inputTokens, ref outputTokens, ref costUsd,
				                                     ref firstAssistant, pendingModelId),
				model:            effectiveModelId,
				profileConfigDir: profileConfigDir,
				mcpConfigPath:    mcpConfigPath,
				ollamaBaseUrl:    ollamaBaseUrl,
				disableTools:     disableTools,
				disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
				cancellationToken: ct);

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
				// Suppress the "No conversation found" message — context reload would be needed
				// but is not yet implemented for headless turns (backlog item).
				if (!evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker,
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
