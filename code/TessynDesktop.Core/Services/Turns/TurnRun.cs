using System;
using System.Threading;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The state and the stream-event handling of one engine turn: which events become transcript entries, which are suppressed
/// because the engine retries instead, and what the observer is told. Moved unchanged from <c>TurnController.HandleStreamEvent</c>,
/// with the per-controller fields now per turn.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnRun
{
	private static readonly ILogger _log = Log.ForContext<TurnRun>();

	private readonly SessionNodeModel _node;
	private readonly ISessionFileService _files;
	private readonly IAppSettingsService _appSettings;
	private readonly ITurnObserver _observer;
	private readonly TurnProgressTracker _progress;

	// Cross-thread (prepared on the caller's thread, consumed on the reader thread): use Interlocked.
	private string? _pendingModelId;
	private string? _pendingEffort;

	public TurnRun(string turnId, SessionNodeModel node, ISessionFileService files, IAppSettingsService appSettings, ITurnObserver observer,
		TurnProgressTracker progress, string? pendingModelId, string? pendingEffort)
	{
		TurnId          = turnId;
		_node           = node;
		_files          = files;
		_appSettings    = appSettings;
		_observer       = observer;
		_progress       = progress;
		_pendingModelId = pendingModelId;
		_pendingEffort  = pendingEffort;
	}

	public string TurnId { get; }

	/// <summary>Reports this turn's progress to the sinks.</summary>
	public TurnProgressTracker Progress => _progress;

	/// <summary>"No conversation found": the engine retries once with the history as context. Reset when the retry is launched.</summary>
	public bool NeedsContextRetry { get; set; }

	/// <summary>ISSUE.010: the transient OAuth refresh race; the engine retries once, silently. Reset when the retry is launched.</summary>
	public bool NeedsAuthRaceRetry { get; set; }

	/// <summary>FR.21.22: text of the error result of this turn, reported with <c>TurnCompleted</c>; reset on every retry.</summary>
	public string? SendErrorText { get; set; }

	/// <summary>Handles one parsed event of the claude stream (any thread).</summary>
	public void HandleStreamEvent(ClaudeStreamEvent evt)
	{
		switch (evt.Type)
		{
			case Constants.Turns.EventTypes.Assistant when !string.IsNullOrWhiteSpace(evt.Content):
				var fileModelId = Interlocked.Exchange(ref _pendingModelId, null);
				var fileEffort  = Interlocked.Exchange(ref _pendingEffort, null);
				_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleAssistant, evt.Content,
					modelId: fileModelId, effort: fileEffort);
				_observer.AssistantEntryAppended(evt.Content, evt.Timestamp, fileModelId, fileEffort);
				break;

			case Constants.Turns.EventTypes.System when evt.Subtype is Constants.Turns.EventSubtypes.Compact:
				_files.AppendCompactionSeparator(_node.FileName);
				_observer.CompactionAppended(evt.Timestamp);
				break;

			case Constants.Turns.EventTypes.System when evt.Subtype is Constants.Turns.EventSubtypes.TaskProgress or Constants.Turns.EventSubtypes.TaskStarted
			                                            && !string.IsNullOrWhiteSpace(evt.Content):
				_observer.ProgressText(evt.Content, evt.Timestamp);
				break;

			case Constants.Turns.EventTypes.System when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				HandleSystemError(evt);
				break;

			case Constants.Turns.EventTypes.Result when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase):
				// "No conversation found" - transient infrastructure error. Flag the auto-retry with the context preamble;
				// nothing is written to the transcript.
				_log.Information("No conversation found for session {FileName} - will retry with context", _node.FileName);
				_node.ClaudeSessionId = null;
				_appSettings.Save();
				NeedsContextRetry = true;
				SendErrorText     = null;
				break;

			case Constants.Turns.EventTypes.Result when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content)
				&& evt.Content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase):
				// ISSUE.010 - transient cross-process OAuth refresh-token race, not a real auth failure. Flag a single silent
				// retry; nothing is written so it never reads as a real failure.
				_log.Information("OAuth refresh race for session {FileName} - will retry once", _node.FileName);
				NeedsAuthRaceRetry = true;
				SendErrorText      = null;
				break;

			case Constants.Turns.EventTypes.Result when evt.IsError && !string.IsNullOrWhiteSpace(evt.Content):
				_log.Warning("Claude result error: {Error}", evt.Content);
				SendErrorText = evt.Content;
				_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, evt.Content);
				if (evt.Content.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
					_observer.AuthErrorChanged(true);
				_observer.ResultError(evt.Content, evt.Timestamp);
				break;

			case Constants.Turns.EventTypes.Result when !evt.IsError && evt.SessionId is not null:
				_observer.AuthErrorChanged(false);
				_node.ClaudeSessionId = evt.SessionId;
				_appSettings.Save();
				_observer.SessionIdCaptured();
				_observer.ResultEnded();
				break;

			case Constants.Turns.EventTypes.Result:
				_observer.ResultEnded();
				break;
		}

		// After the persistence above, so a delta never announces text that is not in the transcript yet.
		_progress.Observe(evt);
	}

	private void HandleSystemError(ClaudeStreamEvent evt)
	{
		var content = evt.Content!;

		// When a context retry or an auth-race retry is pending, stderr-based errors are suppressed: they echo the same
		// error the result event already handled.
		if (NeedsContextRetry || NeedsAuthRaceRetry)
			return;

		if (content.Contains(Constants.ContextRestore.NoConversationFoundMarker, StringComparison.OrdinalIgnoreCase))
		{
			_log.Information("No conversation found (system error) for session {FileName} - will retry with context", _node.FileName);
			_node.ClaudeSessionId = null;
			_appSettings.Save();
			NeedsContextRetry = true;
			SendErrorText     = null;
			return;
		}

		if (content.Contains(Constants.Auth.OAuthRefreshRaceMarker, StringComparison.OrdinalIgnoreCase))
		{
			_log.Information("OAuth refresh race (system error) for session {FileName} - will retry once", _node.FileName);
			NeedsAuthRaceRetry = true;
			SendErrorText      = null;
			return;
		}

		_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, content);
		if (content.Contains(Constants.Auth.InvalidCredentialsMarker, StringComparison.OrdinalIgnoreCase))
			_observer.AuthErrorChanged(true);
		_observer.SystemError(content, evt.Timestamp);
	}
}
