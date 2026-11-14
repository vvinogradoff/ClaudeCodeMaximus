using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>client.hello</c> (contract section 3.1; the last three fields are additive).</summary>
/// <param name="ProtocolVersion">The wire protocol version, <see cref="Constants.Remote.ProtocolVersion"/>.</param>
/// <param name="DaemonVersion">Version of the running desktop app.</param>
/// <param name="MachineName">Name of the PC the daemon runs on.</param>
/// <param name="RunId">A new GUID per process run; changes when the app restarts.</param>
/// <param name="ServerTimeUtc">The daemon's clock.</param>
/// <param name="Capabilities">Features this daemon really implements (see <see cref="Constants.Remote.Capabilities"/>); a client enables a feature only when it is listed.</param>
/// <param name="MinProtocolVersion">The oldest client protocol version the daemon still serves.</param>
/// <param name="EventSeq">Seq of the newest event in the daemon's event log at the time of the call (0 before the first); resume a stream from it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record HelloResult(
	int ProtocolVersion,
	string DaemonVersion,
	string MachineName,
	string RunId,
	DateTimeOffset ServerTimeUtc,
	IReadOnlyList<string> Capabilities,
	int MinProtocolVersion,
	long EventSeq);
