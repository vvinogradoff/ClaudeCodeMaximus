using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>
/// A streaming manager whose process is alive. Like the real read loop, a turn sent into it delivers each scripted event first to the primary
/// callback (bound by the interactive turn that started the process, when there was one) and then to the turn's additive observer.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class LiveProcessStreamingManager : IClaudeStreamingSessionManager
{
	private readonly Queue<ClaudeStreamEvent[]> _turns = new();
	private Action<ClaudeStreamEvent>? _primary;

	public List<ClaudeStreamEvent> PrimaryEvents { get; } = [];
	public List<string> Sends { get; } = [];
	public int EnsureCalls { get; private set; }

	public void ScriptTurn(params ClaudeStreamEvent[] events) => _turns.Enqueue(events);

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent)
	{
		var first = _primary == null;
		EnsureCalls++;
		_primary = onEvent;
		return first;
	}

	public Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null)
	{
		Sends.Add(userMessage);
		if (_turns.Count == 0)
			return Task.CompletedTask;

		foreach (var evt in _turns.Dequeue())
		{
			PrimaryEvents.Add(evt);
			_primary?.Invoke(evt);
			onTurnEvent?.Invoke(evt);
		}
		return Task.CompletedTask;
	}

	public bool IsAlive(string nodeId) => true;
	public void Stop(string nodeId) { }
	public void TerminateAll() { }
	public event Action<string>? ProcessExited { add { } remove { } }
}
