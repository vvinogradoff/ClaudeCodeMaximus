using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The atomic <see cref="ISessionRegistry.CreateSessionAfter"/> behind <c>tree.createSession</c> (contract 3.5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRegistryCreateAfterTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AppSettingsService _real;
	private readonly SaveCountingAppSettings _settings;
	private readonly SessionRegistry _registry;
	private readonly List<TreeChangedEventArgs> _events = new();

	public SessionRegistryCreateAfterTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_createafter_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_real     = new AppSettingsService(_tempDir);
		_settings = new SaveCountingAppSettings(_real);
		_registry = new SessionRegistry(_settings, new FakeSessionFileService(), new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		_registry.TreeChanged += (_, e) => _events.Add(e);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private DirectoryNodeModel Dir(string name = "d") => _registry.AddDirectory(Path.Combine(_tempDir, name));

	[Fact]
	public void SessionAnchor_InsertsRightAfterIt_InTheSameContainer()
	{
		var dir = Dir();
		var a = _registry.AddSession(dir, "A");
		var b = _registry.AddSession(dir, "B");

		var placement = _registry.CreateSessionAfter(a.NodeId, "New");

		Assert.NotNull(placement);
		Assert.Equal(["A", "New", "B"], dir.Sessions.Select(s => s.Name));
		Assert.Equal(1, placement!.Index);
		Assert.Same(dir, placement.Directory);
		Assert.Null(placement.Group);
		Assert.Same(placement.Session, dir.Sessions[1]);
		Assert.Same(b, dir.Sessions[2]);
	}

	[Fact]
	public void SessionAnchor_InAGroup_InsertsIntoThatGroupsSessions()
	{
		var dir = Dir();
		var group = _registry.AddGroup(dir, "G");
		var a = _registry.AddSessionToGroup(group, "A");
		_registry.AddSessionToGroup(group, "B");

		var placement = _registry.CreateSessionAfter(a.NodeId, "New");

		Assert.Equal(["A", "New", "B"], group.Sessions.Select(s => s.Name));
		Assert.Same(group, placement!.Group);
		Assert.Same(dir, placement.Directory);
		Assert.Equal(group.WorkingDirectory, placement.Session.WorkingDirectory);
	}

	[Fact]
	public void LastSession_AsAnchor_AppendsAtTheEnd()
	{
		var dir = Dir();
		_registry.AddSession(dir, "A");
		var b = _registry.AddSession(dir, "B");

		_registry.CreateSessionAfter(b.NodeId, "New");

		Assert.Equal(["A", "B", "New"], dir.Sessions.Select(s => s.Name));
	}

	[Fact]
	public void WorkerAnchor_ResolvesToTheTopLevelSupervisor_SoTheNewSessionIsNotNestedAmongWorkers()
	{
		var dir = Dir();
		var sup = _registry.AddSession(dir, "Sup");
		var w1 = _registry.AddSession(dir, "W1");
		var w2 = _registry.AddSession(dir, "W2");
		var tail = _registry.AddSession(dir, "Tail");
		w1.SupervisorNodeId = sup.NodeId;
		w2.SupervisorNodeId = w1.NodeId;   // a worker of a worker: the chain still ends at Sup

		var placement = _registry.CreateSessionAfter(w2.NodeId, "New");

		Assert.Equal(["Sup", "New", "W1", "W2", "Tail"], dir.Sessions.Select(s => s.Name));
		Assert.Equal(1, placement!.Index);
		Assert.Null(placement.Session.SupervisorNodeId);
		Assert.Same(tail, dir.Sessions[4]);
	}

	[Fact]
	public void OrphanWorkerAnchor_IsItsOwnTopLevelSession()
	{
		var dir = Dir();
		var orphan = _registry.AddSession(dir, "Orphan");
		orphan.SupervisorNodeId = "missing";

		_registry.CreateSessionAfter(orphan.NodeId, "New");

		Assert.Equal(["Orphan", "New"], dir.Sessions.Select(s => s.Name));
	}

	[Fact]
	public void GroupAnchor_InsertsAtIndexZeroOfItsSessions_AndNotAmongTheGroups()
	{
		var dir = Dir();
		var group = _registry.AddGroup(dir, "G");
		var nested = _registry.AddGroupToGroup(group, "Nested");
		_registry.AddSessionToGroup(group, "A");

		var placement = _registry.CreateSessionAfter(group.NodeId, "New");

		Assert.Equal(["New", "A"], group.Sessions.Select(s => s.Name));
		Assert.Equal(0, placement!.Index);
		Assert.Same(group, placement.Group);
		Assert.Same(nested, Assert.Single(group.Groups));
	}

	[Fact]
	public void DirectoryAnchor_InsertsAtIndexZeroOfItsSessions()
	{
		var dir = Dir();
		_registry.AddSession(dir, "A");

		var placement = _registry.CreateSessionAfter(dir.NodeId, "New");

		Assert.Equal(["New", "A"], dir.Sessions.Select(s => s.Name));
		Assert.Equal(0, placement!.Index);
		Assert.Null(placement.Group);
		Assert.Equal(dir.Path, placement.Session.WorkingDirectory);
	}

	[Fact]
	public void UnknownAnchor_ReturnsNull_CreatesNothing_AndDoesNotSave()
	{
		var dir = Dir();
		_registry.AddSession(dir, "A");
		var saves = _settings.SaveCount;
		var events = _events.Count;

		var placement = _registry.CreateSessionAfter("nope", "New");

		Assert.Null(placement);
		Assert.Single(dir.Sessions);
		Assert.Equal(saves, _settings.SaveCount);
		Assert.Equal(events, _events.Count);
	}

	[Fact]
	public void Creating_SavesExactlyOnce_AndRaisesOneSessionAddedWithParentIndexAndPredecessor()
	{
		var dir = Dir();
		var a = _registry.AddSession(dir, "A");
		_registry.AddSession(dir, "B");
		var saves = _settings.SaveCount;
		_events.Clear();

		var placement = _registry.CreateSessionAfter(a.NodeId, "New");

		Assert.Equal(saves + 1, _settings.SaveCount);
		var e = Assert.Single(_events);
		Assert.Equal(TreeChangeKind.SessionAdded, e.Kind);
		Assert.Same(placement!.Session, e.Session);
		Assert.Same(dir, e.ParentDirectory);
		Assert.Null(e.ParentGroup);
		Assert.Equal(1, e.Index);
		Assert.Same(a, e.PrecedingSession);
	}

	[Fact]
	public void FirstPosition_RaisesTheEventWithoutAPredecessor()
	{
		var dir = Dir();

		_registry.CreateSessionAfter(dir.NodeId, "New");

		var e = Assert.Single(_events, x => x.Kind == TreeChangeKind.SessionAdded);
		Assert.Equal(0, e.Index);
		Assert.Null(e.PrecedingSession);
	}

	[Fact]
	public void NewSession_HasTheDesktopDefaults_AndIsPersisted()
	{
		var dir = Dir();

		var session = _registry.CreateSessionAfter(dir.NodeId, "New")!.Session;

		Assert.Equal("New", session.Name);
		Assert.Matches("^[0-9a-f]{32}$", session.NodeId);
		Assert.Matches("^[0-9a-f]{32}$", session.AgentToken);
		Assert.Null(session.ClaudeSessionId);
		Assert.Null(session.ExternalId);
		Assert.Null(session.ModelId);
		Assert.Null(session.ProfileName);
		Assert.Null(session.SupervisorNodeId);
		Assert.False(session.IsFavorite);
		Assert.False(session.IsStreamingMode);
		Assert.True(session.PlayCompletionSound);
		Assert.Equal(0, session.OrchestrationDepth);

		var reloaded = new AppSettingsService(_tempDir);
		reloaded.Load();
		Assert.Equal(session.NodeId, reloaded.Settings.Tree[0].Sessions[0].NodeId);
	}

	[Fact]
	public void AgentSpawn_StillRaisesSessionAddedWithoutIndex_SoTheDesktopKeepsAppending()
	{
		var dir = Dir();

		_registry.CreateSessionForAgent(dir.Path, "Spawned", null);

		var e = Assert.Single(_events, x => x.Kind == TreeChangeKind.SessionAdded);
		Assert.Null(e.Index);
		Assert.Null(e.PrecedingSession);
	}
}
