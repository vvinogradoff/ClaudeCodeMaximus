using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The active workers badge (FR.9.12, FR.1.17; UC.AW.01, TC.AW.01): the registry counts the running workers below a session (direct and nested), the state carries the count as the additive
/// <c>runningWorkerCount</c> (P13) and the publisher refreshes the supervisors when a worker starts or stops.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ActiveWorkersTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

	private readonly RemoteTreeTestEnv _env = new();
	private readonly DirectoryNodeModel _dir;
	private readonly SessionNodeModel _sup;
	private readonly SessionNodeModel _w1;
	private readonly SessionNodeModel _w2;

	public ActiveWorkersTests()
	{
		_dir = _env.AddDirectory("proj");
		_sup = _env.AddSession(_dir, "Supervisor");
		_w1 = _env.AddSession(_dir, "W1", supervisorNodeId: _sup.NodeId);
		_w2 = _env.AddSession(_dir, "W2 (a worker of W1)", supervisorNodeId: _w1.NodeId);
	}

	public void Dispose() => _env.Dispose();

	[Fact]
	public void NothingRunning_CountsZero_AndTheStateOmitsTheField()
	{
		Assert.Equal(0, _env.Registry.CountRunningWorkers(_sup.NodeId));

		var state = _env.States.Build(_sup);
		Assert.Null(state.RunningWorkerCount);
		Assert.False(JsonSerializer.SerializeToNode(state, TdcpJsonOptions.Default)!.AsObject().ContainsKey("runningWorkerCount"));
	}

	[Fact]
	public void ADirectWorkerRunning_CountsForItsSupervisor_NotForItself()
	{
		_env.Registry.SetSessionRunning(_w1.NodeId, true);

		Assert.Equal(1, _env.Registry.CountRunningWorkers(_sup.NodeId));
		Assert.Equal(0, _env.Registry.CountRunningWorkers(_w1.NodeId));   // its own running flag is the session's, not a worker's
		Assert.Equal(1, _env.States.Build(_sup).RunningWorkerCount);
		Assert.True(_env.States.Build(_w1).Running);
	}

	[Fact]
	public void ANestedWorkerCounts_ForEverySupervisorAboveIt()
	{
		_env.Registry.SetSessionRunning(_w2.NodeId, true);

		Assert.Equal(1, _env.Registry.CountRunningWorkers(_w1.NodeId));
		Assert.Equal(1, _env.Registry.CountRunningWorkers(_sup.NodeId));

		_env.Registry.SetSessionRunning(_w1.NodeId, true);
		Assert.Equal(2, _env.Registry.CountRunningWorkers(_sup.NodeId));
		Assert.Equal(1, _env.Registry.CountRunningWorkers(_w1.NodeId));
	}

	[Fact]
	public void TheCount_FollowsWorkersStarting_AndFinishing()
	{
		_env.Registry.SetSessionRunning(_w1.NodeId, true);
		Assert.Equal(1, _env.Registry.CountRunningWorkers(_sup.NodeId));

		_env.Registry.SetSessionRunning(_w2.NodeId, true);
		Assert.Equal(2, _env.Registry.CountRunningWorkers(_sup.NodeId));

		_env.Registry.SetSessionRunning(_w1.NodeId, false);
		Assert.Equal(1, _env.Registry.CountRunningWorkers(_sup.NodeId));

		_env.Registry.SetSessionRunning(_w2.NodeId, false);
		Assert.Equal(0, _env.Registry.CountRunningWorkers(_sup.NodeId));
		Assert.Null(_env.States.Build(_sup).RunningWorkerCount);
	}

	[Fact]
	public void AWorkerWithSeveralOpenActivities_CountsOnce()
	{
		_env.Registry.SetSessionRunning(_w1.NodeId, true);
		_env.Registry.SetSessionRunning(_w1.NodeId, true);

		Assert.Equal(1, _env.Registry.CountRunningWorkers(_sup.NodeId));
	}

	[Fact]
	public void ASupervisorCycle_DoesNotHang_AndCountsEachSessionOnce()
	{
		_sup.SupervisorNodeId = _w2.NodeId;   // a corrupt tree: Sup -> W2 -> W1 -> Sup
		_env.Registry.SetSessionRunning(_w2.NodeId, true);

		var count = _env.Registry.CountRunningWorkers(_sup.NodeId);

		Assert.InRange(count, 0, 1);
	}

	[Fact]
	public void TheRunningWorkerCount_IsServedByTreeSnapshotAndWorkers()
	{
		_env.Registry.SetSessionRunning(_w1.NodeId, true);

		var snapshot = _env.States.Build(_sup);
		var json = JsonSerializer.SerializeToNode(snapshot, TdcpJsonOptions.Default)!.AsObject();
		Assert.Equal(1, json["runningWorkerCount"]!.GetValue<int>());
	}

	// ── publishing ────────────────────────────────────────────────────────────────

	private sealed class Publishing : IDisposable
	{
		private readonly RemoteEventBus _bus = new();
		private readonly List<RemoteNotification> _received = [];
		private readonly RemoteNotificationPublisher _sut;
		private readonly IDaemonClient _daemon;
		public FakeDaemonProxy Fake { get; }

		public Publishing(RemoteTreeTestEnv env, params SessionNodeModel[] nodes)
		{
			_daemon = FakeDaemonProxy.Create(out var fake);
			Fake = fake;
			foreach (var node in nodes)
			{
				fake.ByNodeId[node.NodeId] = node;
				fake.ByFileName[node.FileName] = node;
			}

			_bus.Published += n => { lock (_received) _received.Add(n); };
			var files = new SessionFileService(env.Settings);
			_sut = new RemoteNotificationPublisher(_daemon, files, _bus, new TreeVersionTracker(), new SessionEpochTracker(files), env.States);
			_sut.Start();
		}

		public IReadOnlyList<string> ChangedIds()
		{
			lock (_received)
				return _received.Where(n => n.Method == Constants.Remote.Events.SessionChanged).Select(n => n.Params["state"]!["id"]!.GetValue<string>()).ToList();
		}

		public bool WaitForIds(int count)
		{
			var deadline = DateTime.UtcNow + Wait;
			while (DateTime.UtcNow < deadline)
			{
				if (ChangedIds().Count >= count)
					return true;
				Thread.Sleep(20);
			}

			return false;
		}

		public IReadOnlyList<RemoteNotification> Changed()
		{
			lock (_received)
				return _received.Where(n => n.Method == Constants.Remote.Events.SessionChanged).ToList();
		}

		public void Dispose() => _sut.Stop();
	}

	[Fact]
	public void AWorkerStarting_PublishesSessionChangedForItsSupervisors_WithTheCount()
	{
		using var publishing = new Publishing(_env, _sup, _w1, _w2);

		_env.Registry.SetSessionRunning(_w2.NodeId, true);   // the registry's own state change first, as at run time
		publishing.Fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged, Session = _w2 });

		Assert.True(publishing.WaitForIds(3));
		var ids = publishing.ChangedIds();
		Assert.Contains(_w2.NodeId, ids);
		Assert.Contains(_w1.NodeId, ids);
		Assert.Contains(_sup.NodeId, ids);

		var supervisor = publishing.Changed().Single(n => n.Params["state"]!["id"]!.GetValue<string>() == _sup.NodeId);
		Assert.Equal(1, supervisor.Params["state"]!["runningWorkerCount"]!.GetValue<int>());
	}

	[Fact]
	public void AWorkerStopping_PublishesTheSupervisorWithoutTheField()
	{
		using var publishing = new Publishing(_env, _sup, _w1, _w2);
		_env.Registry.SetSessionRunning(_w1.NodeId, true);
		_env.Registry.SetSessionRunning(_w1.NodeId, false);

		publishing.Fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged, Session = _w1 });

		Assert.True(publishing.WaitForIds(2));
		var supervisor = publishing.Changed().Single(n => n.Params["state"]!["id"]!.GetValue<string>() == _sup.NodeId);
		Assert.Null(supervisor.Params["state"]!["runningWorkerCount"]);
	}

	[Fact]
	public void ABurstOfWorkersStartingAndStopping_IsCoalesced_OneSessionChangedPerSupervisor()
	{
		using var publishing = new Publishing(_env, _sup, _w1, _w2);
		_env.Registry.SetSessionRunning(_w1.NodeId, true);

		for (var i = 0; i < 40; i++)
			publishing.Fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged, Session = _w1 });

		Assert.True(publishing.WaitForIds(2));
		Thread.Sleep(Constants.Remote.Timings.SessionChangedWindow * 3);

		var ids = publishing.ChangedIds();
		Assert.Single(ids, id => id == _sup.NodeId);
		Assert.Single(ids, id => id == _w1.NodeId);
	}

	[Fact]
	public void ATopLevelSessionStarting_RefreshesOnlyItself()
	{
		using var publishing = new Publishing(_env, _sup, _w1, _w2);

		publishing.Fake.Raise(nameof(IDaemonClient.TreeChanged), new TreeChangedEventArgs { Kind = TreeChangeKind.SessionIsRunningChanged, Session = _sup });

		Assert.True(publishing.WaitForIds(1));
		Thread.Sleep(Constants.Remote.Timings.SessionChangedWindow * 3);
		Assert.Equal([_sup.NodeId], publishing.ChangedIds());
	}
}
