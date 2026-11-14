using TessynDesktop.Client;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Stage A placeholder: the real <c>TessynClient</c> implementation arrives from the client worker.
/// Stage B replaces this registration with a factory that constructs it from the <see cref="DaemonEndpoint"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PendingTessynClientFactory : ITessynClientFactory
{
	public ITessynClient Create(DaemonEndpoint endpoint)
	{
		throw new NotSupportedException("The TessynClient implementation is not wired yet (stage B).");
	}
}
