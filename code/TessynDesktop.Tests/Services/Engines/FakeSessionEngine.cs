using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>A session engine that only tracks live processes and records stops, for the router tests.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeSessionEngine : ISessionEngine
{
	public FakeSessionEngine(string engineId) => EngineId = engineId;

	public string EngineId { get; }
	public string DisplayName => EngineId;
	public EngineCapabilities Capabilities { get; } = new(false, true, true, false, true, false, false, false);
	public HashSet<string> Alive { get; } = [];
	public List<string> Stopped { get; } = [];
	public int TerminateAllCount { get; private set; }

	public string? GetConversationId(SessionNodeModel node) => null;
	public void ClearConversation(SessionNodeModel node) { }
	public bool DetachConversation(SessionNodeModel node) => false;
	public bool AdoptConversation(SessionNodeModel node, string conversationId, string? profileConfigDir) => true;
	public Task<EngineTurnPreparation?> PrepareTurnAsync(SessionNodeModel node, TurnSettings settings) => Task.FromResult<EngineTurnPreparation?>(null);
	public Task RunTurnAsync(EngineTurnRequest request, IEngineEventSink sink, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task RunTurnInLiveProcessAsync(SessionNodeModel node, string prompt, IEngineEventSink sink, CancellationToken cancellationToken) => Task.CompletedTask;
	public void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt) { }
	public Task<bool> InterruptAsync(string nodeId) => Task.FromResult(Alive.Remove(nodeId));
	public bool IsAlive(string nodeId) => Alive.Contains(nodeId);

	public void Stop(string nodeId)
	{
		Stopped.Add(nodeId);
		Alive.Remove(nodeId);
	}

	public void TerminateAll() => TerminateAllCount++;
}
