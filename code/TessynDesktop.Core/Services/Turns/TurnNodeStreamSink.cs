using System;
using TessynDesktop.Models.Engines;

namespace TessynDesktop.Services;

/// <summary>
/// The sink bound to a node's persistent process (Streaming Session Mode, FR.19). It hands each event to the turn holding the lock; events
/// outside any turn - background-task output, native schedules (FR.19.4), anything arriving while the next turn still waits for the lock - go
/// to a fresh background run: persisted and rendered by the last observer, but without any state of a finished turn (error text, model
/// stamp) and without progress.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnNodeStreamSink : IEngineEventSink
{
	private readonly TurnNodeContext _context;
	private readonly Func<TurnRun> _backgroundRun;

	public TurnNodeStreamSink(TurnNodeContext context, Func<TurnRun> backgroundRun)
	{
		_context       = context;
		_backgroundRun = backgroundRun;
	}

	private IEngineEventSink Target => _context.Active ?? _backgroundRun();

	public object? EngineTurnState
	{
		get => Target.EngineTurnState;
		set => Target.EngineTurnState = value;
	}

	public void AssistantText(EngineAssistantText message) => Target.AssistantText(message);
	public void Thinking(string text) => Target.Thinking(text);
	public void ToolStarted(string toolId, string? name, string? summary) => Target.ToolStarted(toolId, name, summary);
	public void ToolFinished(string toolId, string? summary, bool isError) => Target.ToolFinished(toolId, summary, isError);
	public void Progress(string text, DateTimeOffset timestamp) => Target.Progress(text, timestamp);
	public void Compacted(DateTimeOffset timestamp) => Target.Compacted(timestamp);
	public void Error(EngineError error) => Target.Error(error);
	public void RetryPending(EngineRetryKind kind) => Target.RetryPending(kind);
	public void Retrying(EngineRetryKind kind) => Target.Retrying(kind);
	public void RetryRunning() => Target.RetryRunning();
	public void PersistentProcessStarted() => Target.PersistentProcessStarted();
	public void TurnEnded(EngineTurnEnd end) => Target.TurnEnded(end);
}
