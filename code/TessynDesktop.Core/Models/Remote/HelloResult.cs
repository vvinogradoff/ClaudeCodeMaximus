using System;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>client.hello</c> (contract section 3.1).</summary>
/// <param name="ProtocolVersion">The wire protocol version, <see cref="Constants.Remote.ProtocolVersion"/>.</param>
/// <param name="DaemonVersion">Version of the running desktop app.</param>
/// <param name="MachineName">Name of the PC the daemon runs on.</param>
/// <param name="RunId">A new GUID per process run; changes when the app restarts.</param>
/// <param name="ServerTimeUtc">The daemon's clock.</param>
/// <remarks>Created by Claude</remarks>
public sealed record HelloResult(
	int ProtocolVersion,
	string DaemonVersion,
	string MachineName,
	string RunId,
	DateTimeOffset ServerTimeUtc);
