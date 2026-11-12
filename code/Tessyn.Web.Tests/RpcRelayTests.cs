using System.Text;
using System.Text.Json;
using Tessyn.Web.Link;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class RpcRelayTests
{
	private static async Task<JsonDocument> CallAsync(HttpClient browser, string requestJson)
	{
		var response = await browser.PostAsync(Constants.Paths.Rpc, new StringContent(requestJson, Encoding.UTF8, Constants.ContentTypes.Json));
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task Rpc_is_relayed_end_to_end_and_the_browser_id_is_preserved()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var call = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"session.get\",\"params\":{\"nodeId\":\"abc\"}}");
		using var request = await daemon.ReadRequestAsync();

		var root = request.RootElement;
		Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
		Assert.StartsWith("r-", root.GetProperty("id").GetString());
		Assert.Equal("session.get", root.GetProperty("method").GetString());
		Assert.Equal("abc", root.GetProperty("params").GetProperty("nodeId").GetString());

		await daemon.ReplyAsync(root.GetProperty("id").GetString()!, "{\"id\":\"abc\",\"name\":\"S\"}");
		using var reply = await call;

		Assert.Equal(42, reply.RootElement.GetProperty("id").GetInt32());
		Assert.Equal("2.0", reply.RootElement.GetProperty("jsonrpc").GetString());
		Assert.Equal("S", reply.RootElement.GetProperty("result").GetProperty("name").GetString());
		Assert.False(reply.RootElement.TryGetProperty("error", out _));
	}

	[Fact]
	public async Task String_ids_are_preserved_and_server_ids_are_unique_per_request()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var first = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":\"same\",\"method\":\"client.hello\"}");
		using var firstRequest = await daemon.ReadRequestAsync();
		var second = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":\"same\",\"method\":\"client.hello\"}");
		using var secondRequest = await daemon.ReadRequestAsync();

		var firstId = firstRequest.RootElement.GetProperty("id").GetString()!;
		var secondId = secondRequest.RootElement.GetProperty("id").GetString()!;
		Assert.NotEqual(firstId, secondId);
		Assert.False(firstRequest.RootElement.TryGetProperty("params", out _));

		await daemon.ReplyAsync(secondId, "2");
		await daemon.ReplyAsync(firstId, "1");
		using var firstReply = await first;
		using var secondReply = await second;

		Assert.Equal("same", firstReply.RootElement.GetProperty("id").GetString());
		Assert.Equal(1, firstReply.RootElement.GetProperty("result").GetInt32());
		Assert.Equal(2, secondReply.RootElement.GetProperty("result").GetInt32());
	}

	[Fact]
	public async Task Daemon_errors_are_relayed_with_the_browser_id()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var call = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"session.get\",\"params\":{}}");
		using var request = await daemon.ReadRequestAsync();
		await daemon.ReplyErrorAsync(request.RootElement.GetProperty("id").GetString()!, -32000, "Session not found");
		using var reply = await call;

		Assert.Equal(7, reply.RootElement.GetProperty("id").GetInt32());
		Assert.Equal(-32000, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal("Session not found", reply.RootElement.GetProperty("error").GetProperty("message").GetString());
	}

	[Fact]
	public async Task Non_allowlisted_methods_get_method_not_found_and_never_reach_the_daemon()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var reply = await CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"orchestration.spawn\",\"params\":{}}");

		Assert.Equal(-32601, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(1, reply.RootElement.GetProperty("id").GetInt32());
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => daemon.ReadRequestAsync(TimeSpan.FromMilliseconds(300)));
	}

	[Fact]
	public async Task Malformed_requests_get_the_json_rpc_error_codes()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();

		using var parse = await CallAsync(browser, "{not json");
		using var notObject = await CallAsync(browser, "[1,2]");
		using var noId = await CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"method\":\"client.hello\"}");
		using var noMethod = await CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":3}");
		using var badParams = await CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"client.hello\",\"params\":5}");

		Assert.Equal(-32700, parse.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(JsonValueKind.Null, parse.RootElement.GetProperty("id").ValueKind);
		Assert.Equal(-32600, notObject.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(-32600, noId.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(-32600, noMethod.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(3, noMethod.RootElement.GetProperty("id").GetInt32());
		Assert.Equal(-32602, badParams.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(4, badParams.RootElement.GetProperty("id").GetInt32());
	}

	[Fact]
	public async Task Offline_daemon_gives_minus_32001()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();

		using var reply = await CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"client.hello\"}");

		Assert.Equal(-32001, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(9, reply.RootElement.GetProperty("id").GetInt32());
	}

	[Fact]
	public async Task Silent_daemon_gives_minus_32002_and_a_late_reply_is_ignored()
	{
		using var factory = new TessynWebFactory().Set("TessynWeb:Link:RequestTimeoutSeconds", "0.3");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var call = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"client.hello\"}");
		using var request = await daemon.ReadRequestAsync();
		using var reply = await call;

		Assert.Equal(-32002, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		var late = await daemon.ReplyAsync(request.RootElement.GetProperty("id").GetString()!, "1");
		Assert.Equal(System.Net.HttpStatusCode.NoContent, late.StatusCode);
	}

	[Fact]
	public async Task Content_heavy_methods_use_the_longer_timeout()
	{
		using var factory = new TessynWebFactory()
			.Set("TessynWeb:Link:RequestTimeoutSeconds", "0.2")
			.Set("TessynWeb:Link:LongRequestTimeoutSeconds", "5");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var call = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tree.snapshot\",\"params\":{}}");
		using var request = await daemon.ReadRequestAsync();
		await Task.Delay(600);
		await daemon.ReplyAsync(request.RootElement.GetProperty("id").GetString()!, "{\"version\":1}");
		using var reply = await call;

		Assert.Equal(1, reply.RootElement.GetProperty("result").GetProperty("version").GetInt32());
	}

	[Fact]
	public async Task A_new_link_supersedes_the_old_one_and_fails_its_pending_requests()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var oldDaemon = await FakeDaemon.ConnectAsync(factory);

		var pending = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"client.hello\"}");
		using var oldRequest = await oldDaemon.ReadRequestAsync();

		await using var newDaemon = await FakeDaemon.ConnectAsync(factory);
		using var failed = await pending;

		Assert.Equal(-32001, failed.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.True(await oldDaemon.Stream.EndsAsync(), "the superseded stream should be completed");

		var next = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"client.hello\"}");
		using var newRequest = await newDaemon.ReadRequestAsync();
		await newDaemon.ReplyAsync(newRequest.RootElement.GetProperty("id").GetString()!, "true");
		using var ok = await next;
		Assert.True(ok.RootElement.GetProperty("result").GetBoolean());
	}

	[Fact]
	public async Task Daemon_disconnect_fails_pending_requests_and_marks_the_link_offline()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		var daemon = await FakeDaemon.ConnectAsync(factory);

		var pending = CallAsync(browser, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"client.hello\"}");
		using var request = await daemon.ReadRequestAsync();
		await daemon.DisposeAsync();
		using var failed = await pending;

		Assert.Equal(-32001, failed.RootElement.GetProperty("error").GetProperty("code").GetInt32());
		Assert.False(factory.Services.GetService(typeof(DaemonLinkHub)) is DaemonLinkHub { IsOnline: true });
	}

	[Fact]
	public async Task Allowlist_is_exactly_the_21_contract_methods()
	{
		var allowlist = new RemoteMethodAllowlist();
		var expected = new[]
		{
			"client.hello", "tree.snapshot", "tree.workers", "tree.search", "tree.createSession", "recent.list", "recent.hide",
			"tags.search", "session.get", "session.entries", "session.entry", "session.send", "session.stop", "session.compact",
			"session.setFavorite", "session.setStreaming", "attention.list", "attention.reply", "attention.dismiss", "catalog.get", "usage.get",
		};

		Assert.Equal(21, allowlist.Methods.Count);
		Assert.Equal(expected.OrderBy(x => x), allowlist.Methods.OrderBy(x => x));
		Assert.All(expected, method => Assert.True(allowlist.IsAllowed(method)));
		Assert.False(allowlist.IsAllowed("tdcp.shutdown"));
		await Task.CompletedTask;
	}
}
