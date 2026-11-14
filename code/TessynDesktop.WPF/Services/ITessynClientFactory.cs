using TessynDesktop.Client;

namespace TessynDesktop.WPF.Services;

/// <summary>Creates the <see cref="ITessynClient"/> for a discovered daemon endpoint.</summary>
/// <remarks>Created by Claude</remarks>
public interface ITessynClientFactory
{
	ITessynClient Create(DaemonEndpoint endpoint);
}
