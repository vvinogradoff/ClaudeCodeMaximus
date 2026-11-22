using System;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The state and the event handling of one interactive turn: which neutral engine events become transcript entries and what the observer
/// is told. Formerly <c>TurnController.HandleStreamEvent</c>; the Claude-specific matching (retry markers, auth and limits detection, the
/// session id) moved into the Claude engine (FR.33.1), which reports here through <see cref="IEngineEventSink"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnRun : IEngineEventSink
{
	private readonly SessionNodeModel _node;
	private readonly ISessionFileService _files;
	private readonly ITurnObserver _observer;
	private readonly TurnProgressTracker _progress;
	private readonly Action<bool>? _authErrorChanged;
	private readonly Action<string?>? _limitErrorChanged;

	// Cross-thread (prepared on the caller's thread, consumed on the reader thread): use Interlocked.
	private string? _pendingModelId;
	private string? _pendingEffort;

	public TurnRun(string turnId, SessionNodeModel node, ISessionFileService files, ITurnObserver observer,
		TurnProgressTracker progress, string? pendingModelId, string? pendingEffort, Action<bool>? authErrorChanged = null, Action<string?>? limitErrorChanged = null)
	{
		_authErrorChanged = authErrorChanged;
		_limitErrorChanged = limitErrorChanged;
		TurnId          = turnId;
		_node           = node;
		_files          = files;
		_observer       = observer;
		_progress       = progress;
		_pendingModelId = pendingModelId;
		_pendingEffort  = pendingEffort;
	}

	public string TurnId { get; }

	/// <inheritdoc />
	public object? EngineTurnState { get; set; }

	/// <summary>FR.21.22: text of the error result of this turn, reported with <c>TurnCompleted</c>; reset on every retry.</summary>
	public string? SendErrorText { get; set; }

	/// <inheritdoc />
	public void AssistantText(EngineAssistantText message)
	{
		if (message.IsLimitError)
			SetLimitError(message.Text);
		var fileModelId = Interlocked.Exchange(ref _pendingModelId, null);
		var fileEffort  = Interlocked.Exchange(ref _pendingEffort, null);
		_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleAssistant, message.Text,
			modelId: fileModelId, effort: fileEffort);
		_observer.AssistantEntryAppended(message.Text, message.Timestamp, fileModelId, fileEffort);

		// After the persistence above, so a delta never announces text that is not in the transcript yet.
		_progress.Delta(TurnDeltaKind.Text, message.Text);
	}

	/// <inheritdoc />
	public void Thinking(string text) => _progress.Delta(TurnDeltaKind.Thinking, text);

	/// <inheritdoc />
	public void ToolStarted(string toolId, string? name, string? summary) => _progress.ToolStarted(toolId, name, summary);

	/// <inheritdoc />
	public void ToolFinished(string toolId, string? summary, bool isError) => _progress.ToolFinished(toolId, summary, isError);

	/// <inheritdoc />
	public void Progress(string text, DateTimeOffset timestamp) => _observer.ProgressText(text, timestamp);

	/// <inheritdoc />
	public void Compacted(DateTimeOffset timestamp)
	{
		_files.AppendCompactionSeparator(_node.FileName);
		_observer.CompactionAppended(timestamp);
	}

	/// <inheritdoc />
	public void Error(EngineError error)
	{
		_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, error.Text);
		if (error.IsAuthError)
			SetAuthError(true);
		if (error.IsLimitError)
			SetLimitError(error.Text);
		_observer.SystemError(error.Text, error.Timestamp);
	}

	/// <inheritdoc />
	public void RetryPending(EngineRetryKind kind) => SendErrorText = null;

	/// <inheritdoc />
	public void Retrying(EngineRetryKind kind)
	{
		if (kind == EngineRetryKind.LostConversation)
		{
			_observer.ContextRetrying();
			_progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryContext);
		}
		else
			_progress.Status(TurnState.Retrying, RemoteProtocol.TurnStream.RetryAuth);
	}

	/// <inheritdoc />
	public void RetryRunning() => _progress.Status(TurnState.Running);

	/// <inheritdoc />
	public void PersistentProcessStarted() => _observer.StreamingProcessStarted();

	/// <inheritdoc />
	public void TurnEnded(EngineTurnEnd end)
	{
		if (!string.IsNullOrWhiteSpace(end.ErrorText))
		{
			SendErrorText = end.ErrorText;
			_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, end.ErrorText);
			if (end.IsAuthError)
				SetAuthError(true);
			if (end.IsLimitError)
				SetLimitError(end.ErrorText);
			_observer.ResultError(end.ErrorText, end.Timestamp);
			return;
		}

		if (end.Success && end.ConversationId is not null)
		{
			SetAuthError(false);
			SetLimitError(null);
			_observer.SessionIdCaptured();
			_observer.ResultEnded();
		}
		else
		{
			if (end.Success)
				SetLimitError(null);
			_observer.ResultEnded();
		}

		if (end.Success)
			_progress.Usage(end.InputTokens, end.OutputTokens, end.CostUsd);
	}

	/// <summary>The auth outcome goes to the observer (the client that runs the turn) and to the session's state (every client).</summary>
	private void SetAuthError(bool hasAuthError)
	{
		_observer.AuthErrorChanged(hasAuthError);
		_authErrorChanged?.Invoke(hasAuthError);
	}

	/// <summary>The limits outcome of the turn goes to the session's state (every client): the shortened text, or null when a later turn succeeded.</summary>
	private void SetLimitError(string? text) =>
		_limitErrorChanged?.Invoke(text == null ? null : LimitErrorDetector.Shorten(text));
}
