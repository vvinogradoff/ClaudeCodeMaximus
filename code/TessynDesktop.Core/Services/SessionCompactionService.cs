using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionCompactionService" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionCompactionService : ISessionCompactionService
{
	private static readonly ILogger _log = Log.ForContext<SessionCompactionService>();

	private readonly IClaudeProcessManager _processManager;
	private readonly ISessionFileService   _fileService;
	private readonly IAppSettingsService   _appSettings;
	private readonly ISessionRegistry      _registry;
	private readonly ISessionTurnService   _turnService;

	public SessionCompactionService(
		IClaudeProcessManager processManager,
		ISessionFileService fileService,
		IAppSettingsService appSettings,
		ISessionRegistry registry,
		ISessionTurnService turnService)
	{
		_processManager = processManager;
		_fileService    = fileService;
		_appSettings    = appSettings;
		_registry       = registry;
		_turnService    = turnService;
	}

	/// <inheritdoc />
	public async Task<SessionCompactionResultModel> CompactAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default)
	{
		var turnLock = _turnService.GetTurnLock(node.NodeId);
		await turnLock.WaitAsync(cancellationToken);
		try
		{
			return await RunWithRunningFlagAsync(node, options, cancellationToken);
		}
		finally
		{
			turnLock.Release();
		}
	}

	/// <inheritdoc />
	public Task<SessionCompactionResultModel> CompactWhileHoldingTurnLockAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default) =>
		RunWithRunningFlagAsync(node, options, cancellationToken);

	private async Task<SessionCompactionResultModel> RunWithRunningFlagAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken)
	{
		// Reference-counted by the registry, so it nests with the running flag a surrounding turn already holds.
		_registry.SetSessionRunning(node.NodeId, true);
		try
		{
			return await RunAsync(node, options, cancellationToken);
		}
		finally
		{
			_registry.SetSessionRunning(node.NodeId, false);
		}
	}

	private async Task<SessionCompactionResultModel> RunAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken)
	{
		_log.Information("Starting auto-compaction for session {FileName}", node.FileName);

		// The prompt embeds the whole transcript from the always-complete text Session File, so the call is stateless
		// (sessionId: null) and needs no cross-profile lookup (FR.11.6).
		var prompt = CompactionPromptBuilder.BuildPrompt(_fileService.ReadEntries(node.FileName), node.WorkingDirectory);
		if (prompt == null)
		{
			_log.Information("Nothing to compact for session {FileName}", node.FileName);
			return new SessionCompactionResultModel(SessionCompactionOutcome.NothingToCompact, null, Constants.Compaction.NothingToCompactMessage, false);
		}

		var compactedContent = new StringBuilder();
		var hadError = false;
		string? errorContent = null;

		try
		{
			// Local Tool Profile (FR.12.21): restrict native tools the way an ordinary turn would. No --mcp-config is built for compaction.
			var directoryModel = _appSettings.Settings.Tree.FirstOrDefault(d =>
				string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
			var localProfile = LocalToolProfileResolver.Resolve(options.ModelInfo, directoryModel);
			var ollamaBaseUrl = options.ModelInfo?.Provider == ModelProvider.Ollama ? _appSettings.Settings.OllamaBaseUrl : null;
			var disableTools = options.ModelInfo is { SupportsTools: false };

			await _processManager.SendMessageAsync(
				workingDirectory: node.WorkingDirectory,
				claudePath:       _appSettings.Settings.ClaudePath,
				sessionId:        null,
				userMessage:      prompt,
				model:            options.ModelId,
				profileConfigDir: options.ProfileConfigDir,
				effort:           options.Effort,
				ollamaBaseUrl:    ollamaBaseUrl,
				disableTools:     disableTools,
				disallowNativeScheduling: _appSettings.Settings.AgentToolsEnabled,
				localToolSpec:    localProfile?.ToolSpec,
				strictMcpConfig:  localProfile != null,
				onEvent:          evt =>
				{
					// Any error event marks the whole run as failed — even if some assistant text was also
					// captured, it must not be treated as a valid compacted result.
					if (evt.IsError)
					{
						hadError = true;
						errorContent ??= evt.Content;
						return;
					}
					if (evt.Type == "assistant" && !string.IsNullOrWhiteSpace(evt.Content))
						compactedContent.AppendLine(evt.Content);
					// The compaction response's session id is intentionally NOT stored: the JSONL is detached afterwards (FR.11.8).
				},
				cancellationToken: cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Compaction call threw for session {FileName}; keeping original", node.FileName);
			hadError = true;
			errorContent ??= ex.Message;
		}

		var compacted = compactedContent.ToString().Trim();
		if (hadError || string.IsNullOrEmpty(compacted))
			return Unchanged(node, hadError, errorContent);

		// COMPACTION separator followed by the compacted conversation (Claude answers in the session file format).
		var fileContent = new StringBuilder();
		fileContent.AppendLine($"[{DateTimeOffset.UtcNow.ToString(Constants.SessionFile.TimestampFormat)}] {Constants.SessionFile.RoleCompaction}");
		fileContent.AppendLine(compacted);
		if (!compacted.EndsWith(Environment.NewLine))
			fileContent.AppendLine();

		try
		{
			// Atomic (temp file + move): a failure before the move leaves the original in place.
			_fileService.RewriteSessionFile(node.FileName, fileContent.ToString());
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Rewriting session file {FileName} after compaction failed; keeping original", node.FileName);
			return Unchanged(node, true, ex.Message);
		}

		var detached = Detach(node);
		_log.Information("Session {FileName} compacted and JSONL detached", node.FileName);
		return new SessionCompactionResultModel(SessionCompactionOutcome.Compacted, null, Constants.Compaction.CompactedMessage, detached);
	}

	private SessionCompactionResultModel Unchanged(SessionNodeModel node, bool hadError, string? error)
	{
		if (hadError)
		{
			_log.Warning("Compaction failed for session {FileName}; keeping original. Error: {Error}", node.FileName, error);
			return new SessionCompactionResultModel(SessionCompactionOutcome.Failed, error,
				string.Format(Constants.Compaction.FailedMessageFormat, error), false);
		}

		_log.Warning("Compaction returned empty content for session {FileName}; keeping original", node.FileName);
		return new SessionCompactionResultModel(SessionCompactionOutcome.Failed, null, Constants.Compaction.EmptyResultMessage, false);
	}

	/// <summary>Moves ClaudeSessionId to PriorClaudeSessionIds (FR.11.8) so the next prompt uses the compacted text as context. False when there was none.</summary>
	private bool Detach(SessionNodeModel node)
	{
		lock (_appSettings.SyncRoot)
		{
			var currentId = node.ClaudeSessionId;
			if (string.IsNullOrEmpty(currentId))
				return false;

			node.PriorClaudeSessionIds.Add(currentId);
			node.ClaudeSessionId = null;
			_appSettings.Save();
			_log.Information("Detached Claude session {SessionId} for {FileName}", currentId, node.FileName);
			return true;
		}
	}
}
