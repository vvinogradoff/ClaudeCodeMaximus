using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Registry behaviour added for the remote API: stable node ids, reference-counted running flag, settings event.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRegistryRemoteTests : IDisposable
{
	private static readonly System.Text.RegularExpressions.Regex NodeIdFormat = new("^[0-9a-f]{32}$");

	private readonly string _tempDir;
	private readonly AppSettingsService _settings;
	private readonly SessionRegistry _registry;

	public SessionRegistryRemoteTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_remote_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_registry = new SessionRegistry(
			_settings,
			new FakeSessionFileService(),
			new FakeDirectoryLabelService(),
			new FakeClaudeSessionStatusService());
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private string AnyPath(string suffix) => Path.Combine(_tempDir, "dir" + suffix);

	// ── Node ids ─────────────────────────────────────────────────────────────────

	[Fact]
	public void LoadFromSettings_AssignsNodeIdsToDirectoriesAndGroupsAtEveryDepth_AndSavesOnce()
	{
		var inner = new GroupNodeModel { Name = "Inner" };
		var outer = new GroupNodeModel { Name = "Outer" };
		outer.Groups.Add(inner);
		var dir = new DirectoryNodeModel { Path = AnyPath("a") };
		dir.Groups.Add(outer);
		_settings.Settings.Tree.Add(dir);

		_registry.LoadFromSettings();

		Assert.Matches(NodeIdFormat, dir.NodeId);
		Assert.Matches(NodeIdFormat, outer.NodeId);
		Assert.Matches(NodeIdFormat, inner.NodeId);
		Assert.Equal(3, new[] { dir.NodeId, outer.NodeId, inner.NodeId }.Distinct().Count());

		var reloaded = new AppSettingsService(_tempDir);
		reloaded.Load();
		Assert.Equal(dir.NodeId, reloaded.Settings.Tree[0].NodeId);
		Assert.Equal(inner.NodeId, reloaded.Settings.Tree[0].Groups[0].Groups[0].NodeId);
	}

	[Fact]
	public void LoadFromSettings_KeepsExistingNodeIds_AndDoesNotSaveWhenNothingChanged()
	{
		var dir = new DirectoryNodeModel { Path = AnyPath("b"), NodeId = new string('a', 32) };
		var group = new GroupNodeModel { Name = "G", NodeId = new string('b', 32) };
		dir.Groups.Add(group);
		_settings.Settings.Tree.Add(dir);
		var settingsFile = Path.Combine(_tempDir, Constants.SettingsFileName);
		Assert.False(File.Exists(settingsFile));

		_registry.LoadFromSettings();

		Assert.Equal(new string('a', 32), dir.NodeId);
		Assert.Equal(new string('b', 32), group.NodeId);
		Assert.False(File.Exists(settingsFile));
	}

	[Fact]
	public void AddDirectory_AssignsANodeId()
	{
		var dir = _registry.AddDirectory(AnyPath("c"));

		Assert.Matches(NodeIdFormat, dir.NodeId);
	}

	[Fact]
	public void AddGroup_AndAddGroupToGroup_AssignNodeIds()
	{
		var dir = _registry.AddDirectory(AnyPath("d"));

		var group = _registry.AddGroup(dir, "G");
		var child = _registry.AddGroupToGroup(group, "Child");

		Assert.Matches(NodeIdFormat, group.NodeId);
		Assert.Matches(NodeIdFormat, child.NodeId);
		Assert.NotEqual(group.NodeId, child.NodeId);
	}

	[Fact]
	public void CreateSessionForAgent_CreatingAGroup_AssignsItANodeId()
	{
		var dir = _registry.AddDirectory(AnyPath("e"));

		var session = _registry.CreateSessionForAgent(dir.Path, "S", "Fresh group");

		Assert.NotNull(session);
		var group = Assert.Single(dir.Groups);
		Assert.Matches(NodeIdFormat, group.NodeId);
	}

	[Fact]
	public void FindDirectoryByNodeId_AndFindGroupByNodeId_FindNodesAtAnyDepth()
	{
		var dir = _registry.AddDirectory(AnyPath("f"));
		var group = _registry.AddGroup(dir, "G");
		var child = _registry.AddGroupToGroup(group, "Child");
		var grandchild = _registry.AddGroupToGroup(child, "Grandchild");

		Assert.Same(dir, _registry.FindDirectoryByNodeId(dir.NodeId));
		Assert.Same(group, _registry.FindGroupByNodeId(group.NodeId));
		Assert.Same(grandchild, _registry.FindGroupByNodeId(grandchild.NodeId));
		Assert.Null(_registry.FindDirectoryByNodeId("nope"));
		Assert.Null(_registry.FindGroupByNodeId("nope"));
		Assert.Null(_registry.FindGroupByNodeId(dir.NodeId));
	}

	[Fact]
	public void Lookups_TakeTheSettingsLock()
	{
		var dir = _registry.AddDirectory(AnyPath("g"));
		var released = new System.Threading.ManualResetEventSlim();
		var finished = new System.Threading.ManualResetEventSlim();

		lock (_settings.SyncRoot)
		{
			var lookup = System.Threading.Tasks.Task.Run(() =>
			{
				_registry.FindDirectoryByNodeId(dir.NodeId);
				finished.Set();
			});

			// While another thread holds the lock the lookup must wait.
			Assert.False(finished.Wait(TimeSpan.FromMilliseconds(200)));
			released.Set();
		}

		Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
	}

	[Fact]
	public void NodeId_RoundTripsThroughTheSettingsFile()
	{
		var dir = _registry.AddDirectory(AnyPath("h"));
		var group = _registry.AddGroup(dir, "G");

		var reloaded = new AppSettingsService(_tempDir);
		reloaded.Load();

		Assert.Equal(dir.NodeId, reloaded.Settings.Tree[0].NodeId);
		Assert.Equal(group.NodeId, reloaded.Settings.Tree[0].Groups[0].NodeId);
	}

	// ── Reference-counted running flag ───────────────────────────────────────────

	private List<TreeChangedEventArgs> CollectRunningEvents()
	{
		var events = new List<TreeChangedEventArgs>();
		_registry.TreeChanged += (_, e) =>
		{
			if (e.Kind == TreeChangeKind.SessionIsRunningChanged)
				events.Add(e);
		};
		return events;
	}

	[Fact]
	public void SetSessionRunning_TrueThenFalse_RaisesOnBothTransitions()
	{
		var events = CollectRunningEvents();

		_registry.SetSessionRunning("n1", true);
		Assert.True(_registry.IsSessionRunning("n1"));
		_registry.SetSessionRunning("n1", false);
		Assert.False(_registry.IsSessionRunning("n1"));

		Assert.Equal(2, events.Count);
	}

	[Fact]
	public void SetSessionRunning_NestedActivities_StayRunningUntilTheLastOneEnds_AndRaiseOnlyOnZeroOneTransitions()
	{
		var events = CollectRunningEvents();

		_registry.SetSessionRunning("n1", true);   // 0 -> 1: event
		_registry.SetSessionRunning("n1", true);   // 1 -> 2: no event
		_registry.SetSessionRunning("n1", false);  // 2 -> 1: no event, still running
		Assert.True(_registry.IsSessionRunning("n1"));
		Assert.Single(events);

		_registry.SetSessionRunning("n1", false);  // 1 -> 0: event
		Assert.False(_registry.IsSessionRunning("n1"));
		Assert.Equal(2, events.Count);
	}

	[Fact]
	public void SetSessionRunning_FalseWithoutAnOpenActivity_FloorsAtZero_WithoutAnEvent()
	{
		var events = CollectRunningEvents();

		_registry.SetSessionRunning("n1", false);
		_registry.SetSessionRunning("n1", false);

		Assert.False(_registry.IsSessionRunning("n1"));
		Assert.Empty(events);

		// The floor means a single true still starts a clean count.
		_registry.SetSessionRunning("n1", true);
		_registry.SetSessionRunning("n1", false);
		Assert.False(_registry.IsSessionRunning("n1"));
	}

	[Fact]
	public void SetSessionRunning_CountsPerSession()
	{
		_registry.SetSessionRunning("n1", true);
		_registry.SetSessionRunning("n2", true);
		_registry.SetSessionRunning("n1", false);

		Assert.False(_registry.IsSessionRunning("n1"));
		Assert.True(_registry.IsSessionRunning("n2"));
	}

	[Fact]
	public void SetSessionRunning_EventCarriesTheSessionWhenItExists()
	{
		var dir = _registry.AddDirectory(AnyPath("r"));
		var session = _registry.AddSession(dir, "S");
		var events = CollectRunningEvents();

		_registry.SetSessionRunning(session.NodeId, true);

		Assert.Same(session, Assert.Single(events).Session);
	}

	[Fact]
	public void SetSessionRunning_IsThreadSafe_BalancedCallsEndAtZero()
	{
		System.Threading.Tasks.Parallel.For(0, 2000, _ =>
		{
			_registry.SetSessionRunning("n1", true);
			_registry.SetSessionRunning("n1", false);
		});

		Assert.False(_registry.IsSessionRunning("n1"));
	}

	// ── Settings-changed event ───────────────────────────────────────────────────

	[Fact]
	public void RaiseSessionSettingsChanged_RaisesTheEventWithTheNodeId()
	{
		SessionSettingsChangedEventArgs? received = null;
		_registry.SessionSettingsChanged += (_, e) => received = e;

		_registry.RaiseSessionSettingsChanged("abc");

		Assert.Equal("abc", received?.NodeId);
	}
}
