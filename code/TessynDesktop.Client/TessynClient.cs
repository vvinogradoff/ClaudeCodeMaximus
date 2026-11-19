using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

/// <summary>
/// <see cref="ITessynClient"/> over <see cref="HttpClient"/>: JSON-RPC 2.0 POSTs to the daemon's <c>/tdcp</c> endpoint with the
/// token header (never an <c>Origin</c> header), and one SSE stream that resumes with <c>Last-Event-ID</c> after a drop.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TessynClient : ITessynClient
{
	private const string JsonContentType = "application/json";
	private const string EventStreamContentType = "text/event-stream";

	private readonly Uri _endpoint;
	private readonly string _token;
	private readonly TessynClientOptions _options;
	private readonly HttpClient _http;
	private readonly bool _ownsHttp;
	private readonly object _gate = new();
	private readonly CancellationTokenSource _lifetime = new();

	private TessynClientState _state = TessynClientState.Disconnected;
	private HelloResult? _serverInfo;
	private Task? _streamTask;
	private long _nextRequestId;
	private long _lastSeq;
	private string? _runId;
	private int _disposed;
	private int _skippedFrameReported;
	private readonly Queue<TessynClientStateChangedEventArgs> _pendingStateEvents = new();
	private bool _drainingStateEvents;

	/// <summary>Creates a client for <paramref name="endpoint"/> (the full <c>/tdcp</c> URL) authenticated with <paramref name="token"/>.</summary>
	public TessynClient(Uri endpoint, string token, TessynClientOptions? options = null)
		: this(endpoint, token, options, new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, ownsHttp: true)
	{
	}

	/// <summary>Creates a client for a discovered daemon.</summary>
	public TessynClient(DaemonEndpoint daemon, TessynClientOptions? options = null)
		: this(daemon.Endpoint, daemon.Token, options)
	{
	}

	/// <summary>Creates a client that sends through <paramref name="http"/> (tests); the caller keeps ownership of it.</summary>
	public TessynClient(Uri endpoint, string token, TessynClientOptions? options, HttpClient http)
		: this(endpoint, token, options, http, ownsHttp: false)
	{
	}

	private TessynClient(Uri endpoint, string token, TessynClientOptions? options, HttpClient http, bool ownsHttp)
	{
		_endpoint = endpoint;
		_token    = token;
		_options  = options ?? new TessynClientOptions();
		_http     = http;
		_ownsHttp = ownsHttp;
	}

	/// <inheritdoc />
	public event EventHandler<TessynClientStateChangedEventArgs>? StateChanged;

	/// <inheritdoc />
	public event EventHandler<RemoteNotification>? NotificationReceived;

	/// <inheritdoc />
	public event EventHandler<ResyncedEventArgs>? Resynced;

	/// <inheritdoc />
	public TessynClientState State
	{
		get
		{
			lock (_gate)
				return _state;
		}
	}

	/// <inheritdoc />
	public HelloResult? ServerInfo
	{
		get
		{
			lock (_gate)
				return _serverInfo;
		}
	}

	// ── connection ────────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public async Task ConnectAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		// Capture the lifetime token first: after disposal the source cannot hand one out.
		var lifetime = _lifetime.Token;

		if (!TryBeginConnect())
			throw new InvalidOperationException($"The client is already {State}.");

		try
		{
			var hello = await HandshakeAsync(cancellationToken).ConfigureAwait(false);
			lock (_gate)
			{
				_lastSeq = hello.EventSeq;
				_runId   = hello.RunId;
			}
		}
		catch (TessynAuthException)
		{
			SetState(TessynClientState.AuthFailed);
			throw;
		}
		catch (TessynIncompatibleException)
		{
			SetState(TessynClientState.Incompatible);
			throw;
		}
		catch
		{
			SetState(TessynClientState.Disconnected);
			throw;
		}

		if (lifetime.IsCancellationRequested)
		{
			SetState(TessynClientState.Disconnected);
			throw new ObjectDisposedException(nameof(TessynClient));
		}

		SetState(TessynClientState.Connected);

		// A daemon without the local stream still serves calls; it just pushes nothing.
		if (HasCapability(RemoteProtocol.Capabilities.EventsLocal))
			_streamTask = Task.Run(() => RunStreamAsync(lifetime), CancellationToken.None);
	}

	private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

	/// <summary>Moves to Connecting unless a connection is already running; the check and the change are one step.</summary>
	private bool TryBeginConnect()
	{
		lock (_gate)
		{
			if (_state is TessynClientState.Connecting or TessynClientState.Connected or TessynClientState.Reconnecting)
				return false;

			QueueStateChange(TessynClientState.Connecting);
		}

		DrainStateEvents();
		return true;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		await _lifetime.CancelAsync().ConfigureAwait(false);

		if (_streamTask != null)
		{
			try
			{
				await _streamTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// Expected: the stream loop was cancelled.
			}
		}

		SetState(TessynClientState.Disconnected);
		_lifetime.Dispose();
		if (_ownsHttp)
			_http.Dispose();
	}

	private async Task<HelloResult> HandshakeAsync(CancellationToken cancellationToken)
	{
		var hello = await HelloAsync(cancellationToken).ConfigureAwait(false);
		if (hello.MinProtocolVersion > RemoteProtocol.ProtocolVersion)
			throw new TessynIncompatibleException(hello);

		lock (_gate)
			_serverInfo = hello;
		return hello;
	}

	private bool HasCapability(string capability)
	{
		var info = ServerInfo;
		return info != null && info.Capabilities.Contains(capability);
	}

	private void SetState(TessynClientState next)
	{
		lock (_gate)
			QueueStateChange(next);

		DrainStateEvents();
	}

	/// <summary>Records a state change; the caller holds <c>_gate</c>. Events are raised in the order they were queued.</summary>
	private void QueueStateChange(TessynClientState next)
	{
		if (_state == next)
			return;

		_pendingStateEvents.Enqueue(new TessynClientStateChangedEventArgs(_state, next));
		_state = next;
	}

	/// <summary>Raises queued state events one at a time; a thread that finds another one draining leaves its events to it.</summary>
	private void DrainStateEvents()
	{
		lock (_gate)
		{
			if (_drainingStateEvents)
				return;

			_drainingStateEvents = true;
		}

		while (true)
		{
			TessynClientStateChangedEventArgs? args;
			lock (_gate)
			{
				if (!_pendingStateEvents.TryDequeue(out args))
				{
					_drainingStateEvents = false;
					return;
				}
			}

			Raise(() => StateChanged?.Invoke(this, args));
		}
	}

	/// <summary>Runs a consumer handler; a throwing handler must never kill the stream loop.</summary>
	private static void Raise(Action raise)
	{
		try
		{
			raise();
		}
		catch (Exception)
		{
			// Consumer bug; the client keeps running.
		}
	}

	// ── notification stream ───────────────────────────────────────────────────────

	private async Task RunStreamAsync(CancellationToken ct)
	{
		var attempt = 0;
		var rehello = false;

		while (!ct.IsCancellationRequested)
		{
			try
			{
				if (rehello)
					await RefreshAfterDropAsync(ct).ConfigureAwait(false);

				await ReadStreamAsync(
					ct,
					onHeaders: () =>
					{
						if (State == TessynClientState.Reconnecting)
							SetState(TessynClientState.Connected);
					},
					onActivity: () => attempt = 0).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				return;
			}
			catch (TessynAuthException)
			{
				SetState(TessynClientState.AuthFailed);
				return;
			}
			catch (TessynIncompatibleException)
			{
				SetState(TessynClientState.Incompatible);
				return;
			}
			catch (Exception)
			{
				// Transport failure or the server closing the stream: fall through to the backoff.
			}

			rehello = true;
			SetState(TessynClientState.Reconnecting);

			try
			{
				await Task.Delay(NextBackoff(attempt++), ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private TimeSpan NextBackoff(int attempt)
	{
		var factor = Math.Pow(2, Math.Min(attempt, 16));
		var delay  = TimeSpan.FromTicks((long)Math.Min(_options.InitialBackoff.Ticks * factor, _options.MaxBackoff.Ticks));
		return delay > _options.MaxBackoff ? _options.MaxBackoff : delay;
	}

	/// <summary>After a drop: a new hello tells whether the daemon restarted; if so the consumer must reload and the stream restarts at the new head.</summary>
	private async Task RefreshAfterDropAsync(CancellationToken ct)
	{
		var hello = await HandshakeAsync(ct).ConfigureAwait(false);

		string? previousRun;
		lock (_gate)
			previousRun = _runId;

		if (previousRun == hello.RunId)
			return;

		lock (_gate)
		{
			_runId   = hello.RunId;
			_lastSeq = hello.EventSeq;
		}

		Raise(() => Resynced?.Invoke(this, new ResyncedEventArgs(ResyncedEventArgs.RunChangedReason, hello.EventSeq)));
	}

	/// <param name="onHeaders">Runs when the daemon accepted the stream request.</param>
	/// <param name="onActivity">Runs for every line received (a comment counts): only then is the backoff reset.</param>
	private async Task ReadStreamAsync(CancellationToken ct, Action onHeaders, Action onActivity)
	{
		long lastSeq;
		string? runId;
		lock (_gate)
		{
			lastSeq = _lastSeq;
			runId   = _runId;
		}

		var resume = HasCapability(RemoteProtocol.Capabilities.EventsResume);
		var query = new List<string>();
		if (runId != null && resume)
			query.Add($"{RemoteProtocol.Sse.RunIdQuery}={Uri.EscapeDataString(runId)}");
		if (!string.IsNullOrEmpty(_options.ClientId))
			query.Add($"{RemoteProtocol.Notifications.ClientIdQuery}={Uri.EscapeDataString(_options.ClientId)}");
		var url = query.Count > 0 ? $"{_endpoint}?{string.Join('&', query)}" : _endpoint.ToString();

		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventStreamContentType));
		request.Headers.Add(RemoteProtocol.TokenHeader, _token);
		if (resume)
			request.Headers.Add(RemoteProtocol.Sse.LastEventIdHeader, lastSeq.ToString(System.Globalization.CultureInfo.InvariantCulture));

		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
		ThrowIfAuthRejected(response);
		response.EnsureSuccessStatusCode();

		onHeaders();

		await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		using var reader = new StreamReader(stream, Encoding.UTF8);

		await foreach (var frame in SseFrameReader.ReadAsync(reader, onActivity, ct).ConfigureAwait(false))
		{
			try
			{
				await HandleFrameAsync(frame, ct).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is InvalidOperationException or FormatException)
			{
				// A frame of the wrong shape (non-string method, primitive payload): skip it and move past it, or a reconnect would replay it forever.
				SkipFrame(frame, ex);
			}
		}

		throw new IOException("The notification stream was closed by the daemon.");
	}

	private void SkipFrame(SseFrame frame, Exception error)
	{
		lock (_gate)
		{
			if (frame.Id > _lastSeq)
				_lastSeq = frame.Id;
		}

		if (Interlocked.Exchange(ref _skippedFrameReported, 1) == 0)
			System.Diagnostics.Trace.TraceWarning($"TessynClient: skipped a malformed notification frame ({error.Message}); further ones are skipped silently.");
	}

	/// <summary>Dispatches one frame: a resync to <see cref="Resynced"/>, anything else to <see cref="NotificationReceived"/>.</summary>
	private async Task HandleFrameAsync(SseFrame frame, CancellationToken ct)
	{
		JsonNode? json;
		try
		{
			json = JsonNode.Parse(frame.Data);
		}
		catch (JsonException)
		{
			return; // A malformed frame is skipped, never fatal.
		}

		var method = json?["method"]?.GetValue<string>();
		if (json == null || method == null)
			return;

		var frameParams = json["params"];

		if (method == RemoteProtocol.Events.Resync)
		{
			var reason = frameParams?["reason"]?.GetValue<string>() ?? RemoteProtocol.ResyncReasons.Gap;
			var head   = frameParams?["eventSeq"]?.GetValue<long>() ?? frame.Id;

			// unknown-seq means another daemon run: its seqs restart, so the old high-water mark must not survive.
			lock (_gate)
				_lastSeq = reason == RemoteProtocol.ResyncReasons.UnknownSeq ? head : Math.Max(_lastSeq, head);

			Raise(() => Resynced?.Invoke(this, new ResyncedEventArgs(reason, head)));

			if (reason == RemoteProtocol.ResyncReasons.UnknownSeq)
				await LearnRunIdAsync(ct).ConfigureAwait(false);
			return;
		}

		lock (_gate)
		{
			if (frame.Id > 0)
			{
				if (frame.Id <= _lastSeq)
					return; // Already delivered (replay overlap).

				_lastSeq = frame.Id;
			}
		}

		var notification = new RemoteNotification(method, frameParams?.DeepClone() ?? new JsonObject(), frame.Id);
		Raise(() => NotificationReceived?.Invoke(this, notification));
	}

	/// <summary>After an unknown-seq resync the daemon may be a new run: take its run id so the next resume names it.</summary>
	private async Task LearnRunIdAsync(CancellationToken ct)
	{
		var hello = await HandshakeAsync(ct).ConfigureAwait(false);
		lock (_gate)
			_runId = hello.RunId;
	}

	// ── JSON-RPC ──────────────────────────────────────────────────────────────────

	private async Task<T> CallAsync<T>(string method, JsonObject? parameters, CancellationToken cancellationToken, TimeSpan? requestTimeout = null)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		var id = Interlocked.Increment(ref _nextRequestId);
		var envelope = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id,
			["method"]  = method,
		};
		if (parameters != null)
			envelope["params"] = parameters;

		using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
		{
			Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, JsonContentType),
		};
		request.Headers.Add(RemoteProtocol.TokenHeader, _token);

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var limit = requestTimeout ?? _options.RequestTimeout;
		timeout.CancelAfter(limit);

		string body;
		HttpStatusCode status;
		try
		{
			using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
			ThrowIfAuthRejected(response);
			status = response.StatusCode;
			body   = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"{method} did not answer within {limit.TotalSeconds:0.##} s.");
		}

		return ParseResponse<T>(method, status, body);
	}

	private static T ParseResponse<T>(string method, HttpStatusCode status, string body)
	{
		JsonNode? json = null;
		try
		{
			json = JsonNode.Parse(body);
		}
		catch (JsonException)
		{
			// Handled below: a non-JSON body is a transport failure.
		}

		if (json is JsonObject envelope)
		{
			if (envelope["error"] is JsonObject error)
			{
				var code    = error["code"]?.GetValue<int>() ?? RemoteProtocol.ErrorCodes.InternalError;
				var message = error["message"]?.GetValue<string>() ?? "Unknown error";
				throw new TessynRpcException(code, message);
			}

			if (envelope["result"] is { } result)
			{
				return result.Deserialize<T>(TdcpJsonOptions.Default)
					?? throw new TessynRpcException(RemoteProtocol.ErrorCodes.InternalError, $"{method} returned an empty result.");
			}
		}

		throw new HttpRequestException($"{method}: unexpected answer (HTTP {(int)status}).", inner: null, status);
	}

	private static void ThrowIfAuthRejected(HttpResponseMessage response)
	{
		if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
			throw new TessynAuthException((int)response.StatusCode, $"The daemon rejected the request (HTTP {(int)response.StatusCode}).");
	}

	private static JsonObject Params(params (string Name, JsonNode? Value)[] values)
	{
		var result = new JsonObject();
		foreach (var (name, value) in values)
		{
			if (value != null)
				result[name] = value;
		}

		return result;
	}

	private static JsonNode? Node(string? value) => value == null ? null : JsonValue.Create(value);
	private static JsonNode? Node(bool? value) => value == null ? null : JsonValue.Create(value.Value);
	private static JsonNode? Node(int? value) => value == null ? null : JsonValue.Create(value.Value);
	private static JsonNode? Node(long? value) => value == null ? null : JsonValue.Create(value.Value);

	// ── remote methods ────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public Task<HelloResult> HelloAsync(CancellationToken cancellationToken = default) =>
		CallAsync<HelloResult>(
			RemoteProtocol.Methods.ClientHello,
			Params(
				("clientName", Node(_options.ClientName)),
				("clientVersion", Node(_options.ClientVersion)),
				("protocolVersion", Node(RemoteProtocol.ProtocolVersion)),
				("capabilities", new JsonArray(
					JsonValue.Create(RemoteProtocol.Capabilities.EventsLocal),
					JsonValue.Create(RemoteProtocol.Capabilities.EventsResume))),
				("clientId", Node(HelloPresents().Count > 0 ? _options.ClientId : null)),
				("presents", HelloPresents().Count > 0 ? new JsonArray([.. HelloPresents().Select(p => (JsonNode?)JsonValue.Create(p))]) : null)),
			cancellationToken);

	/// <inheritdoc />
	public Task<TreeSnapshotResult> TreeSnapshotAsync(long? sinceVersion = null, CancellationToken cancellationToken = default) =>
		CallAsync<TreeSnapshotResult>(RemoteProtocol.Methods.TreeSnapshot, Params(("sinceVersion", Node(sinceVersion))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeWorkersResult> TreeWorkersAsync(string supervisorId, CancellationToken cancellationToken = default) =>
		CallAsync<TreeWorkersResult>(RemoteProtocol.Methods.TreeWorkers, Params(("supervisorId", Node(supervisorId))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeSearchResult> TreeSearchAsync(string query, bool? exact = null, string? scopeNodeId = null, CancellationToken cancellationToken = default) =>
		CallAsync<TreeSearchResult>(
			RemoteProtocol.Methods.TreeSearch,
			Params(("query", Node(query)), ("exact", Node(exact)), ("scopeNodeId", Node(scopeNodeId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<CreateSessionResult> TreeCreateSessionAsync(string anchorId, string name, CancellationToken cancellationToken = default) =>
		CallAsync<CreateSessionResult>(
			RemoteProtocol.Methods.TreeCreateSession,
			Params(("anchorId", Node(anchorId)), ("name", Node(name))),
			cancellationToken);

	/// <inheritdoc />
	public Task<RecentListResult> RecentListAsync(CancellationToken cancellationToken = default) =>
		CallAsync<RecentListResult>(RemoteProtocol.Methods.RecentList, null, cancellationToken);

	/// <inheritdoc />
	public Task<RecentHideResult> RecentHideAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<RecentHideResult>(RemoteProtocol.Methods.RecentHide, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<TagSearchResult> TagsSearchAsync(string text, IReadOnlyList<TagRefDto> selected, string? scopeNodeId = null, CancellationToken cancellationToken = default) =>
		CallAsync<TagSearchResult>(
			RemoteProtocol.Methods.TagsSearch,
			Params(
				("text", Node(text)),
				("scopeNodeId", Node(scopeNodeId)),
				("selected", JsonSerializer.SerializeToNode(selected, TdcpJsonOptions.Default))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionDetailDto> SessionGetAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<SessionDetailDto>(RemoteProtocol.Methods.SessionGet, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, int? beforeIndex = null, int? afterIndex = null, int? limit = null, CancellationToken cancellationToken = default) =>
		CallAsync<SessionEntriesResult>(
			RemoteProtocol.Methods.SessionEntries,
			Params(("nodeId", Node(nodeId)), ("beforeIndex", Node(beforeIndex)), ("afterIndex", Node(afterIndex)), ("limit", Node(limit))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionEntryDto> SessionEntryAsync(string nodeId, int index, int epoch, CancellationToken cancellationToken = default) =>
		CallAsync<SessionEntryDto>(
			RemoteProtocol.Methods.SessionEntry,
			Params(("nodeId", Node(nodeId)), ("index", Node(index)), ("epoch", Node(epoch))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionSendResult> SessionSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default) =>
		CallAsync<SessionSendResult>(
			RemoteProtocol.Methods.SessionSend,
			Params(("nodeId", Node(nodeId)), ("text", Node(text)), ("modelId", Node(modelId)), ("effort", Node(effort)), ("profileId", Node(profileId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionStopResult> SessionStopAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<SessionStopResult>(RemoteProtocol.Methods.SessionStop, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<SessionCompactResult> SessionCompactAsync(string nodeId, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default) =>
		CallAsync<SessionCompactResult>(
			RemoteProtocol.Methods.SessionCompact,
			Params(("nodeId", Node(nodeId)), ("modelId", Node(modelId)), ("effort", Node(effort)), ("profileId", Node(profileId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SetFavoriteResult> SessionSetFavoriteAsync(string nodeId, bool value, CancellationToken cancellationToken = default) =>
		CallAsync<SetFavoriteResult>(
			RemoteProtocol.Methods.SessionSetFavorite,
			Params(("nodeId", Node(nodeId)), ("value", Node(value))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SetStreamingResult> SessionSetStreamingAsync(string nodeId, bool enabled, bool? force = null, CancellationToken cancellationToken = default) =>
		CallAsync<SetStreamingResult>(
			RemoteProtocol.Methods.SessionSetStreaming,
			Params(("nodeId", Node(nodeId)), ("enabled", Node(enabled)), ("force", Node(force))),
			cancellationToken);

	/// <inheritdoc />
	public Task<AttentionListResult> AttentionListAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<AttentionListResult>(RemoteProtocol.Methods.AttentionList, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<AttentionReplyResult> AttentionReplyAsync(string nodeId, IReadOnlyList<AttentionAnswerParams> answers, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default) =>
		CallAsync<AttentionReplyResult>(
			RemoteProtocol.Methods.AttentionReply,
			Params(
				("nodeId", Node(nodeId)),
				("answers", JsonSerializer.SerializeToNode(answers, TdcpJsonOptions.Default)),
				("modelId", Node(modelId)),
				("effort", Node(effort)),
				("profileId", Node(profileId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<AttentionDismissResult> AttentionDismissAsync(string nodeId, string itemId, CancellationToken cancellationToken = default) =>
		CallAsync<AttentionDismissResult>(
			RemoteProtocol.Methods.AttentionDismiss,
			Params(("nodeId", Node(nodeId)), ("itemId", Node(itemId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<CatalogResult> CatalogGetAsync(CancellationToken cancellationToken = default) =>
		CallAsync<CatalogResult>(RemoteProtocol.Methods.CatalogGet, null, cancellationToken);

	/// <inheritdoc />
	public Task<UsageResult> UsageGetAsync(string? profileId = null, CancellationToken cancellationToken = default) =>
		CallAsync<UsageResult>(RemoteProtocol.Methods.UsageGet, Params(("profileId", Node(profileId))), cancellationToken);

	// ── P1.F / P1.G (additive; grouped at the end) ───────────────────────────────────────────

	/// <inheritdoc />
	public Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, SessionEntriesQuery query, CancellationToken cancellationToken = default) =>
		CallAsync<SessionEntriesResult>(
			RemoteProtocol.Methods.SessionEntries,
			Params(
				("nodeId", Node(nodeId)),
				("beforeIndex", Node(query.BeforeIndex)),
				("afterIndex", Node(query.AfterIndex)),
				("afterSeq", Node(query.AfterSeq)),
				("beforeEntryId", Node(query.BeforeEntryId)),
				("aroundEntryId", Node(query.AroundEntryId)),
				("fileId", Node(query.FileId)),
				("limit", Node(query.Limit))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionEntryDto> SessionEntryByIdAsync(string nodeId, string entryId, EntryRangeParams? range = null, CancellationToken cancellationToken = default) =>
		CallAsync<SessionEntryDto>(
			RemoteProtocol.Methods.SessionEntry,
			Params(
				("nodeId", Node(nodeId)),
				("entryId", Node(entryId)),
				("range", range == null ? null : Params(("offset", Node(range.Offset)), ("length", Node(range.Length))))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionHeadersResult> SessionHeadersAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default) =>
		CallAsync<SessionHeadersResult>(
			RemoteProtocol.LoopbackMethods.SessionHeaders,
			Params(("nodeIds", new JsonArray([.. nodeIds.Select(id => (JsonNode?)JsonValue.Create(id))]))),
			cancellationToken);

	/// <inheritdoc />
	public Task<FileReadResult> FileReadAsync(string handle, long? offset = null, int? length = null, CancellationToken cancellationToken = default) =>
		CallAsync<FileReadResult>(
			RemoteProtocol.Methods.FileRead,
			Params(("handle", Node(handle)), ("offset", Node(offset)), ("length", Node(length))),
			cancellationToken);

	/// <inheritdoc />
	public Task<FilePreviewResult> FilePreviewAsync(string nodeId, string entryId, string path, CancellationToken cancellationToken = default) =>
		CallAsync<FilePreviewResult>(
			RemoteProtocol.Methods.FilePreview,
			Params(("nodeId", Node(nodeId)), ("entryId", Node(entryId)), ("path", Node(path))),
			cancellationToken);

	/// <inheritdoc />
	public Task<FilePreviewResult> FilePreviewForItemAsync(string nodeId, string itemId, string path, CancellationToken cancellationToken = default) =>
		CallAsync<FilePreviewResult>(
			RemoteProtocol.Methods.FilePreview,
			Params(("nodeId", Node(nodeId)), ("itemId", Node(itemId)), ("path", Node(path))),
			cancellationToken);

	/// <inheritdoc />
	public Task<AttachmentDto> AttachmentImportAsync(string nodeId, string sourcePath, CancellationToken cancellationToken = default) =>
		CallAsync<AttachmentDto>(
			RemoteProtocol.LoopbackMethods.AttachmentImport,
			Params(("nodeId", Node(nodeId)), ("sourcePath", Node(sourcePath))),
			cancellationToken,
			_options.AttachmentImportTimeout);

	/// <inheritdoc />
	public Task<AttachmentListResult> AttachmentListAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<AttachmentListResult>(
			RemoteProtocol.Methods.AttachmentList,
			Params(("nodeId", Node(nodeId))),
			cancellationToken);
}
