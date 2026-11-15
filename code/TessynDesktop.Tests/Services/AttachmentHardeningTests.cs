using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Review fixes for the attachments (F1, F3, F5, F10, F11, F12): the relay deny rules on import, the stored-attachment check on
/// file.read (junction, hard link), timeouts, hostile names and ids, and the failures of the download.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentHardeningTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();
	private readonly FakeBlobClient _blobs = new();
	private readonly AttachmentService _attachments;
	private readonly FileAccessService _files;
	private readonly TdcpDispatcher _dispatcher;

	public AttachmentHardeningTests()
	{
		_attachments = new AttachmentService(
			_env.Base.Registry, _env.Base.Settings, _ => null, _blobs, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);
		_files = new FileAccessService(
			_env.Base.Registry, _env.Attention, _env.Base.Reader, _env.Base.Settings, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile, attachments: _attachments);
		_dispatcher = new TdcpDispatcher([new FileRpcModule(_files), new AttachmentRpcModule(_attachments)]);
	}

	public void Dispose() => _env.Dispose();

	private string NodeId => _env.Session.NodeId;
	private string Root => Path.Combine(_env.DataRoot, "attachments");

	private string Source(string name, byte[] bytes)
	{
		var path = Path.Combine(_env.Base.Root, "sources", name);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	private async Task<JsonNode> CallAsync(string method, string paramsJson, TdcpTransport transport)
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		return await _dispatcher.HandleTextAsync(request, transport, CancellationToken.None);
	}

	private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

	private static string ReadArgs(string handle) => new JsonObject { ["handle"] = handle }.ToJsonString();

	private static void CreateJunction(string link, string target)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(link)!);
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
		{
			CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
		})!;
		process.WaitForExit();
		Assert.True(Directory.Exists(link), "the junction could not be created");
	}

	private static bool TryHardLink(string link, string existing)
	{
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{existing}\"")
		{
			CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
		})!;
		process.WaitForExit();
		return File.Exists(link);
	}

	// ── F1: import uses the relay deny rules ─────────────────────────────────

	[Theory]
	[InlineData("logs/app.log")]
	[InlineData("sessions/2026-11-15-session.txt")]
	[InlineData("attention/ledger.json")]
	[InlineData("memory-backups/notes.md")]
	[InlineData("drafts/draft.md")]
	[InlineData("models-cache.json")]
	public void Import_refuses_app_data_that_only_the_loopback_rules_would_allow(string relative)
	{
		var inside = Path.Combine(_env.DataRoot, relative.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
		File.WriteAllText(inside, "private");

		var ex = Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, inside));

		Assert.Contains("app data", ex.Message);
		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);
	}

	[Fact]
	public void The_same_app_data_file_is_readable_over_the_loopback_which_is_why_import_must_not_copy_it()
	{
		var inside = Path.Combine(_env.DataRoot, "logs", "app.log");
		Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
		File.WriteAllText(inside, "private");
		var denied = Path.Combine(_env.DataRoot, "appsettings.json");
		File.WriteAllText(denied, "{}");

		Assert.True(FileAccessPolicy.IsDenied(inside, _env.DataRoot, relay: true));
		Assert.False(FileAccessPolicy.IsDenied(inside, _env.DataRoot, relay: false));
		Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, denied));
	}

	[Fact]
	public void Import_refuses_the_attachment_of_another_session_but_takes_the_own_one_again()
	{
		var other = _env.AddSession("Other", "u", "a");
		var mine = _attachments.Import(NodeId, Source("mine.txt", [1]));
		var theirs = _attachments.Import(other.NodeId, Source("theirs.txt", [2]));

		var ex = Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, theirs.Path));
		var again = _attachments.Import(NodeId, mine.Path);

		Assert.Contains("another session", ex.Message);
		Assert.EndsWith("-mine.txt", again.Path);
		Assert.NotEqual(mine.Path, again.Path);
	}

	[Fact]
	public void Import_refuses_a_file_over_the_upload_cap_with_a_clear_message()
	{
		var big = Path.Combine(_env.Base.Root, "sources", "big.bin");
		Directory.CreateDirectory(Path.GetDirectoryName(big)!);
		using (var stream = new FileStream(big, FileMode.Create))
			stream.SetLength(Constants.Attachments.MaxFetchBytes + 1);

		var ex = Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, big));

		Assert.Equal("The file is larger than the 200 MB limit", ex.Message);
		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);
	}

	[Fact]
	public void Import_accepts_a_file_exactly_at_the_cap()
	{
		var edge = Path.Combine(_env.Base.Root, "sources", "edge.bin");
		Directory.CreateDirectory(Path.GetDirectoryName(edge)!);
		using (var stream = new FileStream(edge, FileMode.Create))
			stream.SetLength(Constants.Attachments.MaxFetchBytes);

		var stored = _attachments.Import(NodeId, edge);

		Assert.Equal(Constants.Attachments.MaxFetchBytes, stored.Size);
	}

	// ── missing test 3: import through a junction ────────────────────────────

	[Fact]
	public void Import_follows_the_real_path_through_a_junction_to_app_data()
	{
		var logs = Path.Combine(_env.DataRoot, "logs");
		Directory.CreateDirectory(logs);
		File.WriteAllText(Path.Combine(logs, "app.log"), "private");
		var link = Path.Combine(_env.Base.Root, "innocent-folder");
		CreateJunction(link, logs);

		var ex = Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, Path.Combine(link, "app.log")));

		Assert.Contains("app data", ex.Message);
	}

	[Fact]
	public void Import_follows_the_real_path_through_a_junction_to_a_credential_folder()
	{
		var ssh = Path.Combine(_env.UserProfile, ".ssh");
		Directory.CreateDirectory(ssh);
		File.WriteAllText(Path.Combine(ssh, "config"), "Host x");
		var link = Path.Combine(_env.Base.Root, "docs");
		CreateJunction(link, ssh);

		Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, Path.Combine(link, "config")));
	}

	[Fact]
	public void Import_refuses_a_hard_link_to_a_denied_file()
	{
		var secret = Path.Combine(_env.DataRoot, "logs", "app.log");
		Directory.CreateDirectory(Path.GetDirectoryName(secret)!);
		File.WriteAllText(secret, "private");
		var link = Path.Combine(_env.Base.Root, "sources", "innocent.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(link)!);
		if (!TryHardLink(link, secret))
			return;

		Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, link));
	}

	// ── F3: an attachment handle must lead to the stored file ────────────────

	[Fact]
	public async Task File_read_refuses_an_attachment_whose_node_folder_is_a_junction_to_the_outside()
	{
		var stored = _attachments.Import(NodeId, Source("a.txt", Encoding.UTF8.GetBytes("inside")));
		var nodeFolder = Path.GetDirectoryName(stored.Path)!;
		var outside = Path.Combine(_env.Base.Root, "documents");
		Directory.CreateDirectory(outside);
		File.WriteAllText(Path.Combine(outside, Path.GetFileName(stored.Path)), "OUTSIDE SECRET");
		Directory.Delete(nodeFolder, recursive: true);
		CreateJunction(nodeFolder, outside);

		foreach (var transport in new[] { TdcpTransport.Relay, TdcpTransport.Loopback })
		{
			var response = await CallAsync("file.read", ReadArgs(stored.AttachmentId), transport);
			Assert.Contains("not stored in the attachments folder", (string?)response["error"]!["message"]);
		}
	}

	[Fact]
	public async Task File_read_refuses_an_attachment_that_is_hard_linked_under_a_second_name()
	{
		var stored = _attachments.Import(NodeId, Source("a.txt", Encoding.UTF8.GetBytes("two names")));
		var second = Path.Combine(_env.Base.Root, "second-name.txt");
		if (!TryHardLink(second, stored.Path))
			return;

		var relay = await CallAsync("file.read", ReadArgs(stored.AttachmentId), TdcpTransport.Relay);
		var loopback = await CallAsync("file.read", ReadArgs(stored.AttachmentId), TdcpTransport.Loopback);

		Assert.Contains("hard-linked", (string?)relay["error"]!["message"]);
		Assert.Contains("hard-linked", (string?)loopback["error"]!["message"]);
	}

	[Fact]
	public async Task File_read_still_works_for_a_plain_stored_attachment_on_both_transports()
	{
		var stored = _attachments.Import(NodeId, Source("a.txt", Encoding.UTF8.GetBytes("plain")));

		var relay = await CallAsync("file.read", ReadArgs(stored.AttachmentId), TdcpTransport.Relay);
		var loopback = await CallAsync("file.read", ReadArgs(stored.AttachmentId), TdcpTransport.Loopback);

		Assert.Equal("plain", (string?)relay["result"]!["data"]);
		Assert.Equal("plain", (string?)loopback["result"]!["data"]);
	}

	[Fact]
	public void IsStoredAttachment_needs_the_exact_profile_node_file_depth_and_the_own_session()
	{
		var stored = _attachments.Import(NodeId, Source("a.txt", [1]));

		Assert.True(_attachments.IsStoredAttachment(stored.Path, NodeId));
		Assert.False(_attachments.IsStoredAttachment(stored.Path, "another-node"));
		Assert.False(_attachments.IsStoredAttachment(Path.Combine(Root, "default", NodeId, "sub", "a.txt"), NodeId));
		Assert.False(_attachments.IsStoredAttachment(Path.Combine(Root, "a.txt"), NodeId));
		Assert.False(_attachments.IsStoredAttachment(Path.Combine(_env.Base.Root, "elsewhere", "default", NodeId, "a.txt"), NodeId));
	}

	// ── F5: timeouts ─────────────────────────────────────────────────────────

	[Fact]
	public void The_daemon_waits_well_less_than_the_server()
	{
		Assert.True(Constants.Attachments.FetchTimeout <= TimeSpan.FromSeconds(240));
	}

	[Fact]
	public async Task A_stalled_download_is_a_timeout_error_with_the_timeout_code_and_leaves_nothing_behind()
	{
		_blobs.Hang = true;
		var quick = new AttachmentService(
			_env.Base.Registry, _env.Base.Settings, _ => null, _blobs, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile, fetchTimeout: TimeSpan.FromMilliseconds(150));
		var dispatcher = new TdcpDispatcher([new AttachmentRpcModule(quick)]);
		var args = new JsonObject
		{
			["blobId"] = new string('a', 32), ["nodeId"] = NodeId, ["name"] = "x.txt", ["size"] = 1, ["sha256"] = Sha([1]),
		}.ToJsonString();

		var response = await dispatcher.HandleTextAsync(
			$$"""{"jsonrpc":"2.0","id":1,"method":"attachment.fetch","params":{{args}}}""", TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal(-32002, response["error"]!["code"]!.GetValue<int>());
		Assert.Contains("timed out", (string?)response["error"]!["message"]);
		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);
		await Assert.ThrowsAsync<TdcpTimeoutException>(() => quick.FetchAsync(NodeId, new string('a', 32), "x.txt", 1, Sha([1]), CancellationToken.None));
	}

	// ── F10 / F12 / test 9: hostile names and ids ────────────────────────────

	[Theory]
	[InlineData("con", "_con")]
	[InlineData("aux.txt", "_aux.txt")]
	[InlineData("a:b", "a_b")]
	[InlineData("x.txt:ads", "x.txt_ads")]
	[InlineData("evil\u202Etxt.exe", "eviltxt.exe")]
	[InlineData("zero\u200Bwidth.txt", "zerowidth.txt")]
	[InlineData("line\r\nbreak.txt", "linebreak.txt")]
	[InlineData("..\\..\\up.txt", "up.txt")]
	[InlineData("/abs/path/name.txt", "name.txt")]
	[InlineData("C:\\Windows\\system32\\evil.dll", "evil.dll")]
	[InlineData("trailing. . .", "trailing")]
	public void Hostile_file_names_are_made_harmless(string requested, string expected)
	{
		Assert.Equal(expected, AttachmentService.SafeFileName(requested));
	}

	[Fact]
	public void A_5000_character_name_is_cut_to_the_limit_without_a_trailing_dot_or_space()
	{
		var cut = AttachmentService.SafeFileName(new string('x', 118) + ". " + new string('y', 4000));

		Assert.True(cut.Length <= 120);
		Assert.False(cut.EndsWith('.') || cut.EndsWith(' '));
		Assert.Equal(cut, AttachmentService.SafeFileName(cut));
	}

	[Fact]
	public void A_name_that_is_only_format_characters_becomes_the_fallback()
	{
		Assert.Equal("file", AttachmentService.SafeFileName("\u202E\u200B"));
	}

	[Fact]
	public async Task Hostile_names_through_fetch_end_up_as_one_plain_file_in_the_session_folder()
	{
		var content = new byte[] { 1, 2, 3 };
		_blobs.Content = content;
		foreach (var name in new[] { "con", "aux.txt", "a:b", "x.txt:ads", "evil\u202Etxt.exe", new string('n', 5000), "..\\..\\up.txt", "../../up.txt" })
		{
			var args = new JsonObject
			{
				["blobId"] = new string('a', 32), ["nodeId"] = NodeId, ["name"] = name, ["size"] = 3, ["sha256"] = Sha(content),
			}.ToJsonString();

			var response = await CallAsync("attachment.fetch", args, TdcpTransport.Relay);

			if (response["result"] == null)
			{
				// The full path of a very long name can exceed the Windows limit in a deep temp folder: a clear error, nothing stored.
				Assert.Contains("too long", (string?)response["error"]!["message"]);
				continue;
			}

			var path = (string)response["result"]!["path"]!;
			Assert.Equal(Path.Combine(Root, "default", NodeId), Path.GetDirectoryName(path));
			Assert.True(File.Exists(path));
		}

		Assert.InRange(Directory.GetFiles(Path.Combine(Root, "default", NodeId)).Length, 7, 8);
		Assert.Empty(Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Where(d => !d.EndsWith("default") && !d.EndsWith(NodeId)));
	}

	[Theory]
	[InlineData("..")]
	[InlineData("..\\..")]
	[InlineData("../x")]
	[InlineData("C:\\Windows")]
	[InlineData("/etc")]
	[InlineData(".")]
	[InlineData("")]
	[InlineData("node with spaces")]
	public async Task A_hostile_node_id_through_fetch_is_a_session_not_found_and_stores_nothing(string nodeId)
	{
		_blobs.Content = [1];
		var args = new JsonObject
		{
			["blobId"] = new string('a', 32), ["nodeId"] = nodeId, ["name"] = "a.txt", ["size"] = 1, ["sha256"] = Sha([1]),
		}.ToJsonString();

		var response = await CallAsync("attachment.fetch", args, TdcpTransport.Relay);

		Assert.NotNull(response["error"]);
		Assert.Null(_blobs.LastBlobId);
		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);
	}

	[Fact]
	public async Task A_profile_id_of_dots_cannot_climb_out_of_the_attachments_folder()
	{
		_env.Base.Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "..", DisplayName = "Dotty" });
		var service = new AttachmentService(
			_env.Base.Registry, _env.Base.Settings, _ => "Dotty", _blobs, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);

		var stored = service.Import(NodeId, Source("a.txt", [1]));

		Assert.StartsWith(Path.Combine(Root, "__", NodeId), stored.Path);
		Assert.True(service.TryResolve(stored.AttachmentId, out _, out _));
		await Task.CompletedTask;
	}

	[Fact]
	public void A_forged_id_with_dot_segments_does_not_resolve()
	{
		string Forge(string path) => "att." + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		Assert.False(_attachments.TryResolve(Forge("../" + NodeId + "/a.txt"), out _, out _));
		Assert.False(_attachments.TryResolve(Forge("default/../a.txt"), out _, out _));
		Assert.False(_attachments.TryResolve(Forge("default/" + NodeId + "/.."), out _, out _));
	}

	[Fact]
	public void A_path_that_would_exceed_the_path_limit_is_a_clear_error_and_leaves_no_partial_file()
	{
		var deepRoot = Path.Combine(_env.Base.Root, new string('d', 120), new string('e', 100));
		var service = new AttachmentService(
			_env.Base.Registry, _env.Base.Settings, _ => null, _blobs, deepRoot, _env.Clock, userProfile: _env.UserProfile);

		var ex = Assert.Throws<TdcpException>(() => service.Import(NodeId, Source(new string('f', 100) + ".txt", [1])));

		Assert.Contains("too long", ex.Message);
		Assert.Empty(Directory.GetFiles(deepRoot, "*", SearchOption.AllDirectories));
	}

	// ── test 6: parallel imports ─────────────────────────────────────────────

	[Fact]
	public void Parallel_imports_of_the_same_name_in_the_same_second_never_overwrite_each_other()
	{
		var source = Source("same.txt", [1]);
		var paths = new System.Collections.Concurrent.ConcurrentBag<string>();

		Parallel.For(0, 24, _ => paths.Add(_attachments.Import(NodeId, source).Path));

		Assert.Equal(24, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		Assert.All(paths, p => Assert.True(File.Exists(p)));
		Assert.Empty(Directory.GetFiles(Path.Combine(Root, "default", NodeId), "*.part"));
	}

	// ── F11 / test 7 / test 8: the download and the gateway ──────────────────

	private sealed class DroppingContent : HttpContent
	{
		protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
		protected override bool TryComputeLength(out long length)
		{
			length = 0;
			return false;
		}
		protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new DroppingStream());
	}

	private sealed class DroppingStream : Stream
	{
		private bool _first = true;
		public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (_first)
			{
				_first = false;
				buffer.Span[0] = 7;
				return ValueTask.FromResult(1);
			}

			throw new IOException("The response ended prematurely.");
		}
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(respond(request));
	}

	[Fact]
	public async Task A_connection_dropped_in_the_middle_of_the_body_is_the_could_not_reach_error()
	{
		var gateway = new RelayBlobGateway();
		gateway.Attach(new HttpClient(new StubHandler(_ => new HttpResponseMessage { Content = new DroppingContent() })), "http://server");

		var ex = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));

		Assert.StartsWith("Could not reach the web server: ", ex.Message);
	}

	[Fact]
	public async Task A_client_disposed_by_a_reconnect_is_the_could_not_reach_error()
	{
		var gateway = new RelayBlobGateway();
		var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage()));
		gateway.Attach(client, "http://server");
		client.Dispose();

		var ex = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));

		Assert.StartsWith("Could not reach the web server: ", ex.Message);
	}

	[Fact]
	public async Task A_refused_connection_and_the_error_statuses_are_clear_errors()
	{
		var gateway = new RelayBlobGateway();
		HttpResponseMessage? answer = null;
		gateway.Attach(new HttpClient(new StubHandler(_ => answer ?? throw new HttpRequestException("refused"))), "http://server");

		var refused = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));
		answer = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
		var gone = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));
		answer = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
		var token = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));
		answer = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
		var server = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));

		Assert.StartsWith("Could not reach", refused.Message);
		Assert.Contains("no longer available", gone.Message);
		Assert.Equal("Link token rejected", token.Message);
		Assert.Contains("500", server.Message);
	}

	[Fact]
	public async Task A_dropped_download_through_the_service_stores_nothing()
	{
		var gateway = new RelayBlobGateway();
		gateway.Attach(new HttpClient(new StubHandler(_ => new HttpResponseMessage { Content = new DroppingContent() })), "http://server");
		var service = new AttachmentService(_env.Base.Registry, _env.Base.Settings, _ => null, gateway, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);

		await Assert.ThrowsAsync<TdcpException>(() => service.FetchAsync(NodeId, new string('a', 32), "x.bin", 2, Sha([7, 7]), CancellationToken.None));

		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);
	}

	[Fact]
	public async Task A_new_connection_attached_before_the_old_one_detaches_keeps_working()
	{
		var gateway = new RelayBlobGateway();
		var oldClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
		var newClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage { Content = new ByteArrayContent([1, 2, 3]) }));
		gateway.Attach(oldClient, "http://old");
		gateway.Attach(newClient, "http://new");

		gateway.Detach(oldClient);
		var stream = new MemoryStream();
		await gateway.DownloadBlobAsync("b", stream, CancellationToken.None);

		Assert.Equal([1, 2, 3], stream.ToArray());

		gateway.Detach(newClient);
		await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));
	}
}
