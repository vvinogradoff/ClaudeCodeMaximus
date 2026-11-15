using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>An in-memory stand-in for the web server's blob endpoint.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeBlobClient : IRelayBlobClient
{
	public byte[] Content { get; set; } = [];
	public Exception? Failure { get; set; }
	public bool Hang { get; set; }
	public string? LastBlobId { get; private set; }

	public async Task DownloadBlobAsync(string blobId, Stream destination, CancellationToken cancellationToken)
	{
		LastBlobId = blobId;
		if (Failure != null)
			throw Failure;
		if (Hang)
			await Task.Delay(Timeout.Infinite, cancellationToken);

		await destination.WriteAsync(Content, cancellationToken);
	}
}

/// <summary>
/// Attachments (decision A12): storage naming, import, fetch with size and hash checks, listing, and file.read on attachment ids
/// over loopback and relay. Real session registry and file-access service over temp folders.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();
	private readonly FakeBlobClient _blobs = new();
	private readonly AttachmentService _attachments;
	private readonly FileAccessService _files;
	private readonly TdcpDispatcher _dispatcher;
	private string? _profileName;

	public AttachmentTests()
	{
		_attachments = new AttachmentService(
			_env.Base.Registry, _env.Base.Settings, _ => _profileName, _blobs, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);
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

	// ── storage naming ───────────────────────────────────────────────────────

	[Fact]
	public void Import_stores_under_profile_and_node_with_a_timestamp_prefix()
	{
		var source = Source("report.md", Encoding.UTF8.GetBytes("# hi"));

		var stored = _attachments.Import(NodeId, source);

		var expected = Path.Combine(Root, "default", NodeId, "20261104-120000-report.md");
		Assert.Equal(expected, stored.Path);
		Assert.Equal("# hi", File.ReadAllText(expected));
		Assert.Equal("report.md", stored.Name);
		Assert.Equal(4, stored.Size);
		Assert.Equal("text/markdown", stored.Mime);
		Assert.StartsWith("att.", stored.AttachmentId);
		Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(expected)!, "*.part"));
		Assert.True(File.Exists(source), "the source stays where it was");
	}

	[Fact]
	public void The_same_name_in_the_same_second_gets_a_numeric_suffix()
	{
		var source = Source("a.txt", [1]);

		var first = _attachments.Import(NodeId, source);
		var second = _attachments.Import(NodeId, source);
		var third = _attachments.Import(NodeId, source);

		Assert.EndsWith("20261104-120000-a.txt", first.Path);
		Assert.EndsWith("20261104-120000-a-2.txt", second.Path);
		Assert.EndsWith("20261104-120000-a-3.txt", third.Path);
		Assert.Equal(3, new[] { first.AttachmentId, second.AttachmentId, third.AttachmentId }.Distinct().Count());
	}

	[Fact]
	public void A_later_second_does_not_collide()
	{
		var source = Source("a.txt", [1]);
		_attachments.Import(NodeId, source);
		_env.Clock.Advance(TimeSpan.FromSeconds(5));

		var next = _attachments.Import(NodeId, source);

		Assert.EndsWith("20261104-120005-a.txt", next.Path);
	}

	[Theory]
	[InlineData("..\\..\\evil.txt", "evil.txt")]
	[InlineData("../../evil.txt", "evil.txt")]
	[InlineData("a:b*c?.txt", "a_b_c_.txt")]
	[InlineData("con.txt", "_con.txt")]
	[InlineData("COM1", "_COM1")]
	[InlineData("name. . ", "name")]
	[InlineData("", "file")]
	[InlineData("???", "file")]
	[InlineData(".hidden", ".hidden")]
	public void Names_are_made_safe_for_the_file_system(string requested, string expected)
	{
		Assert.Equal(expected, AttachmentService.SafeFileName(requested));
	}

	[Fact]
	public void A_long_name_is_cut_but_keeps_its_extension()
	{
		var name = AttachmentService.SafeFileName(new string('x', 500) + ".png");

		Assert.Equal(120, name.Length);
		Assert.EndsWith(".png", name);
	}

	[Fact]
	public void A_profile_session_stores_under_its_profile_id()
	{
		_env.Base.Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "work-1", DisplayName = "Work" });
		_profileName = "Work";

		var stored = _attachments.Import(NodeId, Source("a.txt", [1]));

		Assert.Contains(Path.Combine("attachments", "work-1", NodeId), stored.Path);
	}

	[Fact]
	public void An_unknown_or_missing_profile_name_means_the_default_folder()
	{
		_profileName = "[Sys] someone@example.test";
		var viaDefaultLabel = _attachments.Import(NodeId, Source("a.txt", [1]));
		_profileName = "Gone";
		var viaUnknown = _attachments.Import(NodeId, Source("b.txt", [1]));

		Assert.Contains(Path.Combine("attachments", "default", NodeId), viaDefaultLabel.Path);
		Assert.Contains(Path.Combine("attachments", "default", NodeId), viaUnknown.Path);
	}

	[Fact]
	public void Import_refuses_a_missing_file_a_folder_and_credentials()
	{
		var key = Source(".env", Encoding.UTF8.GetBytes("A=1"));
		var pem = Source("server.pem", [1]);

		Assert.Equal("File not found", Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, Path.Combine(_env.Base.Root, "nope.txt"))).Message);
		Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, key));
		Assert.Throws<TdcpException>(() => _attachments.Import(NodeId, pem));
		Assert.Throws<TdcpInvalidParamsException>(() => _attachments.Import(NodeId, "\\\\server\\share\\x.txt"));
		Assert.Throws<TdcpException>(() => _attachments.Import("no-such-node", key));
		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);
	}

	// ── methods ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task Import_is_loopback_only_and_returns_the_attachment()
	{
		var source = Source("p.png", [1, 2, 3]);
		var args = new JsonObject { ["nodeId"] = NodeId, ["sourcePath"] = source }.ToJsonString();

		var relay = await CallAsync("attachment.import", args, TdcpTransport.Relay);
		var loopback = await CallAsync("attachment.import", args, TdcpTransport.Loopback);

		Assert.Equal(-32601, relay["error"]!["code"]!.GetValue<int>());
		var result = loopback["result"]!;
		Assert.Equal("p.png", (string?)result["name"]);
		Assert.Equal(3, (long?)result["size"]);
		Assert.Equal("image/png", (string?)result["mime"]);
		Assert.True(File.Exists((string?)result["path"]));
	}

	[Fact]
	public async Task List_returns_the_session_attachments_newest_first_and_hides_partial_files()
	{
		var older = _attachments.Import(NodeId, Source("old.txt", [1]));
		_env.Clock.Advance(TimeSpan.FromSeconds(10));
		var newer = _attachments.Import(NodeId, Source("new.txt", [1, 2]));
		File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(newer.Path)!, "leftover.part"), [9]);
		File.SetCreationTimeUtc(older.Path, DateTime.UtcNow.AddMinutes(-5));

		var response = await CallAsync("attachment.list", new JsonObject { ["nodeId"] = NodeId }.ToJsonString(), TdcpTransport.Relay);

		var items = response["result"]!["attachments"]!.AsArray();
		Assert.Equal(2, items.Count);
		Assert.Equal(["new.txt", "old.txt"], items.Select(i => (string)i!["name"]!));
		Assert.Equal(NodeId, (string?)response["result"]!["nodeId"]);
	}

	[Fact]
	public async Task List_of_a_session_without_attachments_is_empty_and_of_an_unknown_session_an_error()
	{
		var empty = await CallAsync("attachment.list", new JsonObject { ["nodeId"] = NodeId }.ToJsonString(), TdcpTransport.Relay);
		var unknown = await CallAsync("attachment.list", """{"nodeId":"nope"}""", TdcpTransport.Relay);

		Assert.Empty(empty["result"]!["attachments"]!.AsArray());
		Assert.Equal("Session not found", (string?)unknown["error"]!["message"]);
	}

	// ── attachment.fetch ─────────────────────────────────────────────────────

	private string FetchArgs(byte[] content, string name = "photo.png", string? sha = null, long? size = null) =>
		new JsonObject
		{
			["blobId"] = new string('a', 32), ["nodeId"] = NodeId, ["name"] = name,
			["size"] = size ?? content.Length, ["sha256"] = sha ?? Sha(content),
		}.ToJsonString();

	[Fact]
	public async Task Fetch_stores_the_blob_after_checking_size_and_hash()
	{
		var content = RandomNumberGenerator.GetBytes(300_000);
		_blobs.Content = content;

		var response = await CallAsync("attachment.fetch", FetchArgs(content), TdcpTransport.Relay);

		var result = response["result"]!;
		Assert.Equal(new string('a', 32), _blobs.LastBlobId);
		Assert.Equal(content, File.ReadAllBytes((string)result["path"]!));
		Assert.Equal("photo.png", (string?)result["name"]);
		Assert.Equal(300_000, (long?)result["size"]);
		Assert.Equal("image/png", (string?)result["mime"]);
		Assert.NotNull((string?)result["attachmentId"]);
	}

	[Fact]
	public async Task Fetch_accepts_an_upper_case_hash()
	{
		var content = new byte[] { 1, 2, 3 };
		_blobs.Content = content;

		var response = await CallAsync("attachment.fetch", FetchArgs(content, sha: Sha(content).ToUpperInvariant()), TdcpTransport.Relay);

		Assert.NotNull(response["result"]);
	}

	[Fact]
	public async Task Fetch_with_a_wrong_hash_stores_nothing()
	{
		_blobs.Content = [1, 2, 3];

		var response = await CallAsync("attachment.fetch", FetchArgs([1, 2, 3], sha: Sha([9, 9, 9])), TdcpTransport.Relay);

		Assert.Contains("SHA-256", (string?)response["error"]!["message"]);
		AssertNothingStored();
	}

	[Fact]
	public async Task Fetch_with_fewer_bytes_than_announced_stores_nothing()
	{
		_blobs.Content = [1, 2, 3];

		var response = await CallAsync("attachment.fetch", FetchArgs([1, 2, 3], size: 10), TdcpTransport.Relay);

		Assert.Contains("incomplete or the wrong size", (string?)response["error"]!["message"]);
		AssertNothingStored();
	}

	[Fact]
	public async Task Fetch_stops_a_blob_that_is_larger_than_announced()
	{
		_blobs.Content = new byte[1000];

		var response = await CallAsync("attachment.fetch", FetchArgs(new byte[1000], size: 10), TdcpTransport.Relay);

		Assert.Contains("larger than announced", (string?)response["error"]!["message"]);
		AssertNothingStored();
	}

	[Fact]
	public async Task Fetch_refuses_an_announced_size_over_the_limit_without_downloading()
	{
		var response = await CallAsync("attachment.fetch", FetchArgs([1], size: 200L * 1024 * 1024 + 1), TdcpTransport.Relay);

		Assert.Contains("200 MB", (string?)response["error"]!["message"]);
		Assert.Null(_blobs.LastBlobId);
	}

	[Fact]
	public async Task Fetch_reports_a_vanished_blob_and_an_unavailable_link_clearly()
	{
		_blobs.Failure = new TdcpException("The upload is no longer available on the web server");
		var gone = await CallAsync("attachment.fetch", FetchArgs([1]), TdcpTransport.Relay);

		var noLink = new AttachmentService(_env.Base.Registry, _env.Base.Settings, _ => null, blobs: null, dataRoot: _env.DataRoot, time: _env.Clock);
		var ex = await Assert.ThrowsAsync<TdcpException>(() => noLink.FetchAsync(NodeId, new string('a', 32), "x.txt", 1, Sha([1]), CancellationToken.None));

		Assert.Equal("The upload is no longer available on the web server", (string?)gone["error"]!["message"]);
		Assert.Equal("The web link is not connected", ex.Message);
		AssertNothingStored();
	}

	[Fact]
	public async Task Fetch_validates_its_parameters()
	{
		var badHash = await CallAsync("attachment.fetch", FetchArgs([1], sha: "xyz"), TdcpTransport.Relay);
		var noName = await CallAsync("attachment.fetch", """{"blobId":"b","nodeId":"n","size":1,"sha256":"x"}""", TdcpTransport.Relay);
		var noSession = await CallAsync("attachment.fetch",
			new JsonObject { ["blobId"] = "b", ["nodeId"] = "nope", ["name"] = "a", ["size"] = 1, ["sha256"] = Sha([1]) }.ToJsonString(), TdcpTransport.Relay);

		Assert.Equal(-32602, badHash["error"]!["code"]!.GetValue<int>());
		Assert.Equal(-32602, noName["error"]!["code"]!.GetValue<int>());
		Assert.Equal("Session not found", (string?)noSession["error"]!["message"]);
	}

	[Fact]
	public async Task A_stalled_download_ends_with_the_callers_cancellation_not_a_stored_file()
	{
		_blobs.Hang = true;
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => _attachments.FetchAsync(NodeId, new string('a', 32), "x.txt", 1, Sha([1]), cts.Token));

		AssertNothingStored();
	}

	[Fact]
	public async Task The_blob_gateway_without_a_connection_says_the_link_is_not_connected()
	{
		var gateway = new RelayBlobGateway();

		var ex = await Assert.ThrowsAsync<TdcpException>(() => gateway.DownloadBlobAsync("b", new MemoryStream(), CancellationToken.None));

		Assert.Equal("The web link is not connected", ex.Message);
	}

	private void AssertNothingStored() =>
		Assert.False(Directory.Exists(Root) && Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length > 0);

	// ── file.read on attachment ids ──────────────────────────────────────────

	[Theory]
	[InlineData(TdcpTransport.Loopback)]
	[InlineData(TdcpTransport.Relay)]
	public async Task File_read_accepts_an_attachment_id(TdcpTransport transport)
	{
		var stored = _attachments.Import(NodeId, Source("note.md", Encoding.UTF8.GetBytes("# Title\nbody")));

		var response = await CallAsync("file.read", new JsonObject { ["handle"] = stored.AttachmentId }.ToJsonString(), transport);

		var result = response["result"]!;
		Assert.Equal("# Title\nbody", (string?)result["data"]);
		Assert.Equal("utf8", (string?)result["encoding"]);
		Assert.EndsWith("-note.md", (string?)result["name"]);
		Assert.True((bool)result["eof"]!);
	}

	[Fact]
	public async Task File_read_on_an_image_attachment_is_base64_over_the_relay_in_the_strictest_mode()
	{
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.ReferencedOnly;
		var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
		var stored = _attachments.Import(NodeId, Source("pic.png", bytes));

		var response = await CallAsync("file.read", new JsonObject { ["handle"] = stored.AttachmentId }.ToJsonString(), TdcpTransport.Relay);

		Assert.Equal("base64", (string?)response["result"]!["encoding"]);
		Assert.Equal(bytes, Convert.FromBase64String((string)response["result"]!["data"]!));
		Assert.Equal("image/png", (string?)response["result"]!["mime"]);
	}

	[Fact]
	public async Task The_relay_policy_still_applies_to_attachments_the_loopback_does_not_apply_it()
	{
		var zip = _attachments.Import(NodeId, Source("bundle.zip", [1, 2, 3, 4]));
		var args = new JsonObject { ["handle"] = zip.AttachmentId }.ToJsonString();

		var relay = await CallAsync("file.read", args, TdcpTransport.Relay);
		var loopback = await CallAsync("file.read", args, TdcpTransport.Loopback);

		Assert.Contains("allowlist", (string?)relay["error"]!["message"]);
		Assert.Equal("base64", (string?)loopback["result"]!["encoding"]);
	}

	[Fact]
	public async Task An_oversized_attachment_is_refused_over_the_relay_by_the_type_cap()
	{
		_env.Base.Settings.Settings.FileAccess.MaxTextFileBytes = 10;
		var stored = _attachments.Import(NodeId, Source("big.txt", new byte[100]));

		var response = await CallAsync("file.read", new JsonObject { ["handle"] = stored.AttachmentId }.ToJsonString(), TdcpTransport.Relay);

		Assert.Contains("larger than", (string?)response["error"]!["message"]);
	}

	[Fact]
	public async Task Forged_attachment_ids_are_unknown_handles()
	{
		_attachments.Import(NodeId, Source("a.txt", [1]));
		string Forge(string path) => "att." + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		File.WriteAllText(Path.Combine(_env.DataRoot, "appsettings.json"), "{\"token\":\"secret\"}");

		var forged = new[]
		{
			Forge("../../appsettings.json"),
			Forge("default/../../appsettings.json"),
			Forge($"default/{NodeId}/../../../appsettings.json"),
			Forge($"default/{NodeId}"),
			Forge("default/" + NodeId + "/x/y"),
			Forge("/etc/passwd"),
			"att.!!!",
			"att.",
			"nonsense",
		};

		foreach (var id in forged)
		{
			var response = await CallAsync("file.read", new JsonObject { ["handle"] = id }.ToJsonString(), TdcpTransport.Relay);
			Assert.Equal("Unknown file handle", (string?)response["error"]!["message"]);
		}
	}

	[Fact]
	public async Task A_deleted_attachment_is_no_longer_a_handle()
	{
		var stored = _attachments.Import(NodeId, Source("a.txt", [1]));
		File.Delete(stored.Path);

		var response = await CallAsync("file.read", new JsonObject { ["handle"] = stored.AttachmentId }.ToJsonString(), TdcpTransport.Relay);

		Assert.Equal("Unknown file handle", (string?)response["error"]!["message"]);
	}

	[Fact]
	public async Task The_attachment_id_survives_a_restart()
	{
		var stored = _attachments.Import(NodeId, Source("a.txt", Encoding.UTF8.GetBytes("kept")));
		var restarted = new AttachmentService(_env.Base.Registry, _env.Base.Settings, _ => null, _blobs, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);

		Assert.True(restarted.TryResolve(stored.AttachmentId, out var node, out var path));
		Assert.Equal(NodeId, node);
		Assert.Equal(stored.Path, path);
	}

	[Fact]
	public async Task A_denied_name_inside_the_attachments_folder_is_still_refused_over_the_relay()
	{
		var secret = _attachments.Import(NodeId, Source("notes.txt", [1]));
		var renamed = Path.Combine(Path.GetDirectoryName(secret.Path)!, "20261104-120000-credentials.txt");
		File.WriteAllText(renamed, "x");
		var dto = _attachments.List(NodeId).Attachments.Single(a => a.Name == "credentials.txt");

		var response = await CallAsync("file.read", new JsonObject { ["handle"] = dto.AttachmentId }.ToJsonString(), TdcpTransport.Relay);

		Assert.Contains("cannot be read remotely", (string?)response["error"]!["message"]);
	}

	// ── defaults ─────────────────────────────────────────────────────────────

	[Fact]
	public void The_relay_mode_defaults_to_the_working_directory_and_media_hosts_have_their_defaults()
	{
		var settings = new AppSettingsModel();

		Assert.Equal(FileAccessMode.WorkingDirectory, settings.FileAccess.Mode);
		Assert.Equal(["annielartens.online", "*.githubusercontent.com"], settings.MediaPreviewHosts);
	}

	[Fact]
	public void A_settings_file_without_the_new_properties_gets_the_defaults_and_an_explicit_value_wins()
	{
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

		var bare = JsonSerializer.Deserialize<AppSettingsModel>("{}", options)!;
		var explicitMode = JsonSerializer.Deserialize<AppSettingsModel>("""{"FileAccess":{"Mode":0},"MediaPreviewHosts":["cdn.example.test"]}""", options)!;

		Assert.Equal(FileAccessMode.WorkingDirectory, bare.FileAccess.Mode);
		Assert.Equal(2, bare.MediaPreviewHosts.Count);
		Assert.Equal(FileAccessMode.ReferencedOnly, explicitMode.FileAccess.Mode);
		Assert.Equal(["cdn.example.test"], explicitMode.MediaPreviewHosts);
	}
}
