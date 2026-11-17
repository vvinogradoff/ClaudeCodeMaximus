using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>A sweep that flips the resumable flag of sessions publishes <c>session.changed</c> for each of them (and only for them).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ResumabilityPublisherTests : IDisposable
{
	private sealed class StubCache : ISessionResumabilityCache
	{
		public event Action<IReadOnlyList<string>>? Changed;

		public bool? TryGet(string nodeId) => null;

		public IReadOnlyList<string> Refresh() => [];

		public void Start()
		{
		}

		public void Dispose()
		{
		}

		public void Raise(params string[] nodeIds) => Changed?.Invoke(nodeIds);
	}

	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

	private readonly string _dir;
	private readonly RemoteEventBus _bus = new();
	private readonly StubCache _cache = new();
	private readonly RemoteNotificationPublisher _sut;
	private readonly List<RemoteNotification> _received = [];

	public ResumabilityPublisherTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "cm_test_resumable_publisher_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_dir);
		var settings = new AppSettingsService(_dir);
		settings.Settings.SessionFilesRoot = _dir;
		var files = new SessionFileService(settings);

		var daemon = FakeDaemonProxy.Create(out var fake);
		foreach (var id in new[] { "node-1", "node-2", "node-3" })
		{
			var node = new SessionNodeModel { Name = id, FileName = id + ".txt", NodeId = id };
			fake.ByFileName[node.FileName] = node;
			fake.ByNodeId[id] = node;
		}

		_bus.Published += n => { lock (_received) _received.Add(n); };
		using var epochs = new SessionEpochTracker(files);
		_sut = new RemoteNotificationPublisher(daemon, files, _bus, new TreeVersionTracker(), epochs, new FakeStateBuilder(), resumable: _cache);
		_sut.Start();
	}

	public void Dispose()
	{
		_sut.Stop();
		_bus.Dispose();
		Directory.Delete(_dir, recursive: true);
	}

	private List<string> WaitForChangedNodes(int count)
	{
		var sw = Stopwatch.StartNew();
		List<string> Nodes()
		{
			lock (_received)
				return _received.Where(n => n.Method == "session.changed").Select(n => n.Params["state"]?["id"]?.GetValue<string>() ?? string.Empty).ToList();
		}

		while (sw.Elapsed < Wait && Nodes().Count < count)
			Thread.Sleep(20);

		return Nodes();
	}

	[Fact]
	public void AFlippedSession_PublishesSessionChanged_ForEachFlippedSessionOnly()
	{
		_cache.Raise("node-1", "node-3");

		var nodes = WaitForChangedNodes(2);
		Thread.Sleep(300);

		Assert.Equal(["node-1", "node-3"], WaitForChangedNodes(2).OrderBy(x => x));
		Assert.DoesNotContain("node-2", nodes);
	}

	[Fact]
	public void AfterStop_ASweepPublishesNothing()
	{
		_sut.Stop();

		_cache.Raise("node-1");
		Thread.Sleep(400);

		Assert.Empty(WaitForChangedNodes(0));
	}
}
