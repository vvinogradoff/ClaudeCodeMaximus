using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class TessynDaemonService : ITessynDaemonService
{
    private static readonly ILogger _log = Log.ForContext<TessynDaemonService>();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAppSettingsService _appSettings;
    private readonly Subject<TessynNotification> _notifications = new();
    private readonly Subject<TessynRunEvent> _runEvents = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _connectionCts;
    private CancellationTokenSource? _reconnectCts;
    private int _nextId;
    private bool _disposed;
    private bool _autoReconnect = true;

    private TessynConnectionState _connectionState = TessynConnectionState.Disconnected;
    private TessynDaemonReadiness _readiness = TessynDaemonReadiness.Unknown;
    private TessynDaemonStatus? _lastStatus;

    public TessynConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState == value) return;
            _connectionState = value;
            ConnectionStateChanged?.Invoke(this, value);
        }
    }

    public TessynDaemonReadiness Readiness
    {
        get => _readiness;
        private set
        {
            if (_readiness == value) return;
            _readiness = value;
            ReadinessChanged?.Invoke(this, value);
        }
    }

    public TessynDaemonStatus? LastStatus => _lastStatus;

    public event EventHandler<TessynConnectionState>? ConnectionStateChanged;
    public event EventHandler<TessynDaemonReadiness>? ReadinessChanged;

    public IObservable<TessynNotification> Notifications => _notifications.AsObservable();
    public IObservable<TessynRunEvent> RunEvents => _runEvents.AsObservable();

    public TessynDaemonService(IAppSettingsService appSettings)
    {
        _appSettings = appSettings;
    }

    // --- Connection lifecycle ---

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TessynDaemonService));

        _autoReconnect = true;
        CancelAndDisposeReconnect();

        await ConnectInternalAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        _autoReconnect = false;
        CancelAndDisposeReconnect();

        await CloseWebSocketAsync();
        ConnectionState = TessynConnectionState.Disconnected;
        Readiness = TessynDaemonReadiness.Unknown;
    }

    private async Task ConnectInternalAsync(CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            ConnectionState = TessynConnectionState.Connecting;
            Readiness = TessynDaemonReadiness.Unknown;

            await CloseWebSocketAsync();

            var token = ReadAuthToken();
            if (string.IsNullOrEmpty(token))
            {
                _log.Warning("Tessyn auth token not found; cannot connect");
                ConnectionState = TessynConnectionState.Disconnected;
                ScheduleReconnect();
                return;
            }

            var ws = new ClientWebSocket();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _ws = ws;
            _connectionCts = cts;

            var port = GetPort();
            var uri = new Uri($"ws://127.0.0.1:{port}?token={Uri.EscapeDataString(token)}");
            _log.Information("Connecting to Tessyn daemon at {Uri}", $"ws://127.0.0.1:{port}");

            await ws.ConnectAsync(uri, cts.Token);

            ConnectionState = TessynConnectionState.Connected;
            _log.Information("Connected to Tessyn daemon");

            // Start receive loop with captured local references (prevents socket race)
            _ = Task.Run(() => ReceiveLoopAsync(ws, cts.Token), cts.Token);

            // Subscribe to events
            await SubscribeAsync(cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning(ex, "Failed to connect to Tessyn daemon");
            ConnectionState = TessynConnectionState.Disconnected;
            ScheduleReconnect();
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        await SendRpcAsync<JsonElement>("subscribe", new
        {
            topics = new[] { "session.*", "index.*", "run.*" }
        }, cancellationToken);
    }

    // --- Receive loop ---

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[Constants.Tessyn.ReceiveBufferSize];
        var messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _log.Information("Tessyn daemon closed WebSocket connection");
                    break;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var text = messageBuilder.ToString();
                    messageBuilder.Clear();
                    ProcessMessage(text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (WebSocketException ex)
        {
            _log.Warning(ex, "WebSocket error in receive loop");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error in receive loop");
        }

        // Connection lost — only trigger reconnect if this is still the active socket
        if (_autoReconnect && !_disposed && ReferenceEquals(_ws, ws))
        {
            ConnectionState = TessynConnectionState.Reconnecting;
            Readiness = TessynDaemonReadiness.Unknown;
            ScheduleReconnect();
        }
    }

    private void ProcessMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            // Check if this is a response (has "id" field) or a notification (no "id")
            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
            {
                var id = idElement.GetInt32();
                HandleResponse(id, root);
            }
            else
            {
                HandleNotification(root);
            }
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Failed to parse message from daemon: {Text}", text.Length > 200 ? text[..200] : text);
        }
    }

    private void HandleResponse(int id, JsonElement root)
    {
        if (!_pending.TryRemove(id, out var tcs))
        {
            _log.Debug("Received response for unknown request ID {Id}", id);
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            var code = errorElement.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
            var message = errorElement.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";
            tcs.TrySetException(new TessynRpcException(code, message));
        }
        else if (root.TryGetProperty("result", out var resultElement))
        {
            tcs.TrySetResult(resultElement.Clone());
        }
        else
        {
            tcs.TrySetResult(default);
        }
    }

    private void HandleNotification(JsonElement root)
    {
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        if (method == null) return;

        var paramsElement = root.TryGetProperty("params", out var p) ? p.Clone() : default;

        if (method.StartsWith("run.", StringComparison.Ordinal))
            _log.Debug("Notification: {Method}", method);

        // Handle status notification (sent on connect and via index.state_changed)
        if (method == "status" || method == "index.state_changed")
        {
            if (paramsElement.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    var status = JsonSerializer.Deserialize<TessynDaemonStatus>(paramsElement.GetRawText(), _jsonOptions);
                    if (status != null)
                    {
                        // Protocol version enforcement
                        if (method == "status" && status.ProtocolVersion < Constants.Tessyn.MinProtocolVersion)
                        {
                            _log.Error("Tessyn daemon protocol version {Version} is below minimum {Min}. Disconnecting.",
                                status.ProtocolVersion, Constants.Tessyn.MinProtocolVersion);
                            _autoReconnect = false;
                            _ = Task.Run(DisconnectAsync);
                            return;
                        }

                        _lastStatus = status;
                        Readiness = status.ToReadiness();

                        if (method == "status")
                            _log.Information("Daemon status: {State}, protocol v{Version}, {Indexed}/{Total} sessions",
                                status.State, status.ProtocolVersion, status.SessionsIndexed, status.SessionsTotal);
                    }
                }
                catch (JsonException ex)
                {
                    _log.Warning(ex, "Failed to deserialize status notification");
                }
            }
        }

        // Route run events to typed stream
        if (method.StartsWith("run.", StringComparison.Ordinal) && paramsElement.ValueKind == JsonValueKind.Object)
        {
            var runEvent = ParseRunEvent(method, paramsElement);
            if (runEvent != null)
                _runEvents.OnNext(runEvent);
        }

        _notifications.OnNext(new TessynNotification { Method = method, Params = paramsElement });
    }

    private static TessynRunEvent? ParseRunEvent(string method, JsonElement paramsElement)
    {
        try
        {
            var type = method["run.".Length..];
            var runId = paramsElement.TryGetProperty("runId", out var r) ? r.GetString() : null;
            if (runId == null) return null;

            return new TessynRunEvent
            {
                Type = type,
                RunId = runId,
                ExternalId = paramsElement.TryGetProperty("externalId", out var eid) ? eid.GetString() : null,
                Model = paramsElement.TryGetProperty("model", out var mod) ? mod.GetString() : null,
                Tools = paramsElement.TryGetProperty("tools", out var t) ? JsonSerializer.Deserialize<List<string>>(t.GetRawText()) : null,
                BlockType = paramsElement.TryGetProperty("blockType", out var bt) ? bt.GetString() : null,
                Delta = paramsElement.TryGetProperty("delta", out var d) ? d.GetString() : null,
                BlockIndex = paramsElement.TryGetProperty("blockIndex", out var bi) ? bi.GetInt32() : null,
                ToolName = paramsElement.TryGetProperty("toolName", out var tn) ? tn.GetString() : null,
                Role = paramsElement.TryGetProperty("role", out var rl) ? rl.GetString() : null,
                StopReason = paramsElement.TryGetProperty("stopReason", out var sr) ? sr.GetString() : null,
                Error = paramsElement.TryGetProperty("error", out var err) ? err.GetString() : null,
                RetryAfterMs = paramsElement.TryGetProperty("retryAfterMs", out var ra) ? ra.GetInt32() : null,
                Usage = paramsElement.TryGetProperty("usage", out var u)
                    ? JsonSerializer.Deserialize<TessynRunUsage>(u.GetRawText(), _jsonOptions) : null,
            };
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to parse run event from {Method}", method);
            return null;
        }
    }

    // --- Auto-reconnect ---

    private void ScheduleReconnect()
    {
        if (!_autoReconnect || _disposed) return;

        CancelAndDisposeReconnect();
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        var ct = cts.Token;

        _ = Task.Run(async () =>
        {
            var delayMs = Constants.Tessyn.ReconnectBaseDelayMs;
            while (!ct.IsCancellationRequested && !_disposed)
            {
                ConnectionState = TessynConnectionState.Reconnecting;
                _log.Debug("Reconnecting to Tessyn daemon in {Delay}ms", delayMs);

                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { return; }

                try
                {
                    await ConnectInternalAsync(ct);
                    if (ConnectionState == TessynConnectionState.Connected)
                    {
                        _log.Information("Reconnected to Tessyn daemon");
                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Reconnect attempt failed");
                }

                delayMs = Math.Min(delayMs * 2, Constants.Tessyn.ReconnectMaxDelayMs);
            }
        }, ct);
    }

    private void CancelAndDisposeReconnect()
    {
        var old = _reconnectCts;
        _reconnectCts = null;
        if (old != null)
        {
            old.Cancel();
            old.Dispose();
        }
    }

    // --- Low-level RPC ---

    private async Task<T> SendRpcAsync<T>(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var request = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters != null)
            request["params"] = parameters;

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                // Capture _ws locally under the lock to prevent send/close race
                var ws = _ws;
                if (ws?.State != WebSocketState.Open)
                {
                    _pending.TryRemove(id, out _);
                    throw new InvalidOperationException("Not connected to Tessyn daemon");
                }

                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Constants.Tessyn.RpcTimeoutMs);

        try
        {
            var result = await tcs.Task.WaitAsync(timeoutCts.Token);

            if (typeof(T) == typeof(JsonElement))
                return (T)(object)result;

            return result.ValueKind == JsonValueKind.Undefined
                ? default!
                : JsonSerializer.Deserialize<T>(result.GetRawText(), _jsonOptions)!;
        }
        catch (OperationCanceledException)
        {
            // Clean up pending entry on any cancellation (caller or timeout)
            _pending.TryRemove(id, out _);

            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"RPC call '{method}' timed out after {Constants.Tessyn.RpcTimeoutMs}ms");

            throw;
        }
    }

    private async Task SendRpcVoidAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        await SendRpcAsync<JsonElement>(method, parameters, cancellationToken);
    }

    // --- Session RPCs ---

    public async Task<List<TessynSessionModel>> SessionsListAsync(
        string? projectSlug, bool hidden, bool archived, int? limit, int? offset,
        CancellationToken cancellationToken)
    {
        var p = new Dictionary<string, object?>();
        if (projectSlug != null) p["projectSlug"] = projectSlug;
        if (hidden) p["hidden"] = true;
        if (archived) p["archived"] = true;
        if (limit.HasValue) p["limit"] = limit.Value;
        if (offset.HasValue) p["offset"] = offset.Value;

        var result = await SendRpcAsync<JsonElement>("sessions.list", p.Count > 0 ? p : null, cancellationToken);

        if (result.TryGetProperty("sessions", out var sessions))
            return JsonSerializer.Deserialize<List<TessynSessionModel>>(sessions.GetRawText(), _jsonOptions) ?? [];

        return [];
    }

    public async Task<TessynSessionGetResult> SessionsGetAsync(
        string externalId, int? limit, int? offset, CancellationToken cancellationToken)
    {
        var p = new Dictionary<string, object?> { ["externalId"] = externalId };
        if (limit.HasValue) p["limit"] = limit.Value;
        if (offset.HasValue) p["offset"] = offset.Value;

        var result = await SendRpcAsync<TessynSessionGetResult>("sessions.get", p, cancellationToken);
        return result;
    }

    public Task SessionsRenameAsync(string externalId, string title, CancellationToken cancellationToken) =>
        SendRpcVoidAsync("sessions.rename", new { externalId, title }, cancellationToken);

    public Task SessionsHideAsync(string externalId, bool hidden, CancellationToken cancellationToken) =>
        SendRpcVoidAsync("sessions.hide", new { externalId, hidden }, cancellationToken);

    public Task SessionsArchiveAsync(string externalId, bool archived, CancellationToken cancellationToken) =>
        SendRpcVoidAsync("sessions.archive", new { externalId, archived }, cancellationToken);

    // --- Toggle RPCs ---

    public async Task<TessynTogglesResult> TogglesGetAsync(string externalId, CancellationToken cancellationToken)
    {
        return await SendRpcAsync<TessynTogglesResult>("sessions.toggles.get", new { externalId }, cancellationToken);
    }

    public Task TogglesSetAsync(string externalId, bool? autoCommit, bool? autoBranch,
        bool? autoDocument, bool? autoCompact, CancellationToken cancellationToken)
    {
        var p = new Dictionary<string, object?> { ["externalId"] = externalId };
        if (autoCommit.HasValue) p["autoCommit"] = autoCommit.Value;
        if (autoBranch.HasValue) p["autoBranch"] = autoBranch.Value;
        if (autoDocument.HasValue) p["autoDocument"] = autoDocument.Value;
        if (autoCompact.HasValue) p["autoCompact"] = autoCompact.Value;

        return SendRpcVoidAsync("sessions.toggles.set", p, cancellationToken);
    }

    // --- Draft RPCs ---

    public Task DraftSaveAsync(string externalId, string content, CancellationToken cancellationToken) =>
        SendRpcVoidAsync("sessions.draft.save", new { externalId, content }, cancellationToken);

    public async Task<string?> DraftGetAsync(string externalId, CancellationToken cancellationToken)
    {
        var result = await SendRpcAsync<JsonElement>("sessions.draft.get", new { externalId }, cancellationToken);
        return result.TryGetProperty("content", out var c) ? c.GetString() : null;
    }

    // --- Search ---

    public async Task<TessynSearchResponse> SearchAsync(
        string query, string? projectSlug, string? role, int? limit, int? offset,
        CancellationToken cancellationToken)
    {
        var p = new Dictionary<string, object?> { ["query"] = query };
        if (projectSlug != null) p["projectSlug"] = projectSlug;
        if (role != null) p["role"] = role;
        if (limit.HasValue) p["limit"] = limit.Value;
        if (offset.HasValue) p["offset"] = offset.Value;

        return await SendRpcAsync<TessynSearchResponse>("search", p, cancellationToken);
    }

    // --- Titles ---

    public async Task<int> TitlesGenerateAsync(int? limit, CancellationToken cancellationToken)
    {
        var p = limit.HasValue ? new Dictionary<string, object?> { ["limit"] = limit.Value } : null;
        var result = await SendRpcAsync<JsonElement>("titles.generate", p, cancellationToken);
        return result.TryGetProperty("generated", out var g) ? g.GetInt32() : 0;
    }

    // --- Run management ---

    public async Task<string> RunSendAsync(
        string prompt, string projectPath, string? externalId, string? model,
        string? permissionMode, CancellationToken cancellationToken)
    {
        var p = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["projectPath"] = projectPath,
        };
        if (externalId != null) p["externalId"] = externalId;
        if (model != null) p["model"] = model;
        if (permissionMode != null) p["permissionMode"] = permissionMode;

        var result = await SendRpcAsync<JsonElement>("run.send", p, cancellationToken);
        return result.TryGetProperty("runId", out var r)
            ? r.GetString() ?? throw new InvalidOperationException("run.send returned null runId")
            : throw new InvalidOperationException("run.send response missing runId");
    }

    public Task RunCancelAsync(string runId, CancellationToken cancellationToken) =>
        SendRpcVoidAsync("run.cancel", new { runId }, cancellationToken);

    public async Task<List<TessynActiveRun>> RunListAsync(CancellationToken cancellationToken)
    {
        return await SendRpcAsync<List<TessynActiveRun>>("run.list", null, cancellationToken);
    }

    public async Task<TessynActiveRun?> RunGetAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            return await SendRpcAsync<TessynActiveRun>("run.get", new { runId }, cancellationToken);
        }
        catch (TessynRpcException ex) when (ex.Code == Constants.Tessyn.ErrorRunNotFound)
        {
            return null;
        }
    }

    // --- Daemon management ---

    public async Task<TessynReindexResult> ReindexAsync(CancellationToken cancellationToken)
    {
        return await SendRpcAsync<TessynReindexResult>("reindex", null, cancellationToken);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        SendRpcVoidAsync("shutdown", null, cancellationToken);

    // --- Platform helpers ---

    private static string ReadAuthToken()
    {
        var tokenPath = GetAuthTokenPath();
        if (!File.Exists(tokenPath))
        {
            _log.Debug("Auth token file not found at {Path}", tokenPath);
            return string.Empty;
        }

        return File.ReadAllText(tokenPath).Trim();
    }

    private static string GetAuthTokenPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "tessyn", "ws-auth-token");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "tessyn", "Data", "ws-auth-token");

        // Linux
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "tessyn", "ws-auth-token");
    }

    internal static int GetPort()
    {
        var envPort = Environment.GetEnvironmentVariable("TESSYN_WS_PORT");
        return int.TryParse(envPort, out var port) ? port : Constants.Tessyn.DefaultPort;
    }

    private async Task CloseWebSocketAsync()
    {
        var cts = _connectionCts;
        _connectionCts = null;
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        var ws = _ws;
        _ws = null;

        if (ws != null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }
            catch { /* best-effort close */ }

            ws.Dispose();
        }

        // Fail all pending requests
        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetCanceled();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _autoReconnect = false;
        CancelAndDisposeReconnect();

        CloseWebSocketAsync().GetAwaiter().GetResult();

        _notifications.OnCompleted();
        _runEvents.OnCompleted();
        _notifications.Dispose();
        _runEvents.Dispose();
        _sendLock.Dispose();
        _connectLock.Dispose();
    }
}
