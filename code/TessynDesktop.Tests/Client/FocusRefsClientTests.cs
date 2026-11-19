using System.Text.Json.Nodes;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// Focus references (step 1) on <see cref="TessynClient"/> against the real <see cref="TdcpServer"/> over loopback HTTP: a
/// <c>post_attention</c> with refs, then <c>attention.list</c>, <c>file.read</c>, <c>file.preview</c> and <c>session.entries</c>
/// with <c>aroundEntryId</c> through the typed client.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusRefsClientTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;

	public FocusRefsClientTests()
	{
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log, contentStore: _env.Base.Store), .. _env.Modules]));
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
	}

	[Fact]
	public async Task Refs_from_post_attention_reach_the_typed_client_and_their_targets_can_be_read_and_opened()
	{
		var plan = _env.WriteFile("plan.md", "# The plan\n");
		_env.WriteBodies("u0", "a1", "u2", "The deployment failed with exit code 7.", "u4", "a5", "u6", $"The result is in {plan}.");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs(
			"Decide",
			FocusRefsEnv.EntryRef("failed with exit code 7", label: "error"),
			FocusRefsEnv.FileRef("plan.md", label: "plan")));
		await using var client = new TessynClient(_host.Endpoint, ClientTestHost.Token);
		await client.ConnectAsync();

		var list = await client.AttentionListAsync(_env.Session.NodeId);

		var refs = list.Items.Single().Refs!;
		Assert.Equal(2, refs.Count);
		Assert.Equal("entry", refs[0].Kind);
		Assert.Equal("error", refs[0].Label);
		Assert.Equal("failed with exit code 7".Length, refs[0].Length);
		Assert.Equal("plan.md", refs[1].Path);
		Assert.Null(refs[1].Url);

		var read = await client.FileReadAsync(refs[1].RefId!);
		Assert.Equal("# The plan\n", read.Data);
		Assert.True(read.Eof);

		var around = await client.SessionEntriesAsync(_env.Session.NodeId, new SessionEntriesQuery(AroundEntryId: refs[0].EntryId, Limit: 3));
		Assert.Equal(3, around.Entries.Count);
		Assert.Contains(around.Entries, e => e.EntryId == refs[0].EntryId && e.Content.Contains("exit code 7"));

		var withPath = (await client.SessionEntriesAsync(_env.Session.NodeId)).Entries.Single(e => e.Content.Contains(plan));
		var preview = await client.FilePreviewAsync(_env.Session.NodeId, withPath.EntryId!, plan);
		Assert.Equal("plan.md", preview.Name);
		Assert.Equal("# The", (await client.FileReadAsync(preview.Handle, 0, 5)).Data);
	}

	[Fact]
	public async Task FilePreviewForItem_sends_itemId_and_opens_a_path_written_in_the_item()
	{
		var plan = _env.WriteFile("plan.md", "# The plan\n");
		await _env.PostAsync(new JsonObject { ["kind"] = "ask", ["title"] = "Plan", ["body"] = "the plan is in docs-less plan.md here" });
		await using var client = new TessynClient(_host.Endpoint, ClientTestHost.Token);
		await client.ConnectAsync();
		var itemId = (await client.AttentionListAsync(_env.Session.NodeId)).Items.Single().Id;

		var preview = await client.FilePreviewForItemAsync(_env.Session.NodeId, itemId, "plan.md");
		Assert.Equal("plan.md", preview.Name);
		Assert.Equal("# The", (await client.FileReadAsync(preview.Handle, 0, 5)).Data);

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.FilePreviewForItemAsync(_env.Session.NodeId, itemId, plan + ".nope"));
		Assert.Contains("does not occur", error.Message);
	}

	[Fact]
	public async Task A_refused_read_is_a_rpc_error_on_the_client()
	{
		await using var client = new TessynClient(_host.Endpoint, ClientTestHost.Token);
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.FileReadAsync(new string('f', 32)));

		Assert.Contains("Unknown file handle", error.Message);
	}
}
