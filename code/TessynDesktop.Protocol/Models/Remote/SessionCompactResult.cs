namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>session.compact</c>: the run was accepted and continues in the background.</summary>
/// <param name="Accepted">Always true (a rejected call is a JSON-RPC error).</param>
/// <param name="Queued">True when a turn or compaction was already running when the call was accepted (the run waits behind it).</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionCompactResult(bool Accepted, bool Queued);
