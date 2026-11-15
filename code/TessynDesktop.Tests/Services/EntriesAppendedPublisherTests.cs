using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// P1.F: <c>entries.appended</c> beside <c>session.appended</c>, inline up to a byte cap, and the file id of <c>session.reset</c>,
/// published from real appends to a real session file indexed by the real content store.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class EntriesAppendedPublisherTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

	private readonly EntryIdsEnv _env = new();
	private readonly RemoteEventBus _bus = new();
	private readonly TreeVersionTracker _treeVersion = new();
	private readonly IDaemonClient _daemon;
	private readonly FakeDaemonProxy _fake;
	private readonly RemoteNotificationPublisher _sut;
	private readonly List<RemoteNotification> _received = [];

	public EntriesAppendedPublisherTests()
	{
		_daemon = FakeDaemonProxy.Create(out _fake);
		_fake.ByFileName[_env.FileName] = _env.Session;
		_fake.ByNodeId[_env.Session.NodeId] = _env.Session;
		_bus.Published += n => { lock (_received) _received.Add(n); };

		_sut = NewPublisher(_env.Store);
		_sut.Start();
	}

	private RemoteNotificationPublisher NewPublisher(ISessionContentStore? store) =>
		new(_daemon, _env.Files, _bus, _treeVersion, _env.Epochs, new FakeStateBuilder(), store);

	public void Dispose()
	{
		_sut.Stop();
		_bus.Dispose();
		_env.Dispose();
	}

	/// <summary>Appends for real (so the file and the index change) and raises the daemon event the publisher listens to.</summary>
	private void Append(string role, string content, string? model = null)
	{
		_env.Files.AppendMessage(_env.FileName, role, content, modelId: model);
		_fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs
		{
			FileName = _env.FileName,
			Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = role, Content = content },
		});
	}

	private List<RemoteNotification> Received(string method)
	{
		lock (_received)
			return _received.Where(n => n.Method == method).ToList();
	}

	private List<RemoteNotification> WaitFor(string method, int count)
	{
		var sw = Stopwatch.StartNew();
		while (sw.Elapsed < Wait)
		{
			var found = Received(method);
			if (found.Count >= count)
				return found;

			Thread.Sleep(20);
		}

		return Received(method);
	}

	private static EntriesAppendedPayload Payload(RemoteNotification n) =>
		n.AsEntriesAppended() ?? throw new InvalidOperationException("not an entries.appended payload: " + n.Params);

	[Fact]
	public void Appends_in_one_window_publish_one_entries_appended_with_the_entries_inline()
	{
		Append(Constants.SessionFile.RoleUser, "one");
		Append(Constants.SessionFile.RoleAssistant, "two", "opus");
		Append(Constants.SessionFile.RoleUser, "three");

		var payload = Payload(Assert.Single(WaitFor(Constants.Remote.Events.EntriesAppended, 1)));
		Thread.Sleep(Constants.Remote.Timings.SessionAppendedWindow * 2);

		Assert.Single(Received(Constants.Remote.Events.EntriesAppended));
		Assert.Equal(_env.Session.NodeId, payload.NodeId);
		Assert.Equal(0, payload.FromSeq);
		Assert.Equal(3, payload.ToSeq);
		Assert.NotNull(payload.Entries);
		Assert.Equal(["one", "two", "three"], payload.Entries!.Select(e => e.Content));
		Assert.Equal(["user", "assistant", "user"], payload.Entries!.Select(e => e.Role));
		Assert.Equal([0, 1, 2], payload.Entries!.Select(e => e.Index));
		Assert.Equal(Enumerable.Range(0, 3).Select(i => payload.FileId + ":" + i), payload.Entries!.Select(e => e.EntryId));
		Assert.Equal("opus", payload.Entries![1].ModelId);
	}

	[Fact]
	public void It_is_published_beside_session_appended_which_keeps_its_old_shape()
	{
		Append(Constants.SessionFile.RoleUser, "one");

		WaitFor(Constants.Remote.Events.EntriesAppended, 1);
		var appended = Assert.Single(WaitFor(Constants.Remote.Events.SessionAppended, 1));

		Assert.Equal(["epoch", "nodeId"], appended.Params.AsObject().Select(p => p.Key).Order());
	}

	[Fact]
	public void The_inline_entries_equal_what_session_entries_returns_for_the_same_range()
	{
		for (var i = 0; i < 4; i++)
			Append(i % 2 == 0 ? Constants.SessionFile.RoleUser : Constants.SessionFile.RoleAssistant, $"body {i}");

		var payload = Payload(Assert.Single(WaitFor(Constants.Remote.Events.EntriesAppended, 1)));
		var page    = _env.Reader.GetPage(_env.Session.NodeId, _env.FileName, null, (int)payload.FromSeq - 1, 50);

		Assert.Equal(page.Entries, payload.Entries);
		Assert.Equal(page.FileId, payload.FileId);
		Assert.Equal(page.Seq, payload.ToSeq);
	}

	[Fact]
	public void The_next_window_starts_where_the_last_one_ended()
	{
		Append(Constants.SessionFile.RoleUser, "one");
		Append(Constants.SessionFile.RoleUser, "two");
		var first = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 1)[0]);

		Append(Constants.SessionFile.RoleUser, "three");
		var second = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 2)[1]);

		Assert.Equal((0L, 2L), (first.FromSeq, first.ToSeq));
		Assert.Equal((2L, 3L), (second.FromSeq, second.ToSeq));
		Assert.Equal(first.FileId, second.FileId);
		Assert.Equal(["three"], second.Entries!.Select(e => e.Content));
		Assert.EndsWith(":2", second.Entries![0].EntryId);
	}

	[Fact]
	public void A_big_entry_goes_ids_only_and_the_range_still_tells_what_to_fetch()
	{
		Append(Constants.SessionFile.RoleAssistant, new string('x', Constants.Remote.Limits.EntriesAppendedInlineMaxBytes + 10));

		var payload = Payload(Assert.Single(WaitFor(Constants.Remote.Events.EntriesAppended, 1)));

		Assert.Null(payload.Entries);
		Assert.Equal((0L, 1L), (payload.FromSeq, payload.ToSeq));
		Assert.False(payload.Entries is { Count: > 0 });
		Assert.False(Received(Constants.Remote.Events.EntriesAppended)[0].Params.AsObject().ContainsKey("entries"));

		// The client fetches them with afterSeq = fromSeq - 1... which is "afterSeq = fromSeq" in seq terms: it holds fromSeq entries.
		var fetched = _env.Reader.GetPage(_env.Session.NodeId, _env.FileName, null, (int)payload.FromSeq - 1, 10, Guid.ParseExact(payload.FileId, "N"));
		Assert.Single(fetched.Entries);
		Assert.Equal(payload.FileId + ":0", fetched.Entries[0].EntryId);
	}

	[Fact]
	public void Many_small_entries_over_the_entry_count_cap_go_ids_only()
	{
		// The burst must land in ONE coalescing window, or the test measures how fast the machine is, not the cap. Real appends and
		// their notifications are slow under load (a window may close between them), so: a primer entry publishes first (it seeds the
		// published watermark), then the burst is written to the file without notifications and the notifications are raised in a
		// tight loop of O(1) calls that cannot outlast the window.
		Append(Constants.SessionFile.RoleUser, "primer");
		WaitFor(Constants.Remote.Events.EntriesAppended, 1);

		var count = Constants.Remote.Limits.EntriesAppendedInlineMaxEntries + 1;
		for (var i = 0; i < count; i++)
			_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleUser, "x");
		for (var i = 0; i < count; i++)
			_fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs
			{
				FileName = _env.FileName,
				Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = Constants.SessionFile.RoleUser, Content = "x" },
			});

		var payload = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 2)[1]);

		Assert.Null(payload.Entries);
		Assert.Equal((1L, (long)count + 1), (payload.FromSeq, payload.ToSeq));
		Assert.Equal(2, Received(Constants.Remote.Events.EntriesAppended).Count);
	}

	[Fact]
	public void Entries_whose_total_size_is_over_the_byte_cap_go_ids_only()
	{
		var each = Constants.Remote.Limits.EntriesAppendedInlineMaxBytes / 3;
		for (var i = 0; i < 4; i++)
			Append(Constants.SessionFile.RoleUser, new string('y', each));

		var payload = Payload(Assert.Single(WaitFor(Constants.Remote.Events.EntriesAppended, 1)));

		Assert.Null(payload.Entries);
		Assert.Equal((0L, 4L), (payload.FromSeq, payload.ToSeq));
	}

	[Fact]
	public void The_first_range_is_exact_even_when_one_notification_covers_several_entries()
	{
		// Three entries reach the file, but only one notification is raised for them.
		_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleUser, "a");
		_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleUser, "b");
		_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleUser, "c");
		_fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs { FileName = _env.FileName, Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "c" } });
		var first = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 1)[0]);
		Assert.Equal(2L, first.FromSeq); // seeded just before the last entry
		Assert.Equal(3L, first.ToSeq);

		// A later notification continues from the published watermark, so nothing is missed.
		Append(Constants.SessionFile.RoleUser, "d");
		var second = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 2)[1]);
		Assert.Equal((3L, 4L), (second.FromSeq, second.ToSeq));
	}

	[Fact]
	public void Bookkeeping_is_dropped_when_the_session_is_gone()
	{
		Append(Constants.SessionFile.RoleUser, "x");
		WaitFor(Constants.Remote.Events.EntriesAppended, 1);

		_fake.ByFileName.Clear();
		Append(Constants.SessionFile.RoleUser, "y");
		Thread.Sleep(Constants.Remote.Timings.SessionAppendedWindow * 3);

		var gate = typeof(RemoteNotificationPublisher).GetField("_publishedSeq", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
		var pending = typeof(RemoteNotificationPublisher).GetField("_pendingAppends", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
		Assert.Empty((System.Collections.IDictionary)gate.GetValue(_sut)!);
		Assert.Empty((System.Collections.IDictionary)pending.GetValue(_sut)!);
	}

	[Fact]
	public void The_first_publication_after_a_restart_counts_back_from_the_end_of_the_file()
	{
		_env.WriteConversation(5);
		_env.Restart();
		using var bus = new RemoteEventBus();
		var received = new List<RemoteNotification>();
		bus.Published += n => { lock (received) received.Add(n); };
		var daemon = FakeDaemonProxy.Create(out var fake);
		fake.ByFileName[_env.FileName] = _env.Session;
		fake.ByNodeId[_env.Session.NodeId] = _env.Session;
		var publisher = new RemoteNotificationPublisher(daemon, _env.Files, bus, new TreeVersionTracker(), _env.Epochs, new FakeStateBuilder(), _env.Store);
		publisher.Start();

		_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleUser, "six");
		fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs { FileName = _env.FileName, Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "six" } });

		var sw = Stopwatch.StartNew();
		while (sw.Elapsed < Wait && !received.Any(n => n.Method == Constants.Remote.Events.EntriesAppended))
			Thread.Sleep(20);

		publisher.Stop();
		var payload = Payload(received.First(n => n.Method == Constants.Remote.Events.EntriesAppended));
		Assert.Equal((5L, 6L), (payload.FromSeq, payload.ToSeq));
		Assert.Equal(["six"], payload.Entries!.Select(e => e.Content));
	}

	[Fact]
	public void A_rewrite_publishes_session_reset_with_the_new_fileId_and_appends_continue_under_it()
	{
		Append(Constants.SessionFile.RoleUser, "one");
		var before = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 1)[0]);

		_env.Files.RewriteSessionFile(_env.FileName, EntryIdsEnv.EntryText("USER", 0, "kept") + EntryIdsEnv.EntryText("ASSISTANT", 1, "kept too"));
		var reset = Assert.Single(WaitFor(Constants.Remote.Events.SessionReset, 1)).AsSessionReset()!;

		Assert.Equal(_env.Session.NodeId, reset.NodeId);
		Assert.Equal(1, reset.Epoch);
		Assert.NotNull(reset.FileId);
		Assert.NotEqual(before.FileId, reset.FileId);
		Assert.Equal(reset.FileId, _env.Reader.GetPage(_env.Session.NodeId, _env.FileName, null, null, 5).FileId);

		Append(Constants.SessionFile.RoleUser, "after the rewrite");
		var after = Payload(WaitFor(Constants.Remote.Events.EntriesAppended, 2)[1]);

		Assert.Equal(reset.FileId, after.FileId);
		Assert.Equal((2L, 3L), (after.FromSeq, after.ToSeq));
		Assert.Equal(["after the rewrite"], after.Entries!.Select(e => e.Content));
	}

	[Fact]
	public void Turn_completed_never_overtakes_the_pending_entries_appended()
	{
		Append(Constants.SessionFile.RoleAssistant, "answer");
		_fake.Raise(nameof(IDaemonClient.TurnCompleted), new SessionTurnCompletedEventArgs { FileName = _env.FileName, Cancelled = false });

		WaitFor(Constants.Remote.Events.TurnCompleted, 1);
		var order = new List<string>();
		lock (_received)
			order = [.. _received.Select(n => n.Method)];

		Assert.Contains(Constants.Remote.Events.EntriesAppended, order);
		Assert.True(order.IndexOf(Constants.Remote.Events.EntriesAppended) < order.IndexOf(Constants.Remote.Events.TurnCompleted));
		Assert.True(order.IndexOf(Constants.Remote.Events.SessionAppended) < order.IndexOf(Constants.Remote.Events.TurnCompleted));
	}

	[Fact]
	public void Without_a_content_store_neither_entries_appended_nor_a_reset_fileId_is_published()
	{
		_sut.Stop();
		var plain = NewPublisher(store: null);
		plain.Start();
		try
		{
			Append(Constants.SessionFile.RoleUser, "one");
			WaitFor(Constants.Remote.Events.SessionAppended, 1);
			_env.Files.RewriteSessionFile(_env.FileName, EntryIdsEnv.EntryText("USER", 0, "x"));
			var reset = Assert.Single(WaitFor(Constants.Remote.Events.SessionReset, 1)).AsSessionReset()!;

			Thread.Sleep(Constants.Remote.Timings.SessionAppendedWindow * 2);
			Assert.Empty(Received(Constants.Remote.Events.EntriesAppended));
			Assert.Null(reset.FileId);
		}
		finally
		{
			plain.Stop();
		}
	}

	[Fact]
	public void An_event_for_an_unknown_session_publishes_nothing()
	{
		_fake.ByFileName.Clear();

		Append(Constants.SessionFile.RoleUser, "orphan");

		Thread.Sleep(Constants.Remote.Timings.SessionAppendedWindow * 2);
		Assert.Empty(Received(Constants.Remote.Events.EntriesAppended));
	}

	[Fact]
	public void An_append_event_when_nothing_new_is_in_the_file_publishes_nothing()
	{
		_env.WriteConversation(2);
		var _ = _env.Store.GetHeader(_env.FileName);

		// An event with no new entry behind it (for instance a duplicate notification): the id range is empty.
		_fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs { FileName = _env.FileName, Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "?" } });
		var first = WaitFor(Constants.Remote.Events.EntriesAppended, 1);

		// One pending notification against a file of 2 entries claims the last one as new: documented first-publication behaviour.
		Assert.Single(first);
		Assert.Equal((1L, 2L), (Payload(first[0]).FromSeq, Payload(first[0]).ToSeq));

		_fake.Raise(nameof(IDaemonClient.EntryAppended), new SessionEntryAppendedEventArgs { FileName = _env.FileName, Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "?" } });
		Thread.Sleep(Constants.Remote.Timings.SessionAppendedWindow * 3);
		Assert.Single(Received(Constants.Remote.Events.EntriesAppended));
	}
}
