using System;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;

namespace TessynDesktop.Services;

/// <summary>
/// Turns the parsed claude stream events of one turn into neutral <see cref="IEngineEventSink"/> calls (FR.33.1). All Claude-specific matching
/// lives here: the "No conversation found" and OAuth-race markers (which become retries, not errors), the invalid-credentials marker, the
/// limits detection, the session id of the result and its storage on the node. The rules per <see cref="EngineTurnMode"/> are the former
/// <c>TurnRun.HandleStreamEvent</c> (interactive) and <c>SessionTurnService.HandleEvent</c> (headless), moved unchanged.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ClaudeEventTranslator
{
	private static readonly ILogger _log = Log.ForContext<ClaudeEventTranslator>();

	private readonly SessionNodeModel _node;
	private readonly IAppSettingsService _appSettings;
	private readonly IEngineEventSink _sink;
	private readonly EngineTurnMode _mode;
	private readonly bool _ownsConversation;
	private readonly ClaudeTurnState _state;

	/// <param name="ownsConversation">
	/// The translator stores the captured or lost session id on the node. False for an observer that sees the events next to the sink that owns
	/// them (a headless turn in a live streaming process, FR.19.13).
	/// </param>
	public ClaudeEventTranslator(SessionNodeModel node, IAppSettingsService appSettings, IEngineEventSink sink, EngineTurnMode mode,
		bool ownsConversation, ClaudeTurnState state)
	{
		_node             = node;
		_appSettings      = appSettings;
		_sink             = sink;
		_mode             = mode;
		_ownsConversation = ownsConversation;
		_state            = state;
	}

	/// <summary>Handles one parsed event of the claude stream (any thread).</summary>
	public void Handle(ClaudeStreamEvent evt)
	{
		var interactive = _mode == EngineTurnMode.Interactive;

		switch (evt.Type)
		{
			case Constants.Turns.EventTypes.Assistant when !string.IsNullOrWhiteSpace(evt.Content):
				_sink.AssistantText(new EngineAssistantText(evt.Content, evt.Timestamp, LimitErrorDetector.IsStructuredLimit(evt)));
				break;

			case Constants.Turns.EventTypes.System when evt.Subtype is Constants.Turns.EventSubtypes.Compact:
				_sink.Compacted(evt.Timestamp);
				break;

			case Constants.Turns.EventTypes.System when evt.Subtype is Constants.Turns.EventSubtypes.TaskProgress or Constants.Turns.EventSubtypes.TaskStarted
			                                            && !string.IsNullOrWhiteSpace(evt.Content):
				_sink.Progress(evt.Content, evt.Timestamp);
				break;

			case Constants.Turns.EventTypes.System when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				if (interactive)
					HandleInteractiveSystemError(evt);
				else
					HandleHeadlessSystemError(evt);
				break;

			case Constants.Turns.EventTypes.Result when interactive && evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase):
				// "No conversation found" - transient infrastructure error. Flag the auto-retry with the context preamble;
				// nothing is written to the transcript.
				_log.Information("No conversation found for session {FileName} - will retry with context", _node.FileName);
				LoseConversation();
				break;

			case Constants.Turns.EventTypes.Result when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase):
				// ISSUE.010 - transient cross-process OAuth refresh-token race, not a real auth failure. Flag a single silent
				// retry; nothing is written so it never reads as a real failure.
				if (interactive)
					_log.Information("OAuth refresh race for session {FileName} - will retry once", _node.FileName);
				FlagAuthRaceRetry();
				break;

			case Constants.Turns.EventTypes.Result when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				if (interactive)
					_log.Warning("Claude result error: {Error}", evt.Content);
				_sink.TurnEnded(new EngineTurnEnd(false, evt.Content, IsAuthError(evt.Content), LimitErrorDetector.IsLimit(evt), null,
					evt.InputTokens, evt.OutputTokens, evt.CostUsd, evt.Timestamp));
				break;

			case Constants.Turns.EventTypes.Result when !evt.IsError && evt.SessionId is not null:
				if (_ownsConversation)
				{
					_node.ClaudeSessionId = evt.SessionId;
					_appSettings.Save();
				}
				_sink.TurnEnded(new EngineTurnEnd(true, null, false, false, evt.SessionId, evt.InputTokens, evt.OutputTokens, evt.CostUsd, evt.Timestamp));
				break;

			case Constants.Turns.EventTypes.Result:
				_sink.TurnEnded(new EngineTurnEnd(!evt.IsError, null, false, false, null, evt.InputTokens, evt.OutputTokens, evt.CostUsd, evt.Timestamp));
				break;

			case Constants.Turns.EventTypes.Thinking when !string.IsNullOrWhiteSpace(evt.Content):
				_sink.Thinking(evt.Content);
				break;

			case Constants.Turns.EventTypes.ToolUse when !string.IsNullOrEmpty(evt.ToolUseId):
				_sink.ToolStarted(evt.ToolUseId, evt.ToolName, evt.ToolSummary);
				break;

			case Constants.Turns.EventTypes.ToolResult when !string.IsNullOrEmpty(evt.ToolUseId):
				_sink.ToolFinished(evt.ToolUseId, evt.ToolSummary, evt.ToolIsError);
				break;
		}
	}

	private void HandleInteractiveSystemError(ClaudeStreamEvent evt)
	{
		var content = evt.Content!;

		// When a context retry or an auth-race retry is pending, stderr-based errors are suppressed: they echo the same
		// error the result event already handled.
		if (_state.NeedsContextRetry || _state.NeedsAuthRaceRetry)
			return;

		if (content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase))
		{
			_log.Information("No conversation found (system error) for session {FileName} - will retry with context", _node.FileName);
			LoseConversation();
			return;
		}

		if (content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase))
		{
			_log.Information("OAuth refresh race (system error) for session {FileName} - will retry once", _node.FileName);
			FlagAuthRaceRetry();
			return;
		}

		_sink.Error(new EngineError(content, IsAuthError(content), LimitErrorDetector.IsLimit(evt), evt.Timestamp));
	}

	/// <summary>
	/// Suppress "No conversation found" (a headless turn checks resumability before it starts) and the ISSUE.010 OAuth refresh-race marker
	/// (the result event flags the retry): both are transient infrastructure noise, not user-facing failures.
	/// </summary>
	private void HandleHeadlessSystemError(ClaudeStreamEvent evt)
	{
		var content = evt.Content!;
		if (content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase)
		    || content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase))
			return;

		_sink.Error(new EngineError(content, IsAuthError(content), LimitErrorDetector.IsLimit(evt), evt.Timestamp));
	}

	private void LoseConversation()
	{
		if (_ownsConversation)
		{
			_node.ClaudeSessionId = null;
			_appSettings.Save();
		}

		_state.NeedsContextRetry = true;
		_sink.RetryPending(EngineRetryKind.LostConversation);
	}

	private void FlagAuthRaceRetry()
	{
		_state.NeedsAuthRaceRetry = true;
		_sink.RetryPending(EngineRetryKind.TransientAuth);
	}

	private static bool IsAuthError(string content) =>
		content.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase);
}
