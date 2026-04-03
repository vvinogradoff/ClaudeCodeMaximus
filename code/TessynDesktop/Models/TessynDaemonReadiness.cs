namespace TessynDesktop.Models;

/// <summary>
/// Readiness state of the Tessyn daemon's index.
/// Reflects the daemon's internal state progression: cold → scanning → caught_up | degraded.
/// </summary>
/// <remarks>Created by Claude</remarks>
public enum TessynDaemonReadiness
{
    /// <summary>Daemon state unknown (not connected or no status received yet).</summary>
    Unknown,

    /// <summary>Daemon is starting up, index not yet available.</summary>
    Cold,

    /// <summary>Daemon is scanning/indexing JSONL files.</summary>
    Scanning,

    /// <summary>Index is fully built and up to date. All features available.</summary>
    Ready,

    /// <summary>Daemon is running but in a degraded state.</summary>
    Degraded
}
