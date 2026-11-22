using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Serilog;
using TessynDesktop.Codex.Events;
using TessynDesktop.Codex.Protocol;

namespace TessynDesktop.Codex;

/// <summary>
/// Turns the notifications of <b>one</b> Codex turn into neutral <see cref="CodexTurnEvent"/>s, following the table of FR.33.11:
/// <list type="bullet">
/// <item>deltas become text and thinking deltas; each completed <c>agentMessage</c> becomes one assistant message;</item>
/// <item>tool items become tool started and finished events with their one-line summaries; a plan update, a retrying error and an
/// unmapped item type become progress lines; a completed <c>contextCompaction</c> becomes a compaction; token usage becomes usage;</item>
/// <item>errors are classified by <c>codexErrorInfo</c> (FR.33.13) and reported once per turn;</item>
/// <item>on an interrupted <c>turn/completed</c>, an <c>agentMessage</c> that never completed is written from its deltas, with a last line
/// <c>[interrupted]</c> (FR.33.8).</item>
/// </list>
/// Notifications of other threads and other turns are ignored. When the turn id is not known yet (a compaction turn), the first
/// <c>turn/started</c> of the thread sets it. Not thread-safe: feed it from one notification stream.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnMapper
{
	private static readonly ILogger _log = Log.ForContext<CodexTurnMapper>();

	private readonly JsonSerializerOptions               _options;
	private readonly CodexToolSummarizer                 _tools  = new();
	private readonly CodexErrorClassifier                _errors = new();
	private readonly List<string>                        _agentMessageOrder = [];
	private readonly Dictionary<string, StringBuilder>   _agentMessageDeltas = new(StringComparer.Ordinal);
	private readonly HashSet<string>                     _completedAgentMessages = new(StringComparer.Ordinal);
	private readonly List<string>                        _assistantTexts = [];
	private CodexTurnErrorEvent?                         _error;
	private long                                         _inputTokens;
	private long                                         _outputTokens;
	private long?                                        _contextWindow;

	public CodexTurnMapper(string threadId, string? turnId = null, JsonSerializerOptions? options = null)
	{
		ThreadId = threadId;
		TurnId   = turnId;
		_options = options ?? Constants.Codex.SerializerOptions;
	}

	public string ThreadId { get; }

	public string? TurnId { get; private set; }

	/// <summary>Set once <c>turn/completed</c> of this turn was mapped.</summary>
	public CodexTurnCompletedEvent? Completion { get; private set; }

	public bool IsCompleted => Completion is not null;

	/// <summary>Sets the turn id from the <c>turn/start</c> answer, when it was not known at construction.</summary>
	public void SetTurnId(string turnId) => TurnId ??= turnId;

	/// <summary>The events of one notification; empty when it is not part of this turn or carries nothing to report.</summary>
	public IReadOnlyList<CodexTurnEvent> Map(CodexNotification notification)
	{
		if (IsCompleted || !BelongsToThisTurn(notification))
			return [];

		try
		{
			return notification.Method switch
			{
				Constants.Codex.Methods.AgentMessageDelta         => MapAgentMessageDelta(notification.Params),
				Constants.Codex.Methods.ReasoningSummaryTextDelta => MapReasoningDelta(notification.Params),
				Constants.Codex.Methods.ItemStarted               => MapItemStarted(notification.Params),
				Constants.Codex.Methods.ItemCompleted             => MapItemCompleted(notification.Params),
				Constants.Codex.Methods.TurnPlanUpdated           => MapPlan(notification.Params),
				Constants.Codex.Methods.ThreadTokenUsageUpdated   => MapTokenUsage(notification.Params),
				Constants.Codex.Methods.Error                     => MapError(notification.Params),
				Constants.Codex.Methods.TurnCompleted             => MapTurnCompleted(notification.Params),
				_                                                 => [],
			};
		}
		catch (JsonException ex)
		{
			_log.Warning(ex, "Codex notification {Method} on thread {ThreadId} could not be read; it is skipped", notification.Method, ThreadId);
			return [];
		}
	}

	private bool BelongsToThisTurn(CodexNotification notification)
	{
		var p = notification.Params;
		if (p.ValueKind != JsonValueKind.Object)
			return false;
		if (!p.TryGetProperty(Constants.Codex.Rpc.ThreadId, out var thread) || thread.ValueKind != JsonValueKind.String || thread.GetString() != ThreadId)
			return false;

		string? turnId = null;
		if (p.TryGetProperty(Constants.Codex.Rpc.TurnId, out var turnIdElement) && turnIdElement.ValueKind == JsonValueKind.String)
			turnId = turnIdElement.GetString();
		else if (p.TryGetProperty(Constants.Codex.Rpc.Turn, out var turn) && turn.ValueKind == JsonValueKind.Object
			&& turn.TryGetProperty(Constants.Codex.Rpc.Id, out var id) && id.ValueKind == JsonValueKind.String)
			turnId = id.GetString();

		if (TurnId is null)
		{
			if (notification.Method == Constants.Codex.Methods.TurnStarted && turnId is not null)
				TurnId = turnId;
			return false;
		}

		return turnId is null || turnId == TurnId;
	}

	private IReadOnlyList<CodexTurnEvent> MapAgentMessageDelta(JsonElement p)
	{
		var delta = p.Deserialize<CodexAgentMessageDeltaNotification>(_options)!;
		TrackAgentMessage(delta.ItemId).Append(delta.Delta);
		return [new CodexTextDeltaEvent(delta.ItemId, delta.Delta)];
	}

	private IReadOnlyList<CodexTurnEvent> MapReasoningDelta(JsonElement p)
	{
		var delta = p.Deserialize<CodexReasoningSummaryTextDeltaNotification>(_options)!;
		return [new CodexThinkingDeltaEvent(delta.ItemId, delta.Delta)];
	}

	private IReadOnlyList<CodexTurnEvent> MapItemStarted(JsonElement p)
	{
		var item = p.Deserialize<CodexItemStartedNotification>(_options)!.Item;

		if (item.Type == Constants.Codex.Protocol.ItemAgentMessage)
		{
			TrackAgentMessage(item.Id);
			return [];
		}

		if (_tools.KindOf(item.Type) is { } kind)
			return [new CodexToolStartedEvent(item.Id, kind, _tools.Summarize(kind, item, finished: false))];

		if (item.Type is Constants.Codex.Protocol.ItemUserMessage or Constants.Codex.Protocol.ItemReasoning or Constants.Codex.Protocol.ItemContextCompaction)
			return [];

		_log.Debug("Codex item type {Type} on thread {ThreadId} is not mapped", item.Type, ThreadId);
		return [new CodexProgressEvent(CodexProgressKind.UnmappedItem, string.Format(Constants.Codex.Texts.UnmappedItemFormat, item.Type))];
	}

	private IReadOnlyList<CodexTurnEvent> MapItemCompleted(JsonElement p)
	{
		var item = p.Deserialize<CodexItemCompletedNotification>(_options)!.Item;

		if (item.Type == Constants.Codex.Protocol.ItemAgentMessage)
		{
			_completedAgentMessages.Add(item.Id);
			var text = !string.IsNullOrEmpty(item.Text) ? item.Text : TrackAgentMessage(item.Id).ToString();
			if (string.IsNullOrEmpty(text))
				return [];

			_assistantTexts.Add(text);
			return [new CodexAssistantMessageEvent(item.Id, text, item.Phase, Interrupted: false)];
		}

		if (item.Type == Constants.Codex.Protocol.ItemContextCompaction)
			return [new CodexCompactionEvent(item.Id)];

		if (_tools.KindOf(item.Type) is { } kind)
			return [new CodexToolFinishedEvent(item.Id, kind, _tools.Summarize(kind, item, finished: true), _tools.Succeeded(kind, item))];

		return [];
	}

	private IReadOnlyList<CodexTurnEvent> MapPlan(JsonElement p)
	{
		var plan  = p.Deserialize<CodexTurnPlanUpdatedNotification>(_options)!.Plan;
		var done  = plan.Count(s => s.Status == Constants.Codex.Protocol.PlanStepCompleted);
		var step  = plan.FirstOrDefault(s => s.Status == Constants.Codex.Protocol.PlanStepInProgress)
			?? plan.FirstOrDefault(s => s.Status == Constants.Codex.Protocol.PlanStepPending)
			?? plan.LastOrDefault();

		var text = step is null
			? string.Format(Constants.Codex.Texts.PlanNoStepFormat, done, plan.Count)
			: string.Format(Constants.Codex.Texts.PlanFormat, done, plan.Count, step.Step);
		return [new CodexProgressEvent(CodexProgressKind.Plan, text)];
	}

	private IReadOnlyList<CodexTurnEvent> MapTokenUsage(JsonElement p)
	{
		var usage  = p.Deserialize<CodexTokenUsageUpdatedNotification>(_options)!.TokenUsage;
		var input  = usage.Last.InputTokens;
		var output = usage.Last.OutputTokens + usage.Last.ReasoningOutputTokens;

		_inputTokens   += input;
		_outputTokens  += output;
		_contextWindow  = usage.ModelContextWindow ?? _contextWindow;
		return [new CodexUsageEvent(input, output, usage.ModelContextWindow)];
	}

	private IReadOnlyList<CodexTurnEvent> MapError(JsonElement p)
	{
		var error = p.Deserialize<CodexErrorNotification>(_options)!;
		if (error.WillRetry)
			return [new CodexProgressEvent(CodexProgressKind.Retry, string.Format(Constants.Codex.Texts.RetryingFormat, error.Error.Message))];

		if (_error is not null)
			return [];

		_error = _errors.ToErrorEvent(error.Error);
		return [_error];
	}

	private IReadOnlyList<CodexTurnEvent> MapTurnCompleted(JsonElement p)
	{
		var turn   = p.Deserialize<CodexTurnNotification>(_options)!.Turn;
		var events = new List<CodexTurnEvent>();

		CodexTurnOutcome outcome;
		switch (turn.Status)
		{
			case Constants.Codex.Protocol.TurnCompleted:
				outcome = CodexTurnOutcome.Completed;
				break;

			case Constants.Codex.Protocol.TurnInterrupted:
				outcome = CodexTurnOutcome.Interrupted;
				events.AddRange(FlushInterruptedMessages());
				break;

			default:
				outcome = CodexTurnOutcome.Failed;
				if (_error is null)
				{
					_error = _errors.ToErrorEvent(turn.Error ?? new CodexTurnError { Message = $"the turn ended with status {turn.Status}" });
					events.Add(_error);
				}
				break;
		}

		Completion = new CodexTurnCompletedEvent(
			turn.Id,
			outcome,
			string.Join("\n\n", _assistantTexts),
			_inputTokens,
			_outputTokens,
			_contextWindow,
			outcome == CodexTurnOutcome.Failed ? _error : null);
		events.Add(Completion);
		return events;
	}

	private IEnumerable<CodexTurnEvent> FlushInterruptedMessages()
	{
		foreach (var itemId in _agentMessageOrder)
		{
			if (_completedAgentMessages.Contains(itemId))
				continue;

			var partial = _agentMessageDeltas[itemId].ToString();
			if (partial.Length == 0)
				continue;

			var text = partial.TrimEnd() + "\n" + Constants.Codex.Texts.InterruptedMarker;
			_assistantTexts.Add(text);
			yield return new CodexAssistantMessageEvent(itemId, text, Phase: null, Interrupted: true);
		}
	}

	private StringBuilder TrackAgentMessage(string itemId)
	{
		if (_agentMessageDeltas.TryGetValue(itemId, out var builder))
			return builder;

		builder = new StringBuilder();
		_agentMessageDeltas[itemId] = builder;
		_agentMessageOrder.Add(itemId);
		return builder;
	}
}
