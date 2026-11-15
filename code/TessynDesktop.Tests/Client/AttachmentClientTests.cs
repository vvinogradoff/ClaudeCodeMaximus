using TessynDesktop.Client;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// Attachments on <see cref="TessynClient"/> against the real <see cref="TdcpServer"/> over loopback HTTP and the real
/// <see cref="AttachmentService"/>: <c>attachment.import</c> (loopback only), <c>attachment.list</c> and a refused import as an RPC error.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentClientTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();
	private readonly AttachmentService _attachments;
	private readonly ClientTestHost _host;

	public AttachmentClientTests()
	{
		_attachments = new AttachmentService(_env.Base.Registry, _env.Base.Settings, _ => null, null, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log, contentStore: _env.Base.Store), new AttachmentRpcModule(_attachments)]));
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
	}

	private string Source(string name, string content)
	{
		var path = Path.Combine(_env.Base.Root, "sources", name);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
		return path;
	}

	[Fact]
	public async Task Import_copies_the_file_into_the_session_attachments_and_list_returns_it()
	{
		var source = Source("notes.md", "# notes");
		await using var client = new TessynClient(_host.Endpoint, ClientTestHost.Token);
		await client.ConnectAsync();

		var imported = await client.AttachmentImportAsync(_env.Session.NodeId, source);

		var expected = Path.Combine(_env.DataRoot, "attachments", "default", _env.Session.NodeId, "20261104-120000-notes.md");
		Assert.Equal(expected, imported.Path);
		Assert.Equal("notes.md", imported.Name);
		Assert.Equal(7, imported.Size);
		Assert.Equal("# notes", File.ReadAllText(expected));
		var list = await client.AttachmentListAsync(_env.Session.NodeId);
		Assert.Equal(_env.Session.NodeId, list.NodeId);
		Assert.Equal(imported.AttachmentId, Assert.Single(list.Attachments).AttachmentId);
	}

	[Fact]
	public async Task A_refused_import_is_an_rpc_error_and_stores_nothing()
	{
		var secret = Source("credentials.json", "{}");
		await using var client = new TessynClient(_host.Endpoint, ClientTestHost.Token);
		await client.ConnectAsync();

		await Assert.ThrowsAsync<TessynRpcException>(() => client.AttachmentImportAsync(_env.Session.NodeId, secret));
		await Assert.ThrowsAsync<TessynRpcException>(() => client.AttachmentImportAsync("no-such-session", Source("a.txt", "a")));
		await Assert.ThrowsAsync<TessynRpcException>(() => client.AttachmentImportAsync(_env.Session.NodeId, Path.Combine(_env.Base.Root, "sources")));

		Assert.Empty((await client.AttachmentListAsync(_env.Session.NodeId)).Attachments);
	}

	[Fact]
	public async Task Import_has_its_own_longer_timeout_than_ordinary_calls()
	{
		using var slow = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), new SlowAttachmentModule(TimeSpan.FromSeconds(1.5))]));
		var options = new TessynClientOptions { RequestTimeout = TimeSpan.FromMilliseconds(400), AttachmentImportTimeout = TimeSpan.FromSeconds(30) };
		await using var client = new TessynClient(slow.Endpoint, ClientTestHost.Token, options);
		await client.ConnectAsync();

		// The import answers after 1.5 s: more than the default timeout (0.4 s), well within its own.
		var imported = await client.AttachmentImportAsync("n", Path.Combine(Path.GetTempPath(), "big.bin"));
		Assert.Equal("att.slow", imported.AttachmentId);

		// An ordinary call with the same delay still times out on the default.
		await Assert.ThrowsAsync<TimeoutException>(() => client.AttachmentListAsync("n"));
	}
}
