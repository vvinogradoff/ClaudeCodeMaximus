using System;
using System.Collections.Generic;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Turns the neutral engine events of one turn into <see cref="TurnProgress"/> for the <see cref="ITurnProgressSink"/>s: text and thinking deltas,
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

	/// <summary>A text or thinking delta (any thread).</summary>
	public void Delta(TurnDeltaKind kind, string text)
	{
		if (_sinks.Count == 0 || !IsActive())
			return;

		Emit(new TurnDeltaProgress(_nodeId, _turnId, kind, text));
	}

	/// <summary>A tool call started; its name is kept for the result (any thread).</summary>
	public void ToolStarted(string toolId, string? toolName, string? summary)
	{
		if (_sinks.Count == 0 || !IsActive())
			return;

		var name = toolName ?? Constants.Turns.UnknownToolName;
		lock (_gate) _toolNames[toolId] = name;
		Emit(new TurnToolProgress(_nodeId, _turnId, toolId, name, TurnToolStatus.Started, summary, null));
	}

	/// <summary>A tool call finished, under the name its start had (any thread).</summary>
	public void ToolFinished(string toolId, string? summary, bool isError)
	{
		if (_sinks.Count == 0 || !IsActive())
			return;

		string? known;
		lock (_gate) _toolNames.TryGetValue(toolId, out known);
		Emit(new TurnToolProgress(_nodeId, _turnId, toolId, known ?? Constants.Turns.UnknownToolName, TurnToolStatus.Result,
			summary, isError ? true : null));
	}

	/// <summary>The usage of a successful turn; nothing when it reported none (any thread).</summary>
	public void Usage(int inputTokens, int outputTokens, double costUsd)
	{
		if (_sinks.Count == 0 || !IsActive() || (inputTokens <= 0 && outputTokens <= 0 && costUsd <= 0))
			return;

		Emit(new TurnUsageProgress(_nodeId, _turnId, inputTokens, outputTokens, costUsd));
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
