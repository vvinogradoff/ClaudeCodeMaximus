using TessynDesktop.Client;

namespace TessynDesktop.WPF.Services;

/// <summary>Finds the running daemon (reads <c>daemon.json</c> and the DPAPI token).</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonLocator
{
	Task<DaemonDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}
