using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Codex;

/// <summary>
/// JSON-RPC 2.0 over a pair of streams, as <c>codex app-server</c> speaks it on stdio (FR.33.6): no <c>jsonrpc</c> member, one message per
/// line, and <b>no line-length limit</b> other than memory (a resumed thread can be one very large line, openai/codex issue 40362).
/// <list type="bullet">
/// <item>Requests are correlated with their responses by a numeric id; a cancelled call stops waiting and a late response is dropped.</item>
/// <item>Notifications are raised on <see cref="NotificationReceived"/> in stream order, on the read loop (handlers must be quick).</item>
/// <item>Server requests go to <see cref="ServerRequestHandler"/>, off the read loop, and every one is answered; with no handler, or
/// when it throws, the answer is a JSON-RPC error.</item>
/// <item>The end of the input stream (the process exited or crashed) fails every pending call with
/// <see cref="CodexConnectionLostException"/> and completes <see cref="Closed"/>.</item>
/// </list>
/// The connection does not own the streams.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexJsonRpcConnection : IAsyncDisposable
{
	private static readonly ILogger _log = Log.ForContext<CodexJsonRpcConnection>();

	private readonly Stream                                       _input;
	private readonly Stream                                       _output;
	private readonly ConcurrentDictionary<long, PendingCall>      _pending   = new();
	private readonly SemaphoreSlim                                _writeLock = new(1, 1);
	private readonly CancellationTokenSource                      _lifetime  = new();
	private readonly TaskCompletionSource<Exception?>             _closed    = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly JsonElement                                  _emptyParams;
	private long                                                  _nextId;
	private int                                                   _closedFlag;
	private int                                                   _started;

	public CodexJsonRpcConnection(Stream input, Stream output, JsonSerializerOptions? serializerOptions = null)
	{
		_input            = input;
		_output           = output;
		SerializerOptions = serializerOptions ?? Constants.Codex.SerializerOptions;
		using var empty   = JsonDocument.Parse("{}");
		_emptyParams      = empty.RootElement.Clone();
	}

	public JsonSerializerOptions SerializerOptions { get; }

	/// <summary>Raised for every notification, in the order the lines arrived.</summary>
	public event Action<CodexNotification>? NotificationReceived;

	/// <summary>Answers server requests. Unset, every server request gets <c>-32601</c>.</summary>
	public Func<CodexServerRequest, CancellationToken, Task<CodexServerReply>>? ServerRequestHandler { get; set; }

	/// <summary>Completes when the input stream ends or fails; the result is the read fault, or null for a clean end of stream.</summary>
	public Task<Exception?> Closed => _closed.Task;

	public bool IsClosed => Volatile.Read(ref _closedFlag) == 1;

	/// <summary>Starts the read loop. Call once, after the handlers are attached.</summary>
	public void Start()
	{
		if (Interlocked.Exchange(ref _started, 1) == 1)
			throw new InvalidOperationException("The connection is already started.");

		_ = Task.Run(ReadLoopAsync);
	}

	/// <summary>Sends a request and deserializes its <c>result</c>.</summary>
	/// <exception cref="CodexRpcException">The server answered with an error.</exception>
	/// <exception cref="CodexConnectionLostException">The stream ended before the answer.</exception>
	public async Task<TResult> SendRequestAsync<TResult>(string method, object? parameters, CancellationToken cancellationToken = default)
	{
		var result = await SendRequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
		return result.Deserialize<TResult>(SerializerOptions)
			?? throw new CodexRpcException(method, Constants.Codex.Rpc.InternalError, $"The result of {method} was null.");
	}

	/// <summary>Sends a request and returns its raw <c>result</c> (a detached clone).</summary>
	public async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfClosed(method);

		var id   = Interlocked.Increment(ref _nextId);
		var call = new PendingCall(method);
		_pending[id] = call;

		// The read loop may have closed between the check above and the add: fail this call the same way.
		if (IsClosed && _pending.TryRemove(id, out _))
			throw LostException(method);

		using var registration = cancellationToken.Register(() =>
		{
			if (_pending.TryRemove(id, out var cancelled))
				cancelled.Completion.TrySetCanceled(cancellationToken);
		});

		try
		{
			await WriteMessageAsync(writer =>
			{
				writer.WriteNumber(Constants.Codex.Rpc.Id, id);
				writer.WriteString(Constants.Codex.Rpc.Method, method);
				WriteParams(writer, parameters);
			}).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			_pending.TryRemove(id, out _);
			throw LostException(method, ex);
		}

		return await call.Completion.Task.ConfigureAwait(false);
	}

	/// <summary>Sends a notification (no answer expected).</summary>
	public async Task SendNotificationAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfClosed(method);

		try
		{
			await WriteMessageAsync(writer =>
			{
				writer.WriteString(Constants.Codex.Rpc.Method, method);
				WriteParams(writer, parameters);
			}).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			throw LostException(method, ex);
		}
	}

	public ValueTask DisposeAsync()
	{
		_lifetime.Cancel();
		Close(null);
		return ValueTask.CompletedTask;
	}

	private async Task ReadLoopAsync()
	{
		var       chunk = new byte[Constants.Codex.ReadChunkBytes];
		var       line  = new MemoryStream();
		Exception? fault = null;

		try
		{
			while (true)
			{
				var read = await _input.ReadAsync(chunk.AsMemory(), _lifetime.Token).ConfigureAwait(false);
				if (read == 0)
					break;

				var span = chunk.AsSpan(0, read);
				while (true)
				{
					var newline = span.IndexOf((byte)'\n');
					if (newline < 0)
					{
						line.Write(span);
						break;
					}

					line.Write(span[..newline]);
					span = span[(newline + 1)..];
					DispatchLine(line);
					line = ResetLineBuffer(line);
				}
			}

			// A last message without a trailing newline still counts.
			if (line.Length > 0)
				DispatchLine(line);
		}
		catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			fault = ex;
			_log.Warning(ex, "Codex app-server stream read failed");
		}
		finally
		{
			Close(fault);
		}
	}

	private static MemoryStream ResetLineBuffer(MemoryStream line)
	{
		if (line.Capacity > Constants.Codex.LineBufferRetainBytes)
			return new MemoryStream();

		line.SetLength(0);
		return line;
	}

	private void DispatchLine(MemoryStream line)
	{
		var length = (int)line.Length;
		var bytes  = line.GetBuffer().AsMemory(0, length);

		// Tolerate CRLF line ends and blank lines.
		if (length > 0 && bytes.Span[length - 1] == (byte)'\r')
			bytes = bytes[..(length - 1)];
		if (bytes.Span.Trim((byte)' ').IsEmpty)
			return;

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(bytes);
		}
		catch (JsonException ex)
		{
			_log.Warning(ex, "Codex app-server sent a line that is not JSON ({Length} bytes); it is skipped", bytes.Length);
			return;
		}

		using (document)
			Dispatch(document.RootElement);
	}

	private void Dispatch(JsonElement message)
	{
		if (message.ValueKind != JsonValueKind.Object)
		{
			_log.Warning("Codex app-server sent a JSON {Kind} where a message object was expected; it is skipped", message.ValueKind);
			return;
		}

		var hasId     = message.TryGetProperty(Constants.Codex.Rpc.Id, out var id) && id.ValueKind != JsonValueKind.Null;
		var hasMethod = message.TryGetProperty(Constants.Codex.Rpc.Method, out var methodElement) && methodElement.ValueKind == JsonValueKind.String;

		if (hasMethod)
		{
			var method     = methodElement.GetString()!;
			var parameters = message.TryGetProperty(Constants.Codex.Rpc.Params, out var p) && p.ValueKind != JsonValueKind.Null ? p.Clone() : _emptyParams;

			if (hasId)
			{
				var request = new CodexServerRequest(id.Clone(), method, parameters);
				_ = Task.Run(() => AnswerServerRequestAsync(request));
				return;
			}

			RaiseNotification(new CodexNotification(method, parameters));
			return;
		}

		if (hasId)
		{
			CompleteResponse(id, message);
			return;
		}

		_log.Warning("Codex app-server sent a message with neither method nor id; it is skipped");
	}

	private void RaiseNotification(CodexNotification notification)
	{
		try
		{
			NotificationReceived?.Invoke(notification);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "A handler of Codex notification {Method} failed", notification.Method);
		}
	}

	private void CompleteResponse(JsonElement idElement, JsonElement message)
	{
		long id;
		if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var number))
			id = number;
		else if (idElement.ValueKind == JsonValueKind.String && long.TryParse(idElement.GetString(), out var parsed))
			id = parsed;
		else
		{
			_log.Warning("Codex app-server answered with an id Tessyn never sent ({Id}); it is skipped", idElement.GetRawText());
			return;
		}

		if (!_pending.TryRemove(id, out var call))
		{
			_log.Debug("Codex app-server answered request {Id}, which is no longer waited for", id);
			return;
		}

		if (message.TryGetProperty(Constants.Codex.Rpc.Error, out var error) && error.ValueKind == JsonValueKind.Object)
		{
			var code = error.TryGetProperty(Constants.Codex.Rpc.Code, out var c) && c.TryGetInt32(out var parsedCode) ? parsedCode : Constants.Codex.Rpc.InternalError;
			var text = error.TryGetProperty(Constants.Codex.Rpc.Message, out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : string.Empty;
			JsonElement? data = error.TryGetProperty(Constants.Codex.Rpc.Data, out var d) && d.ValueKind != JsonValueKind.Null ? d.Clone() : null;
			call.Completion.TrySetException(new CodexRpcException(call.Method, code, text, data));
			return;
		}

		var result = message.TryGetProperty(Constants.Codex.Rpc.Result, out var r) ? r.Clone() : _emptyParams;
		call.Completion.TrySetResult(result);
	}

	private async Task AnswerServerRequestAsync(CodexServerRequest request)
	{
		CodexServerReply reply;
		var handler = ServerRequestHandler;
		if (handler is null)
			reply = CodexServerReply.Failure(Constants.Codex.Rpc.MethodNotFound, string.Format(Constants.Codex.Texts.UnsupportedServerRequestFormat, request.Method));
		else
		{
			try
			{
				reply = await handler(request, _lifetime.Token).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_log.Error(ex, "Answering Codex server request {Method} failed", request.Method);
				reply = CodexServerReply.Failure(Constants.Codex.Rpc.InternalError, ex.Message);
			}
		}

		try
		{
			await WriteMessageAsync(writer =>
			{
				writer.WritePropertyName(Constants.Codex.Rpc.Id);
				request.Id.WriteTo(writer);

				if (reply.Error is not null)
				{
					writer.WriteStartObject(Constants.Codex.Rpc.Error);
					writer.WriteNumber(Constants.Codex.Rpc.Code, reply.Error.Code);
					writer.WriteString(Constants.Codex.Rpc.Message, reply.Error.Message);
					writer.WriteEndObject();
					return;
				}

				writer.WritePropertyName(Constants.Codex.Rpc.Result);
				if (reply.Result is null)
					writer.WriteNullValue();
				else
					JsonSerializer.Serialize(writer, reply.Result, reply.Result.GetType(), SerializerOptions);
			}).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			_log.Warning(ex, "Could not answer Codex server request {Method}: the stream is closed", request.Method);
		}
	}

	// A method without params gets no params member: Codex reads those as unit params, which an empty object would not match
	// (the spike sent account/rateLimits/read with no params).
	private void WriteParams(Utf8JsonWriter writer, object? parameters)
	{
		if (parameters is null)
			return;

		writer.WritePropertyName(Constants.Codex.Rpc.Params);
		JsonSerializer.Serialize(writer, parameters, parameters.GetType(), SerializerOptions);
	}

	private async Task WriteMessageAsync(Action<Utf8JsonWriter> writeMembers)
	{
		var buffer = new ArrayBufferWriter<byte>();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writeMembers(writer);
			writer.WriteEndObject();
		}
		buffer.Write("\n"u8);

		await _writeLock.WaitAsync().ConfigureAwait(false);
		try
		{
			await _output.WriteAsync(buffer.WrittenMemory).ConfigureAwait(false);
			await _output.FlushAsync().ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private void Close(Exception? fault)
	{
		if (Interlocked.Exchange(ref _closedFlag, 1) == 1)
			return;

		foreach (var id in _pending.Keys)
		{
			if (_pending.TryRemove(id, out var call))
				call.Completion.TrySetException(LostException(call.Method, fault));
		}

		_closed.TrySetResult(fault);
	}

	private void ThrowIfClosed(string method)
	{
		if (IsClosed)
			throw LostException(method);
	}

	private static CodexConnectionLostException LostException(string method, Exception? inner = null) =>
		new($"The Codex app-server connection is closed ({method} got no answer).", inner);

	private sealed record PendingCall(string Method)
	{
		public TaskCompletionSource<JsonElement> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
