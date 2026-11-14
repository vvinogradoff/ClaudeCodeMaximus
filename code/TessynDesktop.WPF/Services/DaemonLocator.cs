using TessynDesktop.Client;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IDaemonLocator"/> over the Client library's <see cref="DaemonDiscovery"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonLocator : IDaemonLocator
{
	private readonly DaemonDiscovery _discovery = new();

	public Task<DaemonDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) => _discovery.DiscoverAsync(cancellationToken);
}
