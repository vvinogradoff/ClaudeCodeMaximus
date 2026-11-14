using TessynDesktop.Client;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="ITessynClientFactory"/> that always returns the given fake client.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FixedClientFactory : ITessynClientFactory
{
	public FixedClientFactory(FakeTessynClient client) => Client = client;

	public FakeTessynClient Client { get; }

	public ITessynClient Create(DaemonEndpoint endpoint) => Client;
}
