using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Dials out to the web server (contract section 6): one long-lived SSE GET carries JSON-RPC requests down, replies and
/// event batches are POSTed back. Lifecycle follows <see cref="TelegramBridgeService"/> (Start/Stop, loop on the thread
/// pool) but <see cref="Stop"/> waits for the loop. Nothing here may throw into the app: every failure becomes the
/// <see cref="RelayState.Error"/> state plus a backed-off retry. Never logs tokens or payloads.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayConnectorService : IRelayConnectorService, IRelayBlobClient
{
	private static readonly ILogger _log = Log.ForContext<RelayConnectorService>();

	private static readonly TimeSpan _workerDrainTimeout = TimeSpan.FromSeconds(2);

	private readonly IRelayConfigStore _store;
	private readonly TdcpDispatcher _dispatcher;
	private readonly IRemoteEventBus _bus;
	private readonly IEventLog? _eventLog;
	private readonly IRelayHttpClientFactory _httpFactory;
	private readonly RelayConnectorTimings _timings;

	private readonly object _gate = new();
	private readonly object _statusGate = new();
	private CancellationTokenSource? _cts;
	private Task? _loop;
	/// <summary>The client and base URL of the live connection (null between connections); the blob download reuses its auth.</summary>
	private volatile BlobLink? _blobLink;

	private sealed record BlobLink(HttpClient Client, string BaseUrl);

	private RelayStatusModel _status = new(RelayState.Disabled, DateTimeOffset.UtcNow);

	public RelayConnectorService(
		IRelayConfigStore store,
		TdcpDispatcher dispatcher,
		IRemoteEventBus bus,
		IRelayHttpClientFactory httpFactory,
		RelayConnectorTimings? timings = null,
		IEventLog? eventLog = null)
	{
		_store = store;
		_dispatcher = dispatcher;
		_bus = bus;
		_eventLog = eventLog;
		_httpFactory = httpFactory;
		_timings = timings ?? RelayConnectorTimings.Default;
	}

	public RelayStatusModel Status
	{
		get { lock (_statusGate) return _status; }
	}

	public event Action<RelayStatusModel>? StatusChanged;

	public void Start()
	{
		try
		{
			lock (_gate)
			{
				if (_cts is not null)
					return;

				var config = _store.Load();
				if (config is null || !config.Enabled || string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.Token))
				{
					SetStatus(RelayState.Disabled);
					return;
				}

				if (!Uri.TryCreate(config.Url.Trim(), UriKind.Absolute, out var uri)
				    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
				{
					SetStatus(RelayState.Error, "Invalid web server URL");
					return;
				}

				_cts = new CancellationTokenSource();
				var ct = _cts.Token;
				SetStatus(RelayState.Connecting);
				_loop = Task.Run(() => RunAsync(config, ct));
			}
		}
		catch (Exception ex)
		{
			_log.Error(ex, "RelayConnectorService: Start failed");
		}
	}

	public void Stop()
	{
		try
		{
			CancellationTokenSource? cts;
			Task? loop;
			lock (_gate)
			{
				cts = _cts;
				loop = _loop;
				_cts = null;
				_loop = null;
			}

			if (cts is null)
				return;

			cts.Cancel();
			var finished = true;
			try
			{
				finished = loop?.Wait(Constants.Remote.Link.StopTimeout) ?? true;
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "RelayConnectorService: loop ended with an error during Stop");
			}

			if (finished)
				cts.Dispose();
			SetStatus(RelayState.Disabled);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "RelayConnectorService: Stop failed");
		}
	}

	public void Restart()
	{
		Stop();
		Start();
	}

	private async Task RunAsync(RelayConfigModel config, CancellationToken ct)
	{
		var backoff = _timings.ReconnectBackoff;
		var attempt = 0;

		while (!ct.IsCancellationRequested)
		{
			var connectedFor = TimeSpan.Zero;
			var unauthorized = false;
			try
			{
				(connectedFor, unauthorized) = await ConnectOnceAsync(config, ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "RelayConnectorService: unexpected connection failure");
				SetStatus(RelayState.Error, Describe(ex));
			}

			if (ct.IsCancellationRequested)
				break;

			if (connectedFor >= _timings.BackoffResetAfter)
				attempt = 0;
			if (unauthorized)
				attempt = backoff.Length - 1;

			var delay = backoff[Math.Min(attempt, backoff.Length - 1)];
			attempt++;
			try
			{
				await Task.Delay(delay, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	/// <summary>One connection from the SSE GET to its end. Returns how long it was connected and whether the token was rejected.</summary>
	private async Task<(TimeSpan ConnectedFor, bool Unauthorized)> ConnectOnceAsync(RelayConfigModel config, CancellationToken ct)
	{
		SetStatus(RelayState.Connecting);

		var baseUrl = config.Url.Trim().TrimEnd('/');
		var elapsed = new Stopwatch();
		using var client = _httpFactory.Create();
		var blobLink = new BlobLink(client, baseUrl);
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Token.Trim());
		client.DefaultRequestHeaders.TryAddWithoutValidation(Constants.Remote.Link.DaemonHeader, Environment.MachineName);
		client.DefaultRequestHeaders.TryAddWithoutValidation(Constants.Remote.Link.ProtocolHeader, Constants.Remote.Link.ProtocolHeaderValue);

		using var conn = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var workers = new List<Task>();
		Channel<JsonNode>? requests = null;
		Channel<RemoteNotification>? events = null;
		Action<RemoteNotification>? handler = null;

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + Constants.Remote.Link.StreamPath);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, conn.Token);

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				SetStatus(RelayState.Error, Constants.Remote.Link.TokenRejectedMessage);
				return (TimeSpan.Zero, true);
			}
			response.EnsureSuccessStatusCode();

			SetStatus(RelayState.Connected);
			elapsed.Start();
			_blobLink = blobLink;

			requests = Channel.CreateUnbounded<JsonNode>(new UnboundedChannelOptions { SingleWriter = true });
			events = Channel.CreateBounded<RemoteNotification>(new BoundedChannelOptions(Constants.Remote.Link.EventQueueCapacity)
			{
				FullMode = BoundedChannelFullMode.DropOldest,
				SingleReader = true,
			});

			var requestReader = requests.Reader;
			var eventReader = events.Reader;
			for (var i = 0; i < Constants.Remote.Link.DispatchConcurrency; i++)
				workers.Add(Task.Run(() => RequestWorkerAsync(client, baseUrl, requestReader, conn.Token)));
			workers.Add(Task.Run(() => EventPumpAsync(client, baseUrl, eventReader, conn.Token)));

			// Events are only relayed while connected; the handler just enqueues (the bus requires O(1) subscribers).
			var eventWriter = events.Writer;
			// entries.appended is local-SSE only: the web ignores it, and it can carry up to 64 KiB per window.
			handler = n =>
			{
				if (n.Method != Constants.Remote.Events.EntriesAppended)
					eventWriter.TryWrite(n);
			};
			// With an event log the relay forwards the stamped notifications (additive "seq" per event); without one the bus ones.
			if (_eventLog is not null)
				_eventLog.Appended += handler;
			else
				_bus.Published += handler;

			await ReadStreamAsync(response, requests.Writer, conn.Token);

			if (!ct.IsCancellationRequested)
				SetStatus(RelayState.Error, "Link closed by the server");
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// Stop() — not an error.
		}
		catch (Exception ex)
		{
			if (!ct.IsCancellationRequested)
			{
				_log.Warning(ex, "RelayConnectorService: link failed");
				SetStatus(RelayState.Error, Describe(ex));
			}
		}
		finally
		{
			Interlocked.CompareExchange(ref _blobLink, null, blobLink);
			if (handler is not null)
			{
				if (_eventLog is not null)
					_eventLog.Appended -= handler;
				else
					_bus.Published -= handler;
			}

			try { conn.Cancel(); } catch { /* already disposed/cancelled */ }
			requests?.Writer.TryComplete();
			events?.Writer.TryComplete();

			if (workers.Count > 0)
			{
				try
				{
					await Task.WhenAny(Task.WhenAll(workers), Task.Delay(_workerDrainTimeout));
				}
				catch
				{
					// Workers log their own failures.
				}
			}
		}

		return (elapsed.Elapsed, false);
	}

	/// <summary>Reads SSE frames until the stream ends; throws <see cref="TimeoutException"/> when nothing (not even a ping) arrives in time.</summary>
	private async Task ReadStreamAsync(HttpResponseMessage response, ChannelWriter<JsonNode> requests, CancellationToken ct)
	{
		await using var stream = await response.Content.ReadAsStreamAsync(ct);
		using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
		using var dead = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var data = new StringBuilder();

		while (true)
		{
			dead.CancelAfter(_timings.DeadLinkTimeout);

			string? line;
			try
			{
				line = await reader.ReadLineAsync(dead.Token);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested && dead.IsCancellationRequested)
			{
				throw new TimeoutException("No data from the server (link timed out)");
			}

			if (line is null)
				return;

			if (line.Length == 0)
			{
				if (data.Length > 0)
				{
					EnqueueFrame(data.ToString(), requests);
					data.Clear();
				}
				continue;
			}

			// Comment lines (": ping") only prove the link is alive; id:/event:/retry: carry nothing we need.
			if (!line.StartsWith("data:", StringComparison.Ordinal))
				continue;

			var value = line.AsSpan(5);
			if (value.Length > 0 && value[0] == ' ')
				value = value[1..];
			if (data.Length > 0)
				data.Append('\n');
			data.Append(value);
		}
	}

	private static void EnqueueFrame(string json, ChannelWriter<JsonNode> requests)
	{
		JsonNode? node;
		try
		{
			node = JsonNode.Parse(json);
		}
		catch (System.Text.Json.JsonException)
		{
			_log.Debug("RelayConnectorService: ignoring an unparseable frame");
			return;
		}

		// Server-made notifications (link.status, resync) have no id and need no answer.
		if (node is JsonObject obj && obj["id"] is not null && obj["method"] is not null)
			requests.TryWrite(node);
	}

	private async Task RequestWorkerAsync(HttpClient client, string baseUrl, ChannelReader<JsonNode> requests, CancellationToken ct)
	{
		try
		{
			await foreach (var request in requests.ReadAllAsync(ct))
			{
				try
				{
					var reply = await _dispatcher.HandleRequestAsync(request, TdcpTransport.Relay, ct);
					var body = "[" + reply.ToJsonString(TdcpJsonOptions.Default) + "]";
					await PostAsync(client, baseUrl + Constants.Remote.Link.ReplyPath, body, ct);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "RelayConnectorService: could not answer a request");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Connection ended.
		}
	}

	private async Task EventPumpAsync(HttpClient client, string baseUrl, ChannelReader<RemoteNotification> reader, CancellationToken ct)
	{
		try
		{
			while (await reader.WaitToReadAsync(ct))
			{
				await Task.Delay(_timings.EventBatchWindow, ct);

				var batch = new JsonArray();
				while (reader.TryRead(out var notification))
				{
					var item = new JsonObject
					{
						["method"] = notification.Method,
						["params"] = notification.Params.DeepClone(),
					};
					if (notification.Seq > 0)
						item["seq"] = notification.Seq;
					batch.Add(item);
				}

				if (batch.Count == 0)
					continue;

				try
				{
					var body = new JsonObject { ["events"] = batch }.ToJsonString(TdcpJsonOptions.Default);
					await PostAsync(client, baseUrl + Constants.Remote.Link.EventsPath, body, ct);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					// Dropped: browsers resync on the next link.status.
					_log.Warning(ex, "RelayConnectorService: could not post events");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Connection ended.
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "RelayConnectorService: event pump stopped");
		}
	}

	/// <inheritdoc />
	public async Task DownloadBlobAsync(string blobId, System.IO.Stream destination, CancellationToken cancellationToken)
	{
		var link = _blobLink ?? throw new TdcpException(Constants.Attachments.NotConnectedMessage);
		var url  = link.BaseUrl + Constants.Remote.Link.BlobPath + Uri.EscapeDataString(blobId);

		HttpResponseMessage response;
		try
		{
			response = await link.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			throw new TdcpException("Could not reach the web server: " + ex.Message);
		}

		using (response)
		{
			if (response.StatusCode == HttpStatusCode.NotFound)
				throw new TdcpException("The upload is no longer available on the web server");
			if (response.StatusCode == HttpStatusCode.Unauthorized)
				throw new TdcpException(Constants.Remote.Link.TokenRejectedMessage);
			if (!response.IsSuccessStatusCode)
				throw new TdcpException($"The web server answered {(int)response.StatusCode} for the upload");

			await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
			await source.CopyToAsync(destination, Constants.Attachments.CopyBufferBytes, cancellationToken);
		}
	}

	private static async Task PostAsync(HttpClient client, string url, string json, CancellationToken ct)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(Constants.Remote.Link.PostTimeout);
		using var content = new StringContent(json, Encoding.UTF8, "application/json");
		using var response = await client.PostAsync(url, content, timeout.Token);
		if (!response.IsSuccessStatusCode)
			_log.Warning("RelayConnectorService: POST {Path} answered {Status}", new Uri(url).AbsolutePath, (int)response.StatusCode);
	}

	private static string Describe(Exception ex) => ex is HttpRequestException { InnerException: not null } http
		? $"{http.Message} ({http.InnerException!.Message})"
		: ex.Message;

	private void SetStatus(RelayState state, string? error = null)
	{
		RelayStatusModel snapshot;
		lock (_statusGate)
		{
			if (_status.State == state && _status.LastError == error)
				return;
			snapshot = new RelayStatusModel(state, DateTimeOffset.UtcNow, error);
			_status = snapshot;
		}

		if (error is null)
			_log.Information("Relay link state: {State}", state);
		else
			_log.Information("Relay link state: {State} ({Error})", state, error);

		try
		{
			StatusChanged?.Invoke(snapshot);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "RelayConnectorService: a StatusChanged subscriber threw");
		}
	}
}
