using System;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// The neutral event handling of one headless turn (orchestration, schedules, Telegram), formerly <c>SessionTurnService.HandleEvent</c> and,
/// for a turn in a live streaming process, its <c>ObserveEvent</c>. It collects what the caller gets back (<see cref="TurnResultModel"/>):
/// the assistant text, the error and the tokens, and reports progress.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class HeadlessTurnSink : IEngineEventSink
{
	private readonly SessionNodeModel _node;
	private readonly ISessionFileService _files;
	private readonly TurnProgressTracker _progress;
	private readonly bool _persists;
	private readonly string? _pendingModelId;
	private readonly string? _pendingEffort;
	private readonly StringBuilder _resultText = new();
	private bool _firstAssistant = true;

	/// <param name="persists">
	/// The sink writes the transcript. False for a turn in a live streaming process (FR.19.13): the process's own sink writes it, this one only
	/// observes.
	/// </param>
	public HeadlessTurnSink(SessionNodeModel node, ISessionFileService files, TurnProgressTracker progress, bool persists,
		string? pendingModelId = null, string? pendingEffort = null)
	{
		_node           = node;
		_files          = files;
		_progress       = progress;
		_persists       = persists;
		_pendingModelId = pendingModelId;
		_pendingEffort  = pendingEffort;
	}

	/// <summary>The assistant text of the turn, all messages concatenated (the worker's result, FR.15).</summary>
	public string ResultText => _resultText.ToString();

	public bool IsError { get; private set; }

	public string? ErrorMessage { get; private set; }

	public int InputTokens { get; private set; }

	public int OutputTokens { get; private set; }

	public double CostUsd { get; private set; }

	/// <inheritdoc />
	public void AssistantText(EngineAssistantText message)
	{
		if (_persists)
		{
			string? fileModelId = null, fileEffort = null;
			if (_firstAssistant)
			{
				_firstAssistant = false;
				fileModelId     = _pendingModelId;
				fileEffort      = _pendingEffort;
			}
			_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleAssistant, message.Text,
				modelId: fileModelId, effort: fileEffort);
		}

		_resultText.Append(message.Text);
		_progress.Delta(TurnDeltaKind.Text, message.Text);
	}

	/// <inheritdoc />
	public void Thinking(string text) => _progress.Delta(TurnDeltaKind.Thinking, text);

	/// <inheritdoc />
	public void ToolStarted(string toolId, string? name, string? summary) => _progress.ToolStarted(toolId, name, summary);

	/// <inheritdoc />
	public void ToolFinished(string toolId, string? summary, bool isError) => _progress.ToolFinished(toolId, summary, isError);

	/// <inheritdoc />
	public void Progress(string text, DateTimeOffset timestamp)
	{
		// A headless turn shows no progress lines.
	}

	/// <inheritdoc />
	public void Compacted(DateTimeOffset timestamp)
	{
		if (_persists)
			_files.AppendCompactionSeparator(_node.FileName);
	}

	/// <inheritdoc />
	public void Error(EngineError error)
	{
		if (_persists)
			_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, error.Text);
	}

	/// <inheritdoc />
	public void RetryPending(EngineRetryKind kind)
	{
		// The retried error was never recorded: nothing to reset.
	}

	/// <inheritdoc />
	public void Retrying(EngineRetryKind kind) =>
		_progress.Status(TurnState.Retrying, kind == EngineRetryKind.LostConversation
			? RemoteProtocol.TurnStream.RetryContext
			: RemoteProtocol.TurnStream.RetryAuth);

	/// <inheritdoc />
	public void RetryRunning() => _progress.Status(TurnState.Running);

	/// <inheritdoc />
	public void PersistentProcessStarted()
	{
		// A headless turn never starts a persistent process.
	}

	/// <inheritdoc />
	public void TurnEnded(EngineTurnEnd end)
	{
		if (!string.IsNullOrWhiteSpace(end.ErrorText))
		{
			IsError      = true;
			ErrorMessage = end.ErrorText;
			if (_persists)
				_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleSystem, end.ErrorText);
			return;
		}

		if (!end.Success)
			return;

		// A turn that writes the transcript takes the tokens of the result that carries the session id, as it always did; an observing one
		// takes them from any successful result.
		if (!_persists || end.ConversationId is not null)
		{
			InputTokens  = end.InputTokens;
			OutputTokens = end.OutputTokens;
			CostUsd      = end.CostUsd;
		}

		_progress.Usage(end.InputTokens, end.OutputTokens, end.CostUsd);
	}
}
