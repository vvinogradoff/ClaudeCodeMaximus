namespace TessynDesktop.Client;

/// <summary>Content of <c>daemon.json</c> (written by the daemon's <c>DaemonDiscoveryFile</c>).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record DaemonDiscoveryFileModel(string? Endpoint, int Pid, int ProtocolVersion, string? RunId, string? TokenFile);
