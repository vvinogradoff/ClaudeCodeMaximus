using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>An <see cref="IDaemonClient"/> that supports only what the publisher uses: its events and three lookups.</summary>
internal class FakeDaemonProxy : DispatchProxy
{
	private readonly Dictionary<string, Delegate?> _events = new();
	private int _lookups;
	private readonly System.Collections.Concurrent.ConcurrentBag<int> _lookupThreads = new();

	public Dictionary<string, SessionNodeModel> ByFileName { get; } = new();
	public Dictionary<string, SessionNodeModel> ByNodeId { get; } = new();
	public AttentionSummaryModel Summary { get; set; } = new();

	/// <summary>Number of FindSessionBy* / GetAttentionSummary calls so far.</summary>
	public int Lookups => Volatile.Read(ref _lookups);

	/// <summary>Managed thread ids the lookups ran on.</summary>
	public IReadOnlyCollection<int> LookupThreadIds => _lookupThreads;

	public static IDaemonClient Create(out FakeDaemonProxy fake)
	{
		var proxy = Create<IDaemonClient, FakeDaemonProxy>();
		fake = (FakeDaemonProxy)(object)proxy;
		return proxy;
	}

	public void Raise(string eventName, EventArgs args)
	{
		Delegate? handlers;
		lock (_events)
			_events.TryGetValue(eventName, out handlers);
		handlers?.DynamicInvoke(this, args);
	}

	public int SubscriberCount(string eventName)
	{
		lock (_events)
			return _events.TryGetValue(eventName, out var d) && d != null ? d.GetInvocationList().Length : 0;
	}

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		var name = targetMethod!.Name;
		if (name.StartsWith("add_", StringComparison.Ordinal))
		{
			lock (_events)
				_events[name[4..]] = Delegate.Combine(_events.GetValueOrDefault(name[4..]), (Delegate)args![0]!);
			return null;
		}

		if (name.StartsWith("remove_", StringComparison.Ordinal))
		{
			lock (_events)
				_events[name[7..]] = Delegate.Remove(_events.GetValueOrDefault(name[7..]), (Delegate)args![0]!);
			return null;
		}

		Interlocked.Increment(ref _lookups);
		_lookupThreads.Add(Environment.CurrentManagedThreadId);
		return name switch
		{
			nameof(IDaemonClient.FindSessionByFileName) => ByFileName.GetValueOrDefault((string)args![0]!),
			nameof(IDaemonClient.FindSessionByNodeId) => ByNodeId.GetValueOrDefault((string)args![0]!),
			nameof(IDaemonClient.GetAttentionSummary) => Summary,
			_ => throw new NotSupportedException(name),
		};
	}
}

internal sealed class FakeStateBuilder : ISessionStateBuilder
{
	public SessionStateDto Build(SessionNodeModel node) =>
		new(node.NodeId, Running: true, StreamingAlive: false, Attention: null, Favorite: false, LastActivityUtc: null, LastPromptUtc: null, HasDraft: false, ProfileName: null);
}

