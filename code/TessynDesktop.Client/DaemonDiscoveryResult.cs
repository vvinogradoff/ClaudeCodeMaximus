namespace TessynDesktop.Client;

/// <summary>Result of daemon discovery: the status and, when <see cref="DaemonDiscoveryStatus.Found"/>, the endpoint.</summary>
/// <param name="Status">What was found.</param>
/// <param name="Daemon">Set only when <paramref name="Status"/> is Found.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonDiscoveryResult(DaemonDiscoveryStatus Status, DaemonEndpoint? Daemon);
