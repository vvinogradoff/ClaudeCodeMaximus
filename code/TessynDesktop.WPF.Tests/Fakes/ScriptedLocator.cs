using TessynDesktop.Client;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="IDaemonLocator"/> answering from a queue (the last answer repeats).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptedLocator : IDaemonLocator
{
	public Queue<DaemonDiscoveryResult> Results { get; } = new();

	public int Calls { get; private set; }

	public Task<DaemonDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
	{
		Calls++;
		var next = Results.Count > 1 ? Results.Dequeue() : Results.Peek();
		return Task.FromResult(next);
	}
}
