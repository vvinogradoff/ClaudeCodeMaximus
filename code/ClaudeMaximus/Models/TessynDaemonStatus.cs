using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// Parsed status notification from the Tessyn daemon, received on connect
/// and via index.state_changed events.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynDaemonStatus
{
    /// <summary>Daemon state: "cold", "scanning", "caught_up", or "degraded".</summary>
    public required string State { get; init; }

    public int SessionsIndexed { get; init; }
    public int SessionsTotal { get; init; }
    public long Uptime { get; init; }
    public string Version { get; init; } = string.Empty;
    public int ProtocolVersion { get; init; }
    public List<string> Capabilities { get; init; } = [];

    /// <summary>Maps the daemon's state string to a typed readiness enum.</summary>
    public TessynDaemonReadiness ToReadiness() => State switch
    {
        "cold"      => TessynDaemonReadiness.Cold,
        "scanning"  => TessynDaemonReadiness.Scanning,
        "caught_up" => TessynDaemonReadiness.Ready,
        "degraded"  => TessynDaemonReadiness.Degraded,
        _           => TessynDaemonReadiness.Unknown,
    };
}
