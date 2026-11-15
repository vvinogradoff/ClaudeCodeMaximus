using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionOptionsService" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionOptionsService : ISessionOptionsService
{
	private static readonly ILogger _log = Log.ForContext<SessionOptionsService>();

	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _appSettings;
	private readonly ITurnEngine _engine;
	private readonly ITurnSettingsResolver _settings;
	private readonly IClaudeStreamingSessionManager _streaming;

	public SessionOptionsService(
		ISessionRegistry registry,
		IAppSettingsService appSettings,
		ITurnEngine engine,
		ITurnSettingsResolver settings,
		IClaudeStreamingSessionManager streaming)
	{
		_registry    = registry;
		_appSettings = appSettings;
		_engine      = engine;
		_settings    = settings;
		_streaming   = streaming;
	}

	/// <inheritdoc />
	public async Task<SessionSetOptionsResult> SetAsync(string nodeId, SessionOptionsPatch patch)
	{
		var node = RemoteSessionLookup.Require(_registry, nodeId);

		// The settings are resolved before anything is stored: an unresolvable profile then fails the call and leaves the options as they were.
		var running = IsRunning(nodeId);
		TurnSettings? turnSettings = null;
		if (running && !string.IsNullOrEmpty(node.ClaudeSessionId) && (patch.ChangeAutoCommit || patch.AutoDocument.HasValue))
			turnSettings = await _settings.ResolveAsync(node, null);

		var corrections = new List<(string Toggle, string Label, string Prompt)>();
		bool changed;
		SessionOptionsDto options;
		lock (_appSettings.SyncRoot)
		{
			changed = false;

			if (patch.ChangeAutoCommit && node.IsAutoCommit != patch.AutoCommit)
			{
				node.IsAutoCommit = patch.AutoCommit;
				changed = true;
				var (label, prompt) = patch.AutoCommit switch
				{
					true  => ("enabled", Constants.Instructions.MidRunAutoCommitOn),
					false => ("disabled", Constants.Instructions.MidRunAutoCommitOff),
					null  => ("set to no preference", Constants.Instructions.MidRunAutoCommitSilent),
				};
				corrections.Add(("AutoCommit", label, prompt));
			}

			if (patch.AutoDocument is { } autoDocument && node.IsAutoDocument != autoDocument)
			{
				node.IsAutoDocument = autoDocument;
				changed = true;
				corrections.Add(("AutoDocument", autoDocument ? "enabled" : "disabled",
					autoDocument ? Constants.Instructions.MidRunAutoDocumentOn : Constants.Instructions.MidRunAutoDocumentOff));
			}

			if (patch.PlayCompletionSound is { } sound && node.PlayCompletionSound != sound)
			{
				node.PlayCompletionSound = sound;
				changed = true;
			}

			if (changed)
				_appSettings.Save();

			options = new SessionOptionsDto(node.IsAutoCommit, node.IsAutoDocument, node.PlayCompletionSound);
		}

		var notices = new List<string>();
		if (turnSettings != null)
		{
			foreach (var (toggle, label, prompt) in corrections)
			{
				// The engine spawns the one-shot --resume process (also in Streaming Session Mode, FR.19.11) and keeps only its session id.
				_log.Information("Mid-run {Toggle} change on {NodeId}: {State}, sending the correction prompt", toggle, nodeId, label);
				_engine.SendCorrection(node, turnSettings, prompt);
				notices.Add($"[{toggle} was {label} for this run]");
			}
		}

		if (changed)
			_registry.RaiseSessionSettingsChanged(nodeId);

		return new SessionSetOptionsResult(options, notices.Count == 0 ? null : notices);
	}

	/// <inheritdoc />
	public SessionResetConversationResult ResetConversation(string nodeId)
	{
		var node = RemoteSessionLookup.Require(_registry, nodeId);
		if (IsRunning(nodeId))
			throw new TdcpException(Constants.TreeMutation.TurnRunning);

		bool cleared;
		lock (_appSettings.SyncRoot)
		{
			var current = node.ClaudeSessionId;
			cleared = !string.IsNullOrEmpty(current);
			if (cleared)
			{
				// The JSONL stays viewable (FR.11.8): the id moves to the prior ids, the next prompt feeds the text session as context.
				node.PriorClaudeSessionIds.Add(current!);
				node.ClaudeSessionId = null;
				_appSettings.Save();
			}
		}

		if (!cleared)
			return new SessionResetConversationResult(false);

		// A live streaming process still holds the old conversation; without stopping it the detach would not take effect.
		if (_streaming.IsAlive(nodeId))
			_streaming.Stop(nodeId);

		_log.Information("Detached the Claude session of {NodeId}", nodeId);
		_registry.RaiseSessionSettingsChanged(nodeId);
		return new SessionResetConversationResult(true);
	}

	private bool IsRunning(string nodeId) => _engine.IsBusy(nodeId) || _registry.IsSessionRunning(nodeId);
}
