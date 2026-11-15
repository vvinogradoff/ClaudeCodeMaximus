using System;
using System.Collections.Generic;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Turns the stream events of one turn into <see cref="TurnProgress"/> for the <see cref="ITurnProgressSink"/>s: text and thinking deltas,
/// tool calls with their results, usage, status changes. Shared by the engine and the headless turn service, so both report alike.
/// A turn reports only between <see cref="Started"/> and <see cref="Finished"/>: events a streaming process delivers to a finished turn's
/// handler (background notifications between turns) are persisted by their owner as before, but not reported under its old id.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnProgressTracker
{
	private static readonly ILogger _log = Log.ForContext<TurnProgressTracker>();

	private readonly string _nodeId;
	private readonly string _turnId;
	private readonly IReadOnlyList<ITurnProgressSink> _sinks;
	private readonly object _gate = new();
	private readonly Dictionary<string, string> _toolNames = new(StringComparer.Ordinal);
	private bool _active;

	public TurnProgressTracker(string nodeId, string turnId, IReadOnlyList<ITurnProgressSink> sinks)
	{
		_nodeId = nodeId;
		_turnId = turnId;
		_sinks  = sinks;
	}

	public void Started(string? clientRequestId)
	{
		lock (_gate) _active = true;
		Emit(new TurnStartedProgress(_nodeId, _turnId, clientRequestId));
	}

	public void Status(TurnState state, string? detail = null)
	{
		if (!IsActive())
			return;

		Emit(new TurnStatusProgress(_nodeId, _turnId, state, detail));
	}

	/// <summary>Maps one stream event to progress (any thread).</summary>
	public void Observe(ClaudeStreamEvent evt)
	{
		if (_sinks.Count == 0 || !IsActive())
			return;

		switch (evt.Type)
		{
			case Constants.Turns.EventTypes.Assistant when !string.IsNullOrWhiteSpace(evt.Content):
				Emit(new TurnDeltaProgress(_nodeId, _turnId, TurnDeltaKind.Text, evt.Content));
				break;

			case Constants.Turns.EventTypes.Thinking when !string.IsNullOrWhiteSpace(evt.Content):
				Emit(new TurnDeltaProgress(_nodeId, _turnId, TurnDeltaKind.Thinking, evt.Content));
				break;

			case Constants.Turns.EventTypes.ToolUse when !string.IsNullOrEmpty(evt.ToolUseId):
				var name = evt.ToolName ?? Constants.Turns.UnknownToolName;
				lock (_gate) _toolNames[evt.ToolUseId] = name;
				Emit(new TurnToolProgress(_nodeId, _turnId, evt.ToolUseId, name, TurnToolStatus.Started, evt.ToolSummary, null));
				break;

			case Constants.Turns.EventTypes.ToolResult when !string.IsNullOrEmpty(evt.ToolUseId):
				string? known;
				lock (_gate) _toolNames.TryGetValue(evt.ToolUseId, out known);
				Emit(new TurnToolProgress(_nodeId, _turnId, evt.ToolUseId, known ?? Constants.Turns.UnknownToolName, TurnToolStatus.Result,
					evt.ToolSummary, evt.ToolIsError ? true : null));
				break;

			case Constants.Turns.EventTypes.Result when !evt.IsError && (evt.InputTokens > 0 || evt.OutputTokens > 0 || evt.CostUsd > 0):
				Emit(new TurnUsageProgress(_nodeId, _turnId, evt.InputTokens, evt.OutputTokens, evt.CostUsd));
				break;
		}
	}

	/// <summary>The turn ended: tells the sinks (so they flush) and stops further reports.</summary>
	public void Finished(bool cancelled, string? error, bool ran)
	{
		lock (_gate)
		{
			if (!_active)
				return;

			_active = false;
		}

		Emit(new TurnFinishedProgress(_nodeId, _turnId, cancelled, error, ran));
	}

	private bool IsActive()
	{
		lock (_gate) return _active;
	}

	private void Emit(TurnProgress progress)
	{
		foreach (var sink in _sinks)
		{
			try
			{
				sink.Publish(progress);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "A turn progress sink failed for turn {TurnId}", _turnId);
			}
		}
	}
}
