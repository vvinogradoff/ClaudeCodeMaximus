using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// WebSocket client for the Tessyn daemon. Handles JSON-RPC 2.0 request/response
/// correlation, push notification routing, connection lifecycle, and auto-reconnect.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITessynDaemonService : IDisposable
{
    // --- Connection lifecycle ---

    /// <summary>Current WebSocket connection state.</summary>
    TessynConnectionState ConnectionState { get; }

    /// <summary>Current daemon readiness (index state). Unknown when not connected.</summary>
    TessynDaemonReadiness Readiness { get; }

    /// <summary>Last received daemon status (version, capabilities, session counts). Null before first status.</summary>
    TessynDaemonStatus? LastStatus { get; }

    /// <summary>Fires when ConnectionState changes.</summary>
    event EventHandler<TessynConnectionState>? ConnectionStateChanged;

    /// <summary>Fires when Readiness changes.</summary>
    event EventHandler<TessynDaemonReadiness>? ReadinessChanged;

    /// <summary>Connect to the daemon. Reads auth token and establishes WebSocket. Subscribes to events.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Gracefully disconnect. Stops auto-reconnect.</summary>
    Task DisconnectAsync();

    // --- Push notifications ---

    /// <summary>Stream of all daemon push notifications (run.*, session.*, index.*).</summary>
    IObservable<TessynNotification> Notifications { get; }

    /// <summary>Stream of typed run events, filtered from notifications.</summary>
    IObservable<TessynRunEvent> RunEvents { get; }

    // --- Session RPCs ---

    /// <summary>List sessions with optional filters.</summary>
    Task<List<TessynSessionModel>> SessionsListAsync(
        string? projectSlug = null,
        bool hidden = false,
        bool archived = false,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single session with messages and metadata by stable external ID.</summary>
    Task<TessynSessionGetResult> SessionsGetAsync(
        string externalId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rename a session (sets user title in durable metadata).</summary>
    Task SessionsRenameAsync(string externalId, string title, CancellationToken cancellationToken = default);

    /// <summary>Hide a session (soft delete).</summary>
    Task SessionsHideAsync(string externalId, bool hidden = true, CancellationToken cancellationToken = default);

    /// <summary>Archive a session.</summary>
    Task SessionsArchiveAsync(string externalId, bool archived = true, CancellationToken cancellationToken = default);

    // --- Toggle RPCs ---

    /// <summary>Get per-session toggle states.</summary>
    Task<TessynTogglesResult> TogglesGetAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>Set per-session toggle states. Only specified fields are updated.</summary>
    Task TogglesSetAsync(string externalId, bool? autoCommit = null, bool? autoBranch = null,
        bool? autoDocument = null, bool? autoCompact = null, CancellationToken cancellationToken = default);

    // --- Draft RPCs ---

    /// <summary>Save draft input text for a session.</summary>
    Task DraftSaveAsync(string externalId, string content, CancellationToken cancellationToken = default);

    /// <summary>Get saved draft for a session. Returns null if no draft.</summary>
    Task<string?> DraftGetAsync(string externalId, CancellationToken cancellationToken = default);

    // --- Search ---

    /// <summary>Full-text search across all sessions (FTS5).</summary>
    Task<TessynSearchResponse> SearchAsync(
        string query,
        string? projectSlug = null,
        string? role = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    // --- Titles ---

    /// <summary>Generate titles for untitled sessions.</summary>
    Task<int> TitlesGenerateAsync(int? limit = null, CancellationToken cancellationToken = default);

    // --- Run management ---

    /// <summary>
    /// Send a message to start or resume a Claude session. Returns the run ID immediately.
    /// Events stream via RunEvents observable.
    /// </summary>
    Task<string> RunSendAsync(
        string prompt,
        string projectPath,
        string? externalId = null,
        string? model = null,
        string? permissionMode = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cancel an active run via SIGINT.</summary>
    Task RunCancelAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>List all currently active runs.</summary>
    Task<List<TessynActiveRun>> RunListAsync(CancellationToken cancellationToken = default);

    /// <summary>Get state of a specific run.</summary>
    Task<TessynActiveRun?> RunGetAsync(string runId, CancellationToken cancellationToken = default);

    // --- Daemon management ---

    /// <summary>Request full reindex from JSONL files.</summary>
    Task<TessynReindexResult> ReindexAsync(CancellationToken cancellationToken = default);

    /// <summary>Request graceful daemon shutdown.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
