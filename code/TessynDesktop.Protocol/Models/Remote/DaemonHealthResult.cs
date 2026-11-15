namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>daemon.health</c>: counters only, no path and no setting.</summary>
/// <param name="UptimeSeconds">Seconds since the daemon started.</param>
/// <param name="RunId">The id of this daemon run (changes at every start).</param>
/// <param name="EventSeq">The head of the event log.</param>
/// <param name="SessionsRunning">Sessions with a turn running now.</param>
/// <param name="Index">The session index.</param>
/// <param name="LogBytes">Bytes of the log files; null when they cannot be read.</param>
/// <param name="WorkingSetMb">The process working set.</param>
/// <param name="Gen2Count">Generation 2 garbage collections so far.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonHealthResult(long UptimeSeconds, string RunId, long EventSeq, int SessionsRunning, DaemonIndexHealthDto Index, long? LogBytes, double WorkingSetMb, int? Gen2Count);