/// <remarks>Created by Claude</remarks>
public sealed class RemoteNotificationPublisherTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

	private readonly string _tempDir;
	private readonly SessionFileService _files;
	private readonly SessionEpochTracker _epochs;
	private readonly TreeVersionTracker _treeVersion = new();
	private readonly RemoteEventBus _bus = new();
	private readonly IDaemonClient _daemon;
	private readonly FakeDaemonProxy _fake;
	private readonly RemoteNotificationPublisher _sut;
	private readonly List<RemoteNotification> _received = new();
	private readonly SessionNodeModel _node = new() { Name = "S", FileName = "s.txt", NodeId = "node-1" };

	public RemoteNotificationPublisherTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_publisher_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
		_epochs = new SessionEpochTracker(_files);

		_daemon = FakeDaemonProxy.Create(out _fake);
		_fake.ByFileName[_node.FileName] = _node;
		_fake.ByNodeId[_node.NodeId] = _node;

		_bus.Published += n => { lock (_received) _received.Add(n); };
		_sut = new RemoteNotificationPublisher(_daemon, _files, _bus, _treeVersion, _epochs, new FakeStateBuilder());
		_sut.Start();
	}

	public void Dispose()
	{
		_sut.Stop();
		_bus.Dispose();
		_epochs.Dispose();
		Directory.Delete(_tempDir, recursive: true);
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

	private void AssertNone(string method, int forMs = 600)
	{
		Thread.Sleep(forMs);
		Assert.Empty(Received(method));
	}

	// ── tree.changed ─────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(TreeChangeKind.SettingsModified)]
	[InlineData(TreeChangeKind.SessionAdded)]
	[InlineData(TreeChangeKind.Reloaded)]
	public void StructuralTreeChange_PublishesTreeChangedWithTheNewVersion(TreeChangeKind kind)
	{
		_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = kind });

		var notification = Assert.Single(WaitFor(Constants.Remote.Events.TreeChanged, 1));
		Assert.Equal(1, notification.Params["version"]!.GetValue<long>());
		Assert.Equal(1, _treeVersion.Current);
	}

	[Fact]
	public void ManyTreeChangesInAWindow_BumpTheVersionEachTime_ButPublishOnceWithTheLatest()
	{
		for (var i = 0; i < 10; i++)
			_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });

		var all = WaitFor(Constants.Remote.Events.TreeChanged, 1);
		Thread.Sleep(Constants.Remote.Timings.TreeChangedWindow * 2);

		Assert.Single(Received(Constants.Remote.Events.TreeChanged));
		Assert.Equal(10, all[0].Params["version"]!.GetValue<long>());
	}

	[Fact]
	public void RunningStateChange_DoesNotBumpTheTreeVersion_ButPublishesSessionChanged()
	{
		_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged, Session = _node });

		var notification = Assert.Single(WaitFor(Constants.Remote.Events.SessionChanged, 1));
		Assert.Equal(0, _treeVersion.Current);
		Assert.Equal("node-1", notification.Params["state"]!["id"]!.GetValue<string>());
		Assert.True(notification.Params["state"]!["running"]!.GetValue<bool>());
		AssertNone(Constants.Remote.Events.TreeChanged, 400);
	}

	[Fact]
	public void RunningStateChangeWithoutASession_PublishesNothing()
	{
		_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged });

		AssertNone(Constants.Remote.Events.SessionChanged);
	}

	// ── session.appended / session.changed ───────────────────────────────────────

	private static SessionEntryAppendedEventArgs Appended(string fileName) =>
		new() { FileName = fileName, Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "ASSISTANT", Content = "x" } };

	[Fact]
	public void EntryAppended_PublishesSessionAppendedWithNodeIdAndEpoch_AndSessionChanged()
	{
		_fake.Raise(nameof(IDaemonClient.EntryAppended), Appended(_node.FileName));

		var appended = Assert.Single(WaitFor(Constants.Remote.Events.SessionAppended, 1));
		Assert.Equal("node-1", appended.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(0, appended.Params["epoch"]!.GetValue<int>());
		Assert.Single(WaitFor(Constants.Remote.Events.SessionChanged, 1));
	}

	[Fact]
	public void ManyAppends_AreCoalescedPerSession()
	{
		for (var i = 0; i < 25; i++)
			_fake.Raise(nameof(IDaemonClient.EntryAppended), Appended(_node.FileName));

		WaitFor(Constants.Remote.Events.SessionAppended, 1);
		Thread.Sleep(Constants.Remote.Timings.SessionAppendedWindow * 2);

		Assert.Single(Received(Constants.Remote.Events.SessionAppended));
		Assert.Single(Received(Constants.Remote.Events.SessionChanged));
	}

	[Fact]
	public void EntryAppendedForAnUnknownFile_PublishesNothing()
	{
		_fake.Raise(nameof(IDaemonClient.EntryAppended), Appended("unknown.txt"));

		AssertNone(Constants.Remote.Events.SessionAppended);
		AssertNone(Constants.Remote.Events.SessionChanged, 100);
	}

	[Fact]
	public void Handlers_DoNoLookupOnTheRaisingThread()
	{
		_fake.Raise(nameof(IDaemonClient.EntryAppended), Appended(_node.FileName));
		_fake.Raise(nameof(IDaemonClient.TurnCompleted), new SessionTurnCompletedEventArgs { FileName = _node.FileName });
		_fake.Raise(nameof(IDaemonClient.AttentionChanged), new AttentionChangedEventArgs(_node.NodeId));
		_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged, Session = _node });

		Thread.Sleep(Constants.Remote.Timings.SessionChangedWindow * 2);
		Assert.True(_fake.Lookups > 0, "the deferred factories should have run");
		Assert.DoesNotContain(Environment.CurrentManagedThreadId, _fake.LookupThreadIds);
	}

	// ── turn.completed ───────────────────────────────────────────────────────────

	[Fact]
	public void TurnCompleted_PublishesNodeIdCancelledAndError()
	{
		_fake.Raise(nameof(IDaemonClient.TurnCompleted), new SessionTurnCompletedEventArgs { FileName = _node.FileName, Cancelled = true, ErrorText = "limit reached" });

		var notification = Assert.Single(WaitFor(Constants.Remote.Events.TurnCompleted, 1));
		Assert.Equal("node-1", notification.Params["nodeId"]!.GetValue<string>());
		Assert.True(notification.Params["cancelled"]!.GetValue<bool>());
		Assert.Equal("limit reached", notification.Params["error"]!.GetValue<string>());
	}

	[Fact]
	public void TurnCompleted_WithoutAnError_OmitsIt_AndEveryCompletionIsItsOwnNotification()
	{
		_fake.Raise(nameof(IDaemonClient.TurnCompleted), new SessionTurnCompletedEventArgs { FileName = _node.FileName });
		_fake.Raise(nameof(IDaemonClient.TurnCompleted), new SessionTurnCompletedEventArgs { FileName = _node.FileName });

		var all = WaitFor(Constants.Remote.Events.TurnCompleted, 2);

		Assert.Equal(2, all.Count);
		Assert.False(all[0].Params.AsObject().ContainsKey("error"));
		Assert.False(all[0].Params["cancelled"]!.GetValue<bool>());
	}

	[Fact]
	public void TurnCompletedForAnUnknownFile_PublishesNothing()
	{
		_fake.Raise(nameof(IDaemonClient.TurnCompleted), new SessionTurnCompletedEventArgs { FileName = "unknown.txt" });

		AssertNone(Constants.Remote.Events.TurnCompleted);
	}

	// ── attention.changed ────────────────────────────────────────────────────────

	[Fact]
	public void AttentionChanged_PublishesTheSummary_AndSessionChanged()
	{
		_fake.Summary = new AttentionSummaryModel { OpenCount = 1, TopKind = AttentionKind.Ask, AllOpenCount = 2, AllOpenTopKind = AttentionKind.Critical };

		_fake.Raise(nameof(IDaemonClient.AttentionChanged), new AttentionChangedEventArgs("node-1"));

		var notification = Assert.Single(WaitFor(Constants.Remote.Events.AttentionChanged, 1));
		Assert.Equal("node-1", notification.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(1, notification.Params["summary"]!["openCount"]!.GetValue<int>());
		Assert.Equal("ask", notification.Params["summary"]!["topKind"]!.GetValue<string>());
		Assert.Equal("critical", notification.Params["summary"]!["allOpenTopKind"]!.GetValue<string>());
		Assert.Single(WaitFor(Constants.Remote.Events.SessionChanged, 1));
	}

	// ── tags.changed / index.changed ─────────────────────────────────────────────

	[Fact]
	public void TagsChanged_PublishesTheNodeId_OrNoneForAllSessions()
	{
		_fake.Raise(nameof(IDaemonClient.SessionTagsChanged), new SessionTagsChangedEventArgs("node-1"));
		_fake.Raise(nameof(IDaemonClient.SessionTagsChanged), new SessionTagsChangedEventArgs(null));

		var all = WaitFor(Constants.Remote.Events.TagsChanged, 2);

		Assert.Equal(2, all.Count);
		Assert.Contains(all, n => n.Params["nodeId"]?.GetValue<string>() == "node-1");
		Assert.Contains(all, n => !n.Params.AsObject().ContainsKey("nodeId"));
	}

	[Fact]
	public void SessionIndexed_IsDebouncedToOneIndexChangedPerTwoSeconds()
	{
		var sw = Stopwatch.StartNew();
		for (var i = 0; i < 10; i++)
			_fake.Raise(nameof(IDaemonClient.SessionIndexed), new SessionIndexedEventArgs());

		var all = WaitFor(Constants.Remote.Events.IndexChanged, 1);

		Assert.Single(all);
		Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1.8), $"published after {sw.Elapsed}");
		Assert.Empty(all[0].Params.AsObject());
	}

	// ── session settings and rewrites ────────────────────────────────────────────

	[Fact]
	public void SessionSettingsChanged_PublishesSessionChanged()
	{
		_fake.Raise(nameof(IDaemonClient.SessionSettingsChanged), new SessionSettingsChangedEventArgs { NodeId = "node-1" });

		var notification = Assert.Single(WaitFor(Constants.Remote.Events.SessionChanged, 1));
		Assert.Equal("node-1", notification.Params["state"]!["id"]!.GetValue<string>());
	}

	[Fact]
	public void FileRewrite_PublishesSessionResetWithTheBumpedEpoch()
	{
		var rewritten = new SessionNodeModel { Name = "R", FileName = _files.CreateSessionFile(), NodeId = "node-2" };
		_fake.ByFileName[rewritten.FileName] = rewritten;

		_files.RewriteSessionFile(rewritten.FileName, "[2026-11-12T10:00:00Z] USER\nx\n");
		_files.RewriteSessionFile(rewritten.FileName, "[2026-11-12T10:00:00Z] USER\ny\n");

		var all = WaitFor(Constants.Remote.Events.SessionReset, 1);
		Assert.NotEmpty(all);
		Assert.Equal("node-2", all[^1].Params["nodeId"]!.GetValue<string>());
		Thread.Sleep(200);
		Assert.Equal(2, Received(Constants.Remote.Events.SessionReset)[^1].Params["epoch"]!.GetValue<int>());
	}

	[Fact]
	public void FileRewriteOfAnUnknownFile_PublishesNothing()
	{
		var other = _files.CreateSessionFile();

		_files.RewriteSessionFile(other, "x");

		AssertNone(Constants.Remote.Events.SessionReset);
	}

	// ── Lifecycle and robustness ─────────────────────────────────────────────────

	[Fact]
	public void Start_IsIdempotent_AndStopUnsubscribesEverything()
	{
		_sut.Start();
		_sut.Start();

		Assert.Equal(1, _fake.SubscriberCount(nameof(IDaemonClient.TreeChanged)));

		_sut.Stop();

		foreach (var name in new[]
		{
			nameof(IDaemonClient.TreeChanged), nameof(IDaemonClient.EntryAppended), nameof(IDaemonClient.TurnCompleted),
			nameof(IDaemonClient.AttentionChanged), nameof(IDaemonClient.SessionTagsChanged), nameof(IDaemonClient.SessionIndexed),
			nameof(IDaemonClient.SessionSettingsChanged),
		})
			Assert.Equal(0, _fake.SubscriberCount(name));

		_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
		AssertNone(Constants.Remote.Events.TreeChanged);
	}

	[Fact]
	public void AHandlerFailure_NeverPropagatesToTheRaisingThread()
	{
		var broken = new RemoteNotificationPublisher(_daemon, _files, new ThrowingBus(), _treeVersion, _epochs, new FakeStateBuilder());
		broken.Start();

		_fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SettingsModified });
		_fake.Raise(nameof(IDaemonClient.EntryAppended), Appended("s.txt"));
		_fake.Raise(nameof(IDaemonClient.AttentionChanged), new AttentionChangedEventArgs("node-1"));

		broken.Stop();
	}

	private sealed class ThrowingBus : IRemoteEventBus
	{
		public event Action<RemoteNotification>? Published { add { } remove { } }
		public void Publish(string method, object payload) => throw new InvalidOperationException("boom");
		public void PublishCoalesced(string method, string key, Func<object?> payloadFactory, TimeSpan window) => throw new InvalidOperationException("boom");
	}
}
