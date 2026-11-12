using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>No Persistent Session Process is ever alive in the E2E harness.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeStreamingManager : IClaudeStreamingSessionManager
{
	public event Action<string>? ProcessExited { add { } remove { } }

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent) => false;

	public Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null) => Task.CompletedTask;

	public bool IsAlive(string nodeId) => false;

	public void Stop(string nodeId)
	{
	}

	public void TerminateAll()
	{
	}
}
