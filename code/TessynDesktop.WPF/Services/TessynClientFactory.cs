using TessynDesktop.Client;

namespace TessynDesktop.WPF.Services;

/// <summary>Builds the real <see cref="TessynClient"/> from the discovery result.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynClientFactory : ITessynClientFactory
{
	public ITessynClient Create(DaemonEndpoint endpoint) =>
		new TessynClient(endpoint, new TessynClientOptions
		{
			ClientName = Constants.Identity.ClientName,
			ClientVersion = Constants.Identity.ClientVersion,
		});
}
