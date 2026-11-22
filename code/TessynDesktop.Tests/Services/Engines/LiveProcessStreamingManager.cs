using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>
/// A streaming manager whose process is already alive (started by an interactive turn). A turn sent into it delivers its scripted events to the
/// primary callback and to the turn's additive observer, as the real read loop does.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class LiveProcessStreamingManager : IClaudeStreamingSessionManager
{
	private readonly Queue<ClaudeStreamEvent[]> _turns = new();

	public List<ClaudeStreamEvent> PrimaryEvents { get; } = [];
	public List<string> Sends { get; } = [];

	public void ScriptTurn(params ClaudeStreamEvent[] events) => _turns.Enqueue(events);

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent) => false;

	public Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null)
	{
		Sends.Add(userMessage);
		if (_turns.Count == 0)
			return Task.CompletedTask;

		foreach (var evt in _turns.Dequeue())
		{
			PrimaryEvents.Add(evt);
			onTurnEvent?.Invoke(evt);
		}
		return Task.CompletedTask;
	}

	public bool IsAlive(string nodeId) => true;
	public void Stop(string nodeId) { }
	public void TerminateAll() { }
	public event Action<string>? ProcessExited { add { } remove { } }
}
