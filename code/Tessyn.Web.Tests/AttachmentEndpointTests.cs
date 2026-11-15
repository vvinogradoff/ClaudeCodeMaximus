using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <summary>POST /api/attachments and GET /link/blob/{id} against a fake daemon (attachments step, decision A12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentEndpointTests : IDisposable
{
	private const string NodeId = "node-1";
	private readonly string _blobs = Path.Combine(Path.GetTempPath(), "tessyn-blob-tests-" + Guid.NewGuid().ToString("N"));

	private TessynWebFactory NewFactory() => new TessynWebFactory().Set("TessynWeb:Attachments:BlobDirectory", _blobs);

	private static MultipartFormDataContent Form(byte[] bytes, string fileName = "photo.png", string? nodeId = NodeId, bool fileFirst = false)
	{
		var form = new MultipartFormDataContent();
		var file = new ByteArrayContent(bytes);
		file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		if (fileFirst)
			form.Add(file, "file", fileName);
		if (nodeId != null)
			form.Add(new StringContent(nodeId), "nodeId");
		if (!fileFirst)
			form.Add(file, "file", fileName);
		return form;
	}

	private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

	private string[] BlobFiles() => Directory.Exists(_blobs) ? Directory.GetFiles(_blobs) : [];

	[Fact]
	public async Task Upload_is_fetched_by_the_daemon_once_and_the_reply_goes_to_the_browser()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		var bytes = RandomNumberGenerator.GetBytes(100_000);

		var upload = browser.PostAsync(Constants.Paths.Attachments, Form(bytes));
		using var request = await daemon.ReadRequestAsync();

		var root = request.RootElement;
		Assert.Equal("attachment.fetch", root.GetProperty("method").GetString());
		var args = root.GetProperty("params");
		Assert.Equal(NodeId, args.GetProperty("nodeId").GetString());
		Assert.Equal("photo.png", args.GetProperty("name").GetString());
		Assert.Equal(bytes.Length, args.GetProperty("size").GetInt64());
		Assert.Equal(Sha(bytes), args.GetProperty("sha256").GetString());
		var blobId = args.GetProperty("blobId").GetString()!;

		using (var first = await daemon.GetAsync("/link/blob/" + blobId))
		{
			Assert.Equal(HttpStatusCode.OK, first.StatusCode);
			Assert.Equal(bytes.Length, first.Content.Headers.ContentLength);
			Assert.Equal(bytes, await first.Content.ReadAsByteArrayAsync());
		}

		// One-time: the second download finds nothing.
		using (var second = await daemon.GetAsync("/link/blob/" + blobId))
			Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

		await daemon.ReplyAsync(root.GetProperty("id").GetString()!,
			"{\"attachmentId\":\"att.x\",\"path\":\"C:\\\\a\\\\b.png\",\"name\":\"photo.png\",\"size\":100000,\"mime\":\"image/png\"}");
		using var response = await upload;

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		Assert.Equal("att.x", body.RootElement.GetProperty("attachmentId").GetString());
		Assert.Equal("C:\\a\\b.png", body.RootElement.GetProperty("path").GetString());
		Assert.Equal("image/png", body.RootElement.GetProperty("mime").GetString());
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task The_blob_is_deleted_when_the_daemon_never_fetched_it()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var upload = browser.PostAsync(Constants.Paths.Attachments, Form([1, 2, 3]));
		using var request = await daemon.ReadRequestAsync();
		await daemon.ReplyErrorAsync(request.RootElement.GetProperty("id").GetString()!, -32000, "Session not found");
		using var response = await upload;

		Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
		var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
		Assert.Contains("Session not found", error);
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task A_daemon_that_does_not_answer_gives_a_timeout_and_the_blob_is_removed()
	{
		using var factory = NewFactory().Set("TessynWeb:Attachments:FetchTimeoutSeconds", "0.3");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var response = await browser.PostAsync(Constants.Paths.Attachments, Form([1, 2, 3]));

		Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task Offline_daemon_is_refused_before_the_file_is_read()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();

		using var response = await browser.PostAsync(Constants.Paths.Attachments, Form([1, 2, 3]));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
		Assert.Contains("offline", error);
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task A_file_over_the_limit_is_refused_with_413_and_nothing_is_kept()
	{
		using var factory = NewFactory().Set("TessynWeb:Attachments:MaxUploadBytes", "4096");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var response = await browser.PostAsync(Constants.Paths.Attachments, Form(new byte[10_000]));

		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task A_file_exactly_at_the_limit_is_accepted()
	{
		using var factory = NewFactory().Set("TessynWeb:Attachments:MaxUploadBytes", "4096");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var upload = browser.PostAsync(Constants.Paths.Attachments, Form(new byte[4096]));
		using var request = await daemon.ReadRequestAsync();
		Assert.Equal(4096, request.RootElement.GetProperty("params").GetProperty("size").GetInt64());
		await daemon.ReplyAsync(request.RootElement.GetProperty("id").GetString()!, "{\"attachmentId\":\"att.y\"}");
		using var response = await upload;

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task Upload_needs_a_logged_in_browser_and_the_client_header()
	{
		using var factory = NewFactory();
		using var anonymous = factory.CreateBrowser();
		using var login = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var denied = await anonymous.PostAsync(Constants.Paths.Attachments, Form([1]));
		Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

		login.DefaultRequestHeaders.Remove("X-Tessyn-Client");
		using var noHeader = await login.PostAsync(Constants.Paths.Attachments, Form([1]));
		Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
	}

	[Fact]
	public async Task Malformed_uploads_are_400()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var json = await browser.PostAsJsonAsync(Constants.Paths.Attachments, new { nodeId = NodeId });
		Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);

		using var noNode = await browser.PostAsync(Constants.Paths.Attachments, Form([1], nodeId: null));
		Assert.Equal(HttpStatusCode.BadRequest, noNode.StatusCode);

		using var fileFirst = await browser.PostAsync(Constants.Paths.Attachments, Form([1], fileFirst: true));
		Assert.Equal(HttpStatusCode.BadRequest, fileFirst.StatusCode);

		using var onlyNode = new MultipartFormDataContent { { new StringContent(NodeId), "nodeId" } };
		using var noFile = await browser.PostAsync(Constants.Paths.Attachments, onlyNode);
		Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
	}

	[Fact]
	public async Task Blob_download_needs_the_link_token_and_a_real_id()
	{
		using var factory = NewFactory();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		var id = new string('a', 32);

		using var noToken = await daemon.GetAsync("/link/blob/" + id, token: null);
		using var wrongToken = await daemon.GetAsync("/link/blob/" + id, token: "nope");
		using var unknown = await daemon.GetAsync("/link/blob/" + id);
		using var malformed = await daemon.GetAsync("/link/blob/..%2f..%2fetc");

		Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
	}

	[Fact]
	public async Task A_browser_session_cannot_download_a_blob()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();

		using var response = await browser.GetAsync("/link/blob/" + new string('b', 32));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task The_browser_cannot_call_attachment_fetch_through_rpc()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var response = await browser.PostAsync(Constants.Paths.Rpc, new StringContent(
			"{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"attachment.fetch\",\"params\":{}}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json));
		using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		Assert.Equal(-32601, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
	}

	[Fact]
	public async Task The_csp_lets_the_spa_show_previews_and_loosens_nothing_else()
	{
		using var factory = NewFactory();
		using var browser = factory.CreateBrowser();

		using var response = await browser.GetAsync("/healthz");
		var csp = response.Headers.GetValues("Content-Security-Policy").Single();

		Assert.Contains("img-src 'self' data: blob: https:", csp);
		Assert.Contains("media-src 'self' blob: https:", csp);
		Assert.Contains("default-src 'self'", csp);
		Assert.Contains("connect-src 'self'", csp);
		Assert.Contains("object-src 'none'", csp);
		Assert.Contains("frame-ancestors 'none'", csp);
		Assert.DoesNotContain("script-src", csp);
		Assert.DoesNotContain("unsafe-eval", csp);
	}

	// ── review fixes: limits, storage, abort, early 413, hub timeout ─────────

	[Fact]
	public async Task A_third_upload_while_two_are_running_is_answered_429_and_the_others_still_finish()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var first = browser.PostAsync(Constants.Paths.Attachments, Form([1]));
		using var firstRequest = await daemon.ReadRequestAsync();
		var second = browser.PostAsync(Constants.Paths.Attachments, Form([2]));
		using var secondRequest = await daemon.ReadRequestAsync();

		using var third = await browser.PostAsync(Constants.Paths.Attachments, Form([3]));

		Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
		var error = (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
		Assert.Contains("Too many uploads", error);

		await daemon.ReplyAsync(firstRequest.RootElement.GetProperty("id").GetString()!, "{\"attachmentId\":\"att.1\"}");
		await daemon.ReplyAsync(secondRequest.RootElement.GetProperty("id").GetString()!, "{\"attachmentId\":\"att.2\"}");
		using var firstResponse = await first;
		using var secondResponse = await second;
		Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
		Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

		// The slots are free again.
		var again = browser.PostAsync(Constants.Paths.Attachments, Form([4]));
		using var againRequest = await daemon.ReadRequestAsync();
		await daemon.ReplyAsync(againRequest.RootElement.GetProperty("id").GetString()!, "{\"attachmentId\":\"att.4\"}");
		using var againResponse = await again;
		Assert.Equal(HttpStatusCode.OK, againResponse.StatusCode);
	}

	[Fact]
	public async Task A_failed_upload_gives_its_slot_back()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		for (var i = 0; i < 5; i++)
		{
			using var bad = await browser.PostAsync(Constants.Paths.Attachments, Form([1], nodeId: null));
			Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
		}

		var upload = browser.PostAsync(Constants.Paths.Attachments, Form([1]));
		using var request = await daemon.ReadRequestAsync();
		await daemon.ReplyAsync(request.RootElement.GetProperty("id").GetString()!, "{}");
		using var response = await upload;
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task A_blob_folder_without_room_answers_507_with_a_message_and_keeps_nothing()
	{
		using var factory = NewFactory().Set("TessynWeb:Attachments:MaxBlobDirectoryBytes", "1000");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		using var response = await browser.PostAsync(Constants.Paths.Attachments, Form(new byte[5000]));

		Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
		var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
		Assert.Contains("no room", error);
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task A_daemon_timeout_code_is_a_504_not_a_422()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var upload = browser.PostAsync(Constants.Paths.Attachments, Form([1, 2, 3]));
		using var request = await daemon.ReadRequestAsync();
		await daemon.ReplyErrorAsync(request.RootElement.GetProperty("id").GetString()!, -32002, "The download from the web server timed out");
		using var response = await upload;

		Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
	}

	[Fact]
	public async Task A_browser_that_gives_up_while_the_daemon_is_fetching_leaves_no_blob_and_frees_its_slot()
	{
		using var factory = NewFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		using var cancel = new CancellationTokenSource();

		var upload = browser.PostAsync(Constants.Paths.Attachments, Form(new byte[20_000]), cancel.Token);
		using var abandoned = await daemon.ReadRequestAsync();
		Assert.Single(BlobFiles());

		cancel.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload);
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (BlobFiles().Length > 0 && DateTime.UtcNow < deadline)
			await Task.Delay(20);

		Assert.Empty(BlobFiles());

		// Both slots are usable again (the aborted one was released).
		var next = browser.PostAsync(Constants.Paths.Attachments, Form([1]));
		using var request = await daemon.ReadRequestAsync();
		await daemon.ReplyAsync(request.RootElement.GetProperty("id").GetString()!, "{}");
		using var response = await next;
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task A_declared_length_above_the_cap_is_refused_with_413_before_the_body_is_read()
	{
		using var factory = NewFactory().Set("TessynWeb:Attachments:MaxUploadBytes", "4096");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		// More than cap + the 1 MB multipart allowance, with a Content-Length the server can see up front.
		using var response = await browser.PostAsync(Constants.Paths.Attachments, Form(new byte[1_300_000]));

		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
		Assert.Contains("too large", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
		Assert.Empty(BlobFiles());
	}

	[Fact]
	public async Task The_hub_timeout_overload_fires_at_the_given_time_and_drops_the_pending_request()
	{
		using var factory = NewFactory();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		var hub = factory.Services.GetRequiredService<Tessyn.Web.Link.DaemonLinkHub>();

		var started = DateTime.UtcNow;
		var outcome = await hub.SendAsync("attachment.fetch", null, CancellationToken.None, TimeSpan.FromMilliseconds(250));
		var elapsed = DateTime.UtcNow - started;
		using var request = await daemon.ReadRequestAsync();

		Assert.True(outcome.IsError);
		Assert.Equal(-32002, JsonDocument.Parse(outcome.ErrorJson!).RootElement.GetProperty("code").GetInt32());
		Assert.InRange(elapsed, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10));
		Assert.False(hub.CompleteReply(request.RootElement.GetProperty("id").GetString()!, new Tessyn.Web.Link.RpcOutcome("1", null)));
	}

	public void Dispose()
	{
		if (Directory.Exists(_blobs))
			Directory.Delete(_blobs, true);
	}
}
