namespace TessynDesktop.Models;

/// <summary>Content of <c>%AppData%\TessynDesktop\daemon.json</c>: how a client process finds the running daemon. Written with the shared camelCase wire options.</summary>
/// <param name="Endpoint">Full TDCP URL, e.g. <c>http://127.0.0.1:51234/tdcp</c>.</param>
/// <param name="Pid">Process id of the daemon host.</param>
/// <param name="ProtocolVersion"><see cref="Constants.Remote.ProtocolVersion"/> of this daemon.</param>
/// <param name="RunId">The run id <c>client.hello</c> reports; changes at every start.</param>
/// <param name="TokenFile">Path of the DPAPI-protected token file (decrypt as the same Windows user); the token itself is never in this file.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonDiscoveryModel(string Endpoint, int Pid, int ProtocolVersion, string RunId, string TokenFile);
