using TessynDesktop.Client;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="ITessynClientFactory"/> that hands out the given clients one by one.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SequenceClientFactory : ITessynClientFactory
{
	private readonly Queue<FakeTessynClient> _clients;

	public SequenceClientFactory(params FakeTessynClient[] clients) => _clients = new Queue<FakeTessynClient>(clients);

	public ITessynClient Create(DaemonEndpoint endpoint) => _clients.Dequeue();
}
