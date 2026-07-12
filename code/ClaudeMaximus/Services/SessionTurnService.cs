using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.ViewModels;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnService : ISessionTurnService
{
	private static readonly ILogger _log = Log.ForContext<SessionTurnService>();

	private readonly IClaudeProcessManager        _processManager;
	private readonly ISessionFileService          _fileService;
	private readonly IAppSettingsService          _appSettings;
	private readonly IClaudeProfileService        _profileService;
	private readonly Lazy<IAgentMcpServer>        _mcpServer;
	private readonly SessionTreeViewModel         _sessionTree;
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
		SessionTreeViewModel sessionTree,
		IClaudeSessionStatusService sessionStatus,
		IClaudeModelService modelService)
	{
		_processManager = processManager;
		_fileService    = fileService;
		_appSettings    = appSettings;
		_profileService = profileService;
		_mcpServer      = mcpServer;
		_sessionTree    = sessionTree;
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

		// Signal the tree node spinner on the UI thread.
		SessionNodeViewModel? nodeVm = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			nodeVm = _sessionTree.FindNodeVmByNodeId(node.NodeId);
			if (nodeVm != null)
				nodeVm.IsRunning = true;
		});

		try
		{
			var profileConfigDir = ResolveProfileConfigDir(node);
			var mcpConfigPath    = _appSettings.Settings.AgentToolsEnabled
				? await _mcpServer.Value.EnsureConfigFileAsync(node.NodeId, node.AgentToken)
				: null;

			// Build instruction block using persisted per-session toggle states.
			var options          = new InstructionOptionsModel(
				node.IsAutoCommit, false, node.IsAutoDocument,
				_appSettings.Settings.AgentToolsEnabled);
			var instructionBlock = InstructionBlockBuilder.Build(options);
			var augmentedPrompt  = prompt + instructionBlock;

			// Write clean prompt (without instruction block) to the session file.
			_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, prompt);

			// Validate that the claude session file still exists before attempting --resume.
			// If the session was wiped (compaction, cleanup, context window reset), fall through
			// to the context-preamble path which rebuilds context from the ClaudeMaximus JSONL file.
			if (node.ClaudeSessionId != null
			    && !_sessionStatus.IsSessionResumable(node.WorkingDirectory, node.ClaudeSessionId))
			{
				_log.Information("Session {SessionId} is no longer resumable — clearing to use context preamble", node.ClaudeSessionId);
				node.ClaudeSessionId = null;
				_appSettings.Save();
			}

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

			var resultText   = new StringBuilder();
			var isError      = false;
			string? errorMessage = null;
			int inputTokens  = 0, outputTokens = 0;
			double costUsd   = 0;

			await _processManager.SendMessageAsync(
				workingDirectory: node.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        sessionId,
				userMessage:      messageToSend,
				onEvent:          evt => HandleEvent(evt, node, resultText, ref isError, ref errorMessage,
				                                     ref inputTokens, ref outputTokens, ref costUsd),
				model:            effectiveModelId,
				profileConfigDir: profileConfigDir,
				mcpConfigPath:    mcpConfigPath,
				ollamaBaseUrl:    ollamaBaseUrl,
				disableTools:     disableTools,
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

			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				if (nodeVm != null)
					nodeVm.IsRunning = false;
			});
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
		ref double costUsd)
	{
		switch (evt.Type)
		{
			case "assistant" when !string.IsNullOrWhiteSpace(evt.Content):
				_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleAssistant, evt.Content);
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

	private string? ResolveProfileConfigDir(SessionNodeModel node)
	{
		var directoryModel = _appSettings.Settings.Tree.FirstOrDefault(d =>
			string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
		var profileIndex = directoryModel?.SelectedProfileIndex ?? 0;
		return _profileService.GetConfigDirForProfile(profileIndex, _appSettings.Settings.Profiles);
	}
}
