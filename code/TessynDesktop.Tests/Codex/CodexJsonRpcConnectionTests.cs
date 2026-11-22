using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Tests.Codex.Fakes;

namespace TessynDesktop.Tests.Codex;

/// <summary>
/// <see cref="CodexJsonRpcConnection"/> on raw in-memory pipes: framing without <c>jsonrpc</c>, correlation, cancellation, notifications,
/// server requests and the end-of-stream signal (FR.33.6, FR.33.8; TC.CX.09).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexJsonRpcConnectionTests : IAsyncDisposable
{
	private readonly FakePipeStream         _serverToClient = new();
	private readonly FakePipeStream         _clientToServer = new();
	private readonly StreamReader           _serverReader;
	private readonly CodexJsonRpcConnection _connection;
	private readonly Eventually             _eventually = new();

	public CodexJsonRpcConnectionTests()
	{
		_serverReader = new StreamReader(_clientToServer, new UTF8Encoding(false));
		_connection   = new CodexJsonRpcConnection(_serverToClient, _clientToServer);
	}

	public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

	[Fact]
	public async Task Request_IsOneLineWithoutJsonRpcMember_AndResultIsCorrelated()
	{
		_connection.Start();
		var call = _connection.SendRequestAsync("model/list", new { includeHidden = false });

		var sent = await ReadSentAsync();
		Assert.False(sent.TryGetProperty("jsonrpc", out _));
		Assert.Equal("model/list", sent.GetProperty("method").GetString());
		Assert.False(sent.GetProperty("params").GetProperty("includeHidden").GetBoolean());

		await ServerSendsAsync($"{{\"id\":{sent.GetProperty("id").GetInt64()},\"result\":{{\"data\":[],\"nextCursor\":null}}}}");
		var result = await call;
		Assert.Equal(JsonValueKind.Array, result.GetProperty("data").ValueKind);
	}

	[Fact]
	public async Task Request_WithNullParams_HasNoParamsMember()
	{
		_connection.Start();
		var call = _connection.SendRequestAsync("account/rateLimits/read", null);

		var sent = await ReadSentAsync();
		Assert.False(sent.TryGetProperty("params", out _));
		await ServerSendsAsync($"{{\"id\":{sent.GetProperty("id").GetInt64()},\"result\":{{}}}}");
		await call;
	}

	[Fact]
	public async Task Responses_OutOfOrder_GoToTheirOwnCalls()
	{
		_connection.Start();
		var first  = _connection.SendRequestAsync("a", null);
		var second = _connection.SendRequestAsync("b", null);
		var idA    = (await ReadSentAsync()).GetProperty("id").GetInt64();
		var idB    = (await ReadSentAsync()).GetProperty("id").GetInt64();

		await ServerSendsAsync($"{{\"id\":{idB},\"result\":{{\"who\":\"b\"}}}}");
		await ServerSendsAsync($"{{\"id\":{idA},\"result\":{{\"who\":\"a\"}}}}");

		Assert.Equal("a", (await first).GetProperty("who").GetString());
		Assert.Equal("b", (await second).GetProperty("who").GetString());
	}

	[Fact]
	public async Task ErrorResponse_ThrowsCodexRpcException_WithCodeAndMessage()
	{
		_connection.Start();
		var call = _connection.SendRequestAsync("thread/resume", new { threadId = "t" });
		var id   = (await ReadSentAsync()).GetProperty("id").GetInt64();

		await ServerSendsAsync($"{{\"error\":{{\"code\":-32600,\"message\":\"no rollout found for thread id t\"}},\"id\":{id}}}");

		var error = await Assert.ThrowsAsync<CodexRpcException>(() => call);
		Assert.Equal(-32600, error.Code);
		Assert.Equal("no rollout found for thread id t", error.Message);
		Assert.Equal("thread/resume", error.Method);
	}

	[Fact]
	public async Task CancelledCall_StopsWaiting_AndALateResponseIsDropped()
	{
		_connection.Start();
		using var cancel = new CancellationTokenSource();
		var call = _connection.SendRequestAsync("slow", null, cancel.Token);
		var id   = (await ReadSentAsync()).GetProperty("id").GetInt64();

		cancel.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

		await ServerSendsAsync($"{{\"id\":{id},\"result\":{{}}}}");
		var next = _connection.SendRequestAsync("next", null);
		var nextId = (await ReadSentAsync()).GetProperty("id").GetInt64();
		await ServerSendsAsync($"{{\"id\":{nextId},\"result\":{{\"ok\":true}}}}");
		Assert.True((await next).GetProperty("ok").GetBoolean());
	}

	[Fact]
	public async Task Notifications_AreRaisedInStreamOrder_WithParamsThatOutliveTheLine()
	{
		var seen = new ConcurrentQueue<CodexNotification>();
		_connection.NotificationReceived += n => seen.Enqueue(n);
		_connection.Start();

		for (var i = 0; i < 50; i++)
			await ServerSendsAsync($"{{\"method\":\"item/agentMessage/delta\",\"params\":{{\"delta\":\"{i}\"}},\"emittedAtMs\":1}}");
		await ServerSendsAsync("{\"method\":\"initialized-ish\"}");

		await _eventually.TrueAsync(() => seen.Count == 51, "51 notifications arrived");
		var deltas = seen.Take(50).Select(n => n.Params.GetProperty("delta").GetString()).ToList();
		Assert.Equal(Enumerable.Range(0, 50).Select(i => i.ToString()), deltas);
		Assert.Equal(JsonValueKind.Object, seen.Last().Params.ValueKind);
	}

	[Fact]
	public async Task CrlfBlankAndNonJsonLines_AreTolerated()
	{
		var seen = new ConcurrentQueue<CodexNotification>();
		_connection.NotificationReceived += n => seen.Enqueue(n);
		_connection.Start();

		await ServerSendsRawAsync("\n\r\nnot json at all\n{\"method\":\"one\",\"params\":{}}\r\n[1,2]\n{\"method\":\"two\"}");
		_serverToClient.CompleteWriting();

		await _eventually.TrueAsync(() => seen.Count == 2, "both notifications arrived (the last one without a newline)");
		Assert.Equal(["one", "two"], seen.Select(n => n.Method));
	}

	[Fact]
	public async Task VeryLargeLine_IsReadWithoutALimit()
	{
		var seen = new ConcurrentQueue<CodexNotification>();
		_connection.NotificationReceived += n => seen.Enqueue(n);
		_connection.Start();

		var blob = new string('x', 8 * 1024 * 1024);
		var line = Encoding.UTF8.GetBytes($"{{\"method\":\"thread/huge\",\"params\":{{\"blob\":\"{blob}\"}}}}\n");
		for (var offset = 0; offset < line.Length; offset += 100_000)
			_serverToClient.Write(line, offset, Math.Min(100_000, line.Length - offset));

		await _eventually.TrueAsync(() => seen.Count == 1, "the 8 MB notification arrived");
		Assert.Equal(blob.Length, seen.Single().Params.GetProperty("blob").GetString()!.Length);
	}

	[Fact]
	public async Task ServerRequest_IsAnsweredWithTheHandlersResult_EchoingTheId()
	{
		_connection.ServerRequestHandler = (request, _) =>
			Task.FromResult(CodexServerReply.Success(new { decision = request.Method == "item/commandExecution/requestApproval" ? "accept" : "no" }));
		_connection.Start();

		await ServerSendsAsync("{\"method\":\"item/commandExecution/requestApproval\",\"id\":\"srv-7\",\"params\":{\"threadId\":\"t\"}}");

		var answer = await ReadSentAsync();
		Assert.Equal("srv-7", answer.GetProperty("id").GetString());
		Assert.Equal("accept", answer.GetProperty("result").GetProperty("decision").GetString());
		Assert.False(answer.TryGetProperty("method", out _));
	}

	[Fact]
	public async Task ServerRequest_WithoutAHandler_GetsMethodNotFound()
	{
		_connection.Start();
		await ServerSendsAsync("{\"method\":\"item/tool/requestUserInput\",\"id\":12,\"params\":{}}");

		var answer = await ReadSentAsync();
		Assert.Equal(12, answer.GetProperty("id").GetInt32());
		Assert.Equal(-32601, answer.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal("Tessyn does not support item/tool/requestUserInput", answer.GetProperty("error").GetProperty("message").GetString());
	}

	[Fact]
	public async Task ServerRequest_WhoseHandlerThrows_GetsInternalError()
	{
		_connection.ServerRequestHandler = (_, _) => throw new InvalidOperationException("boom");
		_connection.Start();
		await ServerSendsAsync("{\"method\":\"x/y\",\"id\":3,\"params\":{}}");

		var answer = await ReadSentAsync();
		Assert.Equal(-32603, answer.GetProperty("error").GetProperty("code").GetInt32());
	}

	[Fact]
	public async Task EndOfStream_FailsPendingCalls_CompletesClosed_AndRejectsNewCalls()
	{
		_connection.Start();
		var pending = _connection.SendRequestAsync("turn/start", new { threadId = "t" });
		await ReadSentAsync();

		_serverToClient.CompleteWriting();

		await Assert.ThrowsAsync<CodexConnectionLostException>(() => pending);
		Assert.Null(await _connection.Closed.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.True(_connection.IsClosed);
		await Assert.ThrowsAsync<CodexConnectionLostException>(() => _connection.SendRequestAsync("again", null));
	}

	private async Task<JsonElement> ReadSentAsync()
	{
		var line = await _serverReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
		Assert.NotNull(line);
		using var document = JsonDocument.Parse(line);
		return document.RootElement.Clone();
	}

	private Task ServerSendsAsync(string json) => ServerSendsRawAsync(json + "\n");

	private Task ServerSendsRawAsync(string text)
	{
		_serverToClient.Write(Encoding.UTF8.GetBytes(text));
		return Task.CompletedTask;
	}
}
