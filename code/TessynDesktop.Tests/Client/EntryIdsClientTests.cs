using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// P1.F / P1.G on <see cref="TessynClient"/> against the real <see cref="TdcpServer"/> over loopback HTTP, over a real session
/// file and content store: the new calls, the typed <c>entries.appended</c> payload and the whole chain from an append to the
/// client's notification.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class EntryIdsClientTests : IDisposable
{
	private static readonly TessynClientOptions FastRetry = new()
	{
		InitialBackoff = TimeSpan.FromMilliseconds(50),
		MaxBackoff     = TimeSpan.FromMilliseconds(200),
	};

	private readonly EntryIdsEnv _env = new();
	private readonly ClientTestHost _host;

	public EntryIdsClientTests()
	{
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log, contentStore: _env.Store), _env.Module]));
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
	}

	private TessynClient NewClient() => new(_host.Endpoint, ClientTestHost.Token, FastRetry);

	[Fact]
	public async Task Hello_announces_the_entry_ids_capability_when_a_content_store_is_wired()
	{
		await using var client = NewClient();

		await client.ConnectAsync();

		Assert.Contains(RemoteProtocol.Capabilities.EntriesIds, client.ServerInfo!.Capabilities);
	}

	[Fact]
	public async Task A_daemon_without_a_content_store_does_not_announce_entry_ids()
	{
		using var plain = new ClientTestHost();
		await using var client = new TessynClient(plain.Endpoint, ClientTestHost.Token, FastRetry);

		await client.ConnectAsync();

		Assert.DoesNotContain(RemoteProtocol.Capabilities.EntriesIds, client.ServerInfo!.Capabilities);
	}

	[Fact]
	public async Task The_existing_calls_return_the_new_fields_and_the_old_overloads_are_unchanged()
	{
		_env.WriteConversation(5);
		await using var client = NewClient();
		await client.ConnectAsync();

		var get  = await client.SessionGetAsync(_env.Session.NodeId);
		var page = await client.SessionEntriesAsync(_env.Session.NodeId, limit: 2);
		var full = await client.SessionEntryAsync(_env.Session.NodeId, 4, page.Epoch);

		Assert.Equal(5, get.Seq);
		Assert.Equal(page.FileId, get.FileId);
		Assert.Equal(["m3", "m4"], page.Entries.Select(e => e.Content));
		Assert.Equal(5, page.Seq);
		Assert.Equal($"{page.FileId}:4", full.EntryId);
		Assert.Equal("m4", full.Content);
	}

	[Fact]
	public async Task SessionEntries_with_a_query_pages_by_seq_and_by_entry_id()
	{
		_env.WriteConversation(8);
		await using var client = NewClient();
		await client.ConnectAsync();
		var node = _env.Session.NodeId;

		var forward = await client.SessionEntriesAsync(node, new SessionEntriesQuery(AfterSeq: 5));
		Assert.Equal(["m5", "m6", "m7"], forward.Entries.Select(e => e.Content));

		var guarded = await client.SessionEntriesAsync(node, new SessionEntriesQuery(AfterSeq: 6, FileId: forward.FileId, Limit: 1));
		Assert.Equal(["m6"], guarded.Entries.Select(e => e.Content));

		var backward = await client.SessionEntriesAsync(node, new SessionEntriesQuery(BeforeEntryId: forward.Entries[0].EntryId, Limit: 3));
		Assert.Equal(["m2", "m3", "m4"], backward.Entries.Select(e => e.Content));

		var newest = await client.SessionEntriesAsync(node, new SessionEntriesQuery(Limit: 2));
		Assert.Equal(["m6", "m7"], newest.Entries.Select(e => e.Content));

		var stale = await Assert.ThrowsAsync<TessynRpcException>(() =>
			client.SessionEntriesAsync(node, new SessionEntriesQuery(AfterSeq: 1, FileId: new string('f', 32))));
		Assert.Equal("Session history changed", stale.Message);

		var invalid = await Assert.ThrowsAsync<TessynRpcException>(() =>
			client.SessionEntriesAsync(node, new SessionEntriesQuery(AfterSeq: 1, AfterIndex: 1)));
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, invalid.Code);
	}

	[Fact]
	public async Task SessionEntryById_returns_the_entry_and_slices_with_a_range()
	{
		var body = string.Concat(Enumerable.Range(0, 30).Select(i => $"w{i:D2} "));
		_env.WriteRaw(EntryIdsEnv.EntryText("ASSISTANT", 0, body));
		await using var client = NewClient();
		await client.ConnectAsync();
		var node = _env.Session.NodeId;
		var id = (await client.SessionEntriesAsync(node)).Entries[0].EntryId!;

		var whole = await client.SessionEntryByIdAsync(node, id);
		Assert.Equal(body, whole.Content);
		Assert.Equal(id, whole.EntryId);
		Assert.Null(whole.ContentOffset);

		var slice = await client.SessionEntryByIdAsync(node, id, new EntryRangeParams(Offset: 8, Length: 8));
		Assert.Equal(body.Substring(8, 8), slice.Content);
		Assert.Equal(8, slice.ContentOffset);
		Assert.Equal(body.Length, slice.FullLength);
		Assert.True(slice.Truncated);

		var defaultLength = await client.SessionEntryByIdAsync(node, id, new EntryRangeParams(Offset: 4));
		Assert.Equal(body[4..], defaultLength.Content);
	}

	[Fact]
	public async Task SessionEntryById_after_a_rewrite_fails_with_history_changed()
	{
		_env.WriteConversation(3);
		await using var client = NewClient();
		await client.ConnectAsync();
		var id = (await client.SessionEntriesAsync(_env.Session.NodeId)).Entries[0].EntryId!;

		_env.Files.RewriteSessionFile(_env.FileName, EntryIdsEnv.ConversationEntry(0));

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.SessionEntryByIdAsync(_env.Session.NodeId, id));
		Assert.Equal("Session history changed", error.Message);
	}

	[Fact]
	public async Task SessionHeaders_round_trips_on_loopback_and_lists_missing_ids()
	{
		_env.WriteConversation(6);
		await using var client = NewClient();
		await client.ConnectAsync();

		var result = await client.SessionHeadersAsync([_env.Session.NodeId, "unknown"]);

		var header = Assert.Single(result.Headers);
		Assert.Equal(_env.Session.NodeId, header.NodeId);
		Assert.Equal(6, header.EntryCount);
		Assert.Equal(6, header.Seq);
		Assert.Equal((await client.SessionGetAsync(_env.Session.NodeId)).FileId, header.FileId);
		Assert.Equal(["unknown"], result.Missing);
	}

	[Fact]
	public async Task The_typed_payload_helpers_parse_the_events_and_ignore_other_ones()
	{
		_env.WriteConversation(2);
		await using var client = NewClient();
		await client.ConnectAsync();
		var probe = new ClientProbe(client);
		var payload = new EntriesAppendedPayload(_env.Session.NodeId, new string('d', 32), 2, 4);

		_host.Bus.Publish(RemoteProtocol.Events.EntriesAppended, payload);
		_host.Bus.Publish(RemoteProtocol.Events.SessionReset, new SessionResetPayload(_env.Session.NodeId, 3, new string('e', 32)));

		var appended = await probe.NextNotificationAsync();
		var reset    = await probe.NextNotificationAsync();

		Assert.Equal(payload, appended.AsEntriesAppended());
		Assert.Null(appended.AsSessionReset());
		Assert.Null(reset.AsEntriesAppended());
		Assert.Equal(new SessionResetPayload(_env.Session.NodeId, 3, new string('e', 32)), reset.AsSessionReset());
	}

	[Fact]
	public async Task A_malformed_entries_appended_payload_parses_to_null()
	{
		var broken = new RemoteNotification(RemoteProtocol.Events.EntriesAppended, System.Text.Json.Nodes.JsonNode.Parse("{\"nodeId\":1,\"fromSeq\":\"x\"}")!);

		Assert.Null(broken.AsEntriesAppended());
		await Task.CompletedTask;
	}

	[Fact]
	public async Task An_append_reaches_the_client_as_entries_appended_and_the_client_can_catch_up_by_seq()
	{
		_env.WriteConversation(3);
		var daemon = FakeDaemonProxy.Create(out var fake);
		fake.ByFileName[_env.FileName] = _env.Session;
		fake.ByNodeId[_env.Session.NodeId] = _env.Session;
		var publisher = new RemoteNotificationPublisher(daemon, _env.Files, _host.Bus, new TreeVersionTracker(), _env.Epochs, new FakeStateBuilder(), _env.Store);
		publisher.Start();
		try
		{
			await using var client = NewClient();
			await client.ConnectAsync();
			var probe = new ClientProbe(client);
			var held = await client.SessionEntriesAsync(_env.Session.NodeId);

			_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleUser, "fresh");
			fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs
			{
				FileName = _env.FileName,
				Entry    = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "fresh" },
			});

			EntriesAppendedPayload? payload = null;
			while (payload == null)
				payload = (await probe.NextNotificationAsync()).AsEntriesAppended();

			Assert.Equal(held.FileId, payload.FileId);
			Assert.Equal(held.Seq, payload.FromSeq);
			Assert.Equal(held.Seq + 1, payload.ToSeq);
			Assert.Equal(["fresh"], payload.Entries!.Select(e => e.Content));

			// Ids-only fallback path: the same range through afterSeq equals the inline entries.
			var caughtUp = await client.SessionEntriesAsync(_env.Session.NodeId, new SessionEntriesQuery(AfterSeq: payload.FromSeq, FileId: payload.FileId));
			Assert.Equal(payload.Entries, caughtUp.Entries);
		}
		finally
		{
			publisher.Stop();
		}
	}
}
