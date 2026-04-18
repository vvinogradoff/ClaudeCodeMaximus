using System;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// High-level service for sending messages via the Tessyn daemon and observing run events.
/// Bridges between SessionViewModel and the daemon's run.send / run.cancel / run.list RPCs.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITessynRunService
{
    /// <summary>
    /// Send a message to start or resume a Claude session via the daemon.
    /// Returns the runId. Subscribe to RunEvents filtered by runId for streaming output.
    /// </summary>
    Task<string> SendAsync(
        string projectPath,
        string prompt,
        string? externalId = null,
        string? model = null,
        string? permissionMode = null,
        string? profile = null,
        string? reasoningEffort = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cancel an active run.</summary>
    Task CancelAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>List all active runs (for reconnect recovery).</summary>
    Task<System.Collections.Generic.List<TessynActiveRun>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Stream of typed run events from all active runs.</summary>
    IObservable<TessynRunEvent> RunEvents { get; }
}
