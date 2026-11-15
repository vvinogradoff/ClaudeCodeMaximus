using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Tessyn.Web.E2E.Tests.Harness;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests;

/// <summary>
/// Attachments (decision A12) through the real stack: a browser upload to the real Tessyn.Web, the real relay connector pulling the blob
/// with its link token, the real Core storing it under the data root, and the browser reading it back over the relay.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentFlowTests
{
	private static JsonNode P(string json) => JsonNode.Parse(json)!;

	private static MultipartFormDataContent Form(string nodeId, byte[] bytes, string fileName, string contentType = "application/octet-stream")
	{
		var form = new MultipartFormDataContent { { new StringContent(nodeId), "nodeId" } };
		var file = new ByteArrayContent(bytes);
		file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
		form.Add(file, "file", fileName);
		return form;
	}

	private static string AttachmentsRoot(WebStack stack) => Path.Combine(stack.Daemon.Root, "attachments");

	[Fact]
	public async Task A01_Upload_is_pulled_by_the_daemon_stored_per_profile_and_session_and_readable_over_the_relay()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var nodeId = stack.Seed.BetaId;
		var bytes = RandomNumberGenerator.GetBytes(6 * 1024 * 1024);

		using var response = await browser.PostAsync("/api/attachments", Form(nodeId, bytes, "holiday photo (1).png", "image/png"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
		var path = body["path"]!.GetValue<string>();
		Assert.StartsWith(Path.Combine(AttachmentsRoot(stack), "default", nodeId) + Path.DirectorySeparatorChar, path);
		Assert.EndsWith("-holiday photo (1).png", path);
		Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
		Assert.Equal("holiday photo (1).png", body["name"]!.GetValue<string>());
		Assert.Equal(bytes.Length, body["size"]!.GetValue<long>());
		Assert.Equal("image/png", body["mime"]!.GetValue<string>());
		Assert.StartsWith("att.", body["attachmentId"]!.GetValue<string>());

		// The browser can list it and read it back through the allowlisted methods; the relay applies its policy (a PNG is on it).
		var list = await stack.RpcAsync(browser, "attachment.list", P($$"""{"nodeId":"{{nodeId}}"}"""));
		Assert.Equal(path, list["attachments"]![0]!["path"]!.GetValue<string>());
		var chunk = await stack.RpcAsync(browser, "file.read", P($$"""{"handle":"{{body["attachmentId"]!.GetValue<string>()}}","length":16}"""));
		Assert.Equal("base64", chunk["encoding"]!.GetValue<string>());
		Assert.Equal(bytes.Take(16).ToArray(), Convert.FromBase64String(chunk["data"]!.GetValue<string>()));
		Assert.False(chunk["eof"]!.GetValue<bool>());
	}

	[Fact]
	public async Task A02_A_session_with_a_profile_stores_under_that_profile_id()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var node = stack.Daemon.Registry.FindModelByNodeId(stack.Seed.BetaId)!;
		lock (stack.Daemon.Settings.SyncRoot)
			node.ProfileName = "work@example.test";

		using var response = await browser.PostAsync("/api/attachments", Form(node.NodeId, [1, 2, 3], "n.txt", "text/plain"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var path = (await response.Content.ReadFromJsonAsync<JsonNode>())!["path"]!.GetValue<string>();
		Assert.StartsWith(Path.Combine(AttachmentsRoot(stack), "profile_1", node.NodeId), path);
	}

	[Fact]
	public async Task A03_Two_uploads_of_the_same_name_do_not_overwrite_each_other()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var nodeId = stack.Seed.BetaId;

		var paths = new List<string>();
		for (var i = 0; i < 3; i++)
		{
			using var response = await browser.PostAsync("/api/attachments", Form(nodeId, [(byte)i], "same.txt", "text/plain"));
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			paths.Add((await response.Content.ReadFromJsonAsync<JsonNode>())!["path"]!.GetValue<string>());
		}

		Assert.Equal(3, paths.Distinct().Count());
		Assert.Equal([0, 1, 2], paths.Select(p => (int)File.ReadAllBytes(p)[0]).Order());
	}

	[Fact]
	public async Task A04_An_unknown_session_is_refused_with_the_daemons_message_and_nothing_is_stored()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();

		using var response = await browser.PostAsync("/api/attachments", Form("no-such-session", [1], "a.txt", "text/plain"));

		Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
		Assert.Contains("Session not found", (await response.Content.ReadFromJsonAsync<JsonNode>())!["error"]!.GetValue<string>());
		Assert.False(Directory.Exists(AttachmentsRoot(stack)) && Directory.GetFiles(AttachmentsRoot(stack), "*", SearchOption.AllDirectories).Length > 0);
	}

	[Fact]
	public async Task A05_With_the_daemon_offline_the_upload_fails_clearly_and_works_again_after_it_reconnects()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		stack.Link.Stop();

		using var offline = await browser.PostAsync("/api/attachments", Form(stack.Seed.BetaId, [1], "a.txt", "text/plain"));
		Assert.Equal(HttpStatusCode.ServiceUnavailable, offline.StatusCode);
		Assert.Contains("offline", (await offline.Content.ReadFromJsonAsync<JsonNode>())!["error"]!.GetValue<string>());

		stack.Link.Start();
		await stack.Link.WaitForStateAsync(RelayState.Connected);
		using var again = await browser.PostAsync("/api/attachments", Form(stack.Seed.BetaId, [1], "a.txt", "text/plain"));
		Assert.Equal(HttpStatusCode.OK, again.StatusCode);
	}

	[Fact]
	public async Task A06_The_browser_cannot_import_or_fetch_and_the_catalog_carries_the_preview_hosts()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();

		var import = await stack.RpcRawAsync(browser, "attachment.import", P($$"""{"nodeId":"{{stack.Seed.BetaId}}","sourcePath":"C:\\x.txt"}"""));
		var fetch = await stack.RpcRawAsync(browser, "attachment.fetch", P("""{"blobId":"b","nodeId":"n","name":"a","size":1,"sha256":"x"}"""));
		var catalog = await stack.RpcAsync(browser, "catalog.get");

		Assert.Equal(-32601, WebStack.ErrorCode(import));
		Assert.Equal(-32601, WebStack.ErrorCode(fetch));
		Assert.Equal(["annielartens.online", "*.githubusercontent.com"], catalog["mediaPreviewHosts"]!.AsArray().Select(h => h!.GetValue<string>()));
	}

	[Fact]
	public async Task A07_A_desktop_import_is_readable_by_the_phone_and_the_relay_cannot_call_import()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var source = Path.Combine(stack.Daemon.Root, "source.md");
		await File.WriteAllTextAsync(source, "# from the desktop");
		var request = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"] = 1,
			["method"] = "attachment.import",
			["params"] = new JsonObject { ["nodeId"] = stack.Seed.BetaId, ["sourcePath"] = source },
		};

		var imported = await stack.Daemon.Dispatcher.HandleRequestAsync(request, TdcpTransport.Loopback, CancellationToken.None);
		var viaRelay = await stack.Daemon.Dispatcher.HandleRequestAsync(request, TdcpTransport.Relay, CancellationToken.None);

		Assert.Null(imported["error"]);
		Assert.Equal(-32601, viaRelay["error"]!["code"]!.GetValue<int>());
		var read = await stack.RpcAsync(browser, "file.read", P($$"""{"handle":"{{imported["result"]!["attachmentId"]!.GetValue<string>()}}"}"""));
		Assert.Equal("# from the desktop", read["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task A08_The_blob_endpoint_is_closed_to_browsers_and_to_a_wrong_token()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var anonymous = stack.Web.CreateClient();

		using var asBrowser = await browser.GetAsync("/link/blob/" + new string('a', 32));
		using var asNobody = await anonymous.GetAsync("/link/blob/" + new string('a', 32));
		var wrong = new HttpRequestMessage(HttpMethod.Get, "/link/blob/" + new string('a', 32));
		wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token");
		using var withWrongToken = await anonymous.SendAsync(wrong);

		Assert.Equal(HttpStatusCode.Unauthorized, asBrowser.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, asNobody.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, withWrongToken.StatusCode);
	}
}
