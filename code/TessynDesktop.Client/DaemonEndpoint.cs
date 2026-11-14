namespace TessynDesktop.Client;

/// <summary>Everything needed to connect to a running daemon.</summary>
/// <param name="Endpoint">The TDCP URL, e.g. <c>http://127.0.0.1:51234/tdcp</c>.</param>
/// <param name="Token">The plain local token for the <c>X-CMX-Token</c> header.</param>
/// <param name="RunId">The run id the daemon published in <c>daemon.json</c>.</param>
/// <param name="Pid">Process id of the daemon host.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonEndpoint(Uri Endpoint, string Token, string RunId, int Pid);
