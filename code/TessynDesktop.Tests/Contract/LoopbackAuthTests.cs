using System;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>Loopback <c>/tdcp</c> authentication: 401 with a JSON-RPC error body, and Origin rejection (P1.C).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoopbackAuthTests : IDisposable
{
	private const string HelloBody = """{"jsonrpc":"2.0","id":7,"method":"client.hello"}""";

	private readonly TdcpTestHost _host = new(new TdcpDispatcher([new ClientRpcModule()]));

	public void Dispose() => _host.Dispose();

	private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) =>
		JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

	[Fact]
	public async Task A_valid_token_gets_a_result()
	{
		using var response = await _host.PostAsync(HelloBody);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.NotNull((await BodyAsync(response))["result"]);
	}

	[Theory]
	[InlineData("wrong-token")]
	[InlineData("")]
	[InlineData(null)]
	public async Task A_bad_or_missing_token_on_POST_answers_401_with_a_JSON_RPC_error(string? token)
	{
		using var response = await _host.PostAsync(HelloBody, token);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
		var body = await BodyAsync(response);
		Assert.Equal("2.0", body["jsonrpc"]!.GetValue<string>());
		Assert.Equal(-32600, body["error"]!["code"]!.GetValue<int>());
		Assert.Contains("X-CMX-Token", body["error"]!["message"]!.GetValue<string>());
		Assert.Null(body["result"]);
	}

	[Fact]
	public async Task A_token_that_only_shares_a_prefix_is_rejected()
	{
		using var response = await _host.PostAsync(HelloBody, TdcpTestHost.Token + "x");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task A_bad_token_on_the_stream_answers_401()
	{
		using var sse = await SseTestClient.ConnectAsync(_host.BaseUrl, "wrong-token");

		Assert.Equal(401, sse.StatusCode);
	}

	[Theory]
	[InlineData("http://evil.example")]
	[InlineData("null")]
	[InlineData("http://127.0.0.1:1234")]
	public async Task A_request_carrying_an_Origin_is_rejected_even_with_the_right_token(string origin)
	{
		using var response = await _host.PostAsync(HelloBody, TdcpTestHost.Token, origin);

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
		var body = await BodyAsync(response);
		Assert.Equal(-32600, body["error"]!["code"]!.GetValue<int>());
		Assert.Contains("Origin", body["error"]!["message"]!.GetValue<string>());
		Assert.Null(body["result"]);
	}

	[Fact]
	public async Task The_stream_rejects_an_Origin_too()
	{
		using var sse = await SseTestClient.ConnectAsync(_host.BaseUrl, TdcpTestHost.Token, origin: "http://evil.example");

		Assert.Equal(403, sse.StatusCode);
	}
}
