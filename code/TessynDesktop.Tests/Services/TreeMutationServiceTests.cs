using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The J1 tree mutation service over a real registry: validation, refusal reasons, the git-origin move rule and positions.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeMutationServiceTests : IDisposable
{
	private const string OriginA = "git@github.com:org/repo-a.git";
	private const string OriginB = "git@github.com:org/repo-b.git";

	private readonly TreeMutationTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	// ── addDirectory ─────────────────────────────────────────────────────────

	[Fact]
	public void AddDirectory_AddsAnExistingFolder_AndAnswersTheSameNodeForTheSamePath()
	{
		var path = Path.Combine(_env.Root, "proj");
		Directory.CreateDirectory(path);

		var first  = _env.Tree.AddDirectory(path);
		var second = _env.Tree.AddDirectory(path.ToUpperInvariant());

		Assert.Equal(first.DirectoryId, second.DirectoryId);
		Assert.Single(_env.Registry.Directories);
		Assert.Equal(path, first.Label);
	}

	[Fact]
	public void AddDirectory_RefusesAFolderThatDoesNotExist_WithoutThePathInTheMessage()
	{
		var path = Path.Combine(_env.Root, "missing-folder");

		var error = Assert.Throws<TdcpException>(() => _env.Tree.AddDirectory(path));

		Assert.Equal(Constants.TreeMutation.PathNotFound, error.Message);
		Assert.DoesNotContain("missing-folder", error.Message);
		Assert.Empty(_env.Registry.Directories);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void AddDirectory_RefusesABlankPath(string path) =>
		Assert.Throws<TdcpInvalidParamsException>(() => _env.Tree.AddDirectory(path));

	[Theory]
	[InlineData(@"\\no-such-host-j1\share\proj")]
	[InlineData("//no-such-host-j1/share/proj")]
	[InlineData(@"\\?\C:\proj")]
	[InlineData(@"\\.\C:\proj")]
	[InlineData("proj")]
	[InlineData(@".\proj")]
	[InlineData(@"C:proj")]
	[InlineData("bad\0path")]
	public void AddDirectory_RefusesUncDevicePrefixAndRelativePaths_BeforeAnyDiskAccess_WithTheSafeMessage(string path)
	{
		var started = DateTime.UtcNow;

		var error = Assert.Throws<TdcpException>(() => _env.Tree.AddDirectory(path));

		Assert.Equal(Constants.TreeMutation.FolderRefused, error.Message);
		Assert.DoesNotContain("no-such-host", error.Message);
		Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "a network shape must be refused without a connection attempt");
		Assert.Empty(_env.Registry.Directories);
	}

	[Fact]
	public void AddDirectory_RefusesADriveRoot_TheProfileAndItsParents_AndTheDataRootAroundIt()
	{
		Directory.CreateDirectory(_env.Home);
		Directory.CreateDirectory(Path.Combine(_env.DataRoot, "attachments"));

		foreach (var path in new[] { Path.GetPathRoot(_env.Root)!, _env.Home, _env.Root, _env.DataRoot, Path.Combine(_env.DataRoot, "attachments") })
			Assert.Equal(Constants.TreeMutation.FolderRefused, Assert.Throws<TdcpException>(() => _env.Tree.AddDirectory(path)).Message);

		Assert.Empty(_env.Registry.Directories);
	}

	[Fact]
	public void AddDirectory_AFolderBelowTheProfileIsFine()
	{
		var path = Path.Combine(_env.Home, "proj");
		Directory.CreateDirectory(path);

		Assert.NotNull(_env.Tree.AddDirectory(path).DirectoryId);
	}

	[Fact]
	public void AddDirectory_ATrailingSeparatorIsTheSameNode()
	{
		var path = Path.Combine(_env.Root, "proj");
		Directory.CreateDirectory(path);

		var plain    = _env.Tree.AddDirectory(path);
		var trailing = _env.Tree.AddDirectory(path + Path.DirectorySeparatorChar);

		Assert.Equal(plain.DirectoryId, trailing.DirectoryId);
		Assert.Equal(path, Assert.Single(_env.Registry.Directories).Path);
	}

	[Fact]
	public void AddDirectory_RefusesAJunction()
	{
		var target = Path.Combine(_env.Root, "real");
		var link   = Path.Combine(_env.Root, "link");
		Directory.CreateDirectory(target);
		using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
			process.WaitForExit();
		Assert.True(Directory.Exists(link), "the junction could not be created on this machine");

		try
		{
			var error = Assert.Throws<TdcpException>(() => _env.Tree.AddDirectory(link));

			Assert.Equal(Constants.TreeMutation.FolderRefused, error.Message);
			Assert.DoesNotContain("link", error.Message);
		}
		finally
		{
			Directory.Delete(link);   // removes the junction only; the recursive clean-up of the temp dir must not walk through it
		}
	}

	// ── addGroup / renameNode ────────────────────────────────────────────────

	[Fact]
	public void AddGroup_UnderADirectory_AndUnderAGroup_TrimsTheName()
	{
		var dir = _env.AddDirectory("proj");

		var top   = _env.Tree.AddGroup(dir.NodeId, "  Top  ");
		var inner = _env.Tree.AddGroup(top.GroupId, "Inner");

		var group = Assert.Single(dir.Groups);
		Assert.Equal("Top", group.Name);
		Assert.Equal(inner.GroupId, Assert.Single(group.Groups).NodeId);
		Assert.Equal(dir.Path, group.Groups[0].WorkingDirectory);
	}

	[Fact]
	public void AddGroup_RefusesABlankNameAndAnUnknownParent()
	{
		var dir = _env.AddDirectory("proj");

		Assert.Throws<TdcpInvalidParamsException>(() => _env.Tree.AddGroup(dir.NodeId, "  "));
		var error = Assert.Throws<TdcpException>(() => _env.Tree.AddGroup("nope", "G"));
		Assert.Equal(Constants.TreeMutation.ParentNotFound, error.Message);
	}

	[Fact]
	public void RenameNode_RenamesASessionAndAGroup_ButNotADirectory()
	{
		var dir     = _env.AddDirectory("proj");
		var group   = _env.Registry.AddGroup(dir, "Old group");
		var session = _env.AddSession(dir, "Old session");

		Assert.Equal("New group", _env.Tree.RenameNode(group.NodeId, " New group ").Name);
		_env.Tree.RenameNode(session.NodeId, "New session");

		Assert.Equal("New group", group.Name);
		Assert.Equal("New session", session.Name);
		var refused = Assert.Throws<TdcpException>(() => _env.Tree.RenameNode(dir.NodeId, "x"));
		Assert.Equal(Constants.TreeMutation.DirectoryRenameRefused, refused.Message);
		Assert.Equal(Constants.TreeMutation.NodeNotFound, Assert.Throws<TdcpException>(() => _env.Tree.RenameNode("nope", "x")).Message);
		Assert.Throws<TdcpInvalidParamsException>(() => _env.Tree.RenameNode(session.NodeId, " "));
	}

	// ── deleteNode ───────────────────────────────────────────────────────────

	[Fact]
	public void DeleteNode_RemovesASessionWithoutAFile_AndAnEmptyGroupAtAnyDepth()
	{
		var dir     = _env.AddDirectory("proj");
		var outer   = _env.Registry.AddGroup(dir, "Outer");
		var inner   = _env.Registry.AddGroupToGroup(outer, "Inner");
		var session = _env.AddSession(dir, "S");
		_env.Files.FileExists = false;

		Assert.True(_env.Tree.DeleteNode(session.NodeId, force: false).Deleted);
		Assert.True(_env.Tree.DeleteNode(inner.NodeId, force: false).Deleted);
		Assert.True(_env.Tree.DeleteNode(outer.NodeId, force: false).Deleted);

		Assert.Empty(dir.Sessions);
		Assert.Empty(dir.Groups);
	}

	[Fact]
	public void DeleteNode_ASessionWithAFile_IsBlockedUntilForced()
	{
		var dir     = _env.AddDirectory("proj");
		var session = _env.AddSession(dir, "S");

		var blocked = _env.Tree.DeleteNode(session.NodeId, force: false);
		var forced  = _env.Tree.DeleteNode(session.NodeId, force: true);

		Assert.Equal((false, RemoteProtocol.TreeMutation.BlockedBy.SessionFile), (blocked.Deleted, blocked.BlockedBy));
		Assert.True(forced.Deleted);
		Assert.Null(forced.BlockedBy);
		Assert.Empty(dir.Sessions);
	}

	[Fact]
	public void DeleteNode_ARunningSession_IsBlockedEvenWhenForced()
	{
		var dir     = _env.AddDirectory("proj");
		var session = _env.AddSession(dir, "S");
		_env.Registry.SetSessionRunning(session.NodeId, true);

		var result = _env.Tree.DeleteNode(session.NodeId, force: true);

		Assert.Equal((false, RemoteProtocol.TreeMutation.BlockedBy.Running), (result.Deleted, result.BlockedBy));
		Assert.Single(dir.Sessions);
	}

	[Fact]
	public void DeleteNode_ASupervisorWithWorkers_IsBlockedUntilForced()
	{
		var dir        = _env.AddDirectory("proj");
		var group      = _env.Registry.AddGroup(dir, "G");
		var supervisor = _env.AddSession(dir, "Supervisor");
		_env.AddSession(dir, "Worker", group, supervisorNodeId: supervisor.NodeId);
		_env.Files.FileExists = false;

		var blocked = _env.Tree.DeleteNode(supervisor.NodeId, force: false);
		var forced  = _env.Tree.DeleteNode(supervisor.NodeId, force: true);

		Assert.Equal(RemoteProtocol.TreeMutation.BlockedBy.Workers, blocked.BlockedBy);
		Assert.True(forced.Deleted);
	}

	[Fact]
	public void DeleteNode_ANonEmptyGroupOrDirectory_IsBlockedNotEmpty_AndAnEmptyDirectoryGoes()
	{
		var dir   = _env.AddDirectory("proj");
		var group = _env.Registry.AddGroup(dir, "G");
		_env.AddSession(dir, "S", group);

		var groupBlocked = _env.Tree.DeleteNode(group.NodeId, force: true);
		var dirBlocked   = _env.Tree.DeleteNode(dir.NodeId, force: true);
		var empty        = _env.AddDirectory("empty");
		var removed      = _env.Tree.DeleteNode(empty.NodeId, force: false);

		Assert.Equal(RemoteProtocol.TreeMutation.BlockedBy.NotEmpty, groupBlocked.BlockedBy);
		Assert.Equal(RemoteProtocol.TreeMutation.BlockedBy.NotEmpty, dirBlocked.BlockedBy);
		Assert.True(removed.Deleted);
		Assert.Single(_env.Registry.Directories);
	}

	[Fact]
	public void DeleteNode_AnUnknownNode_IsRefused() =>
		Assert.Equal(Constants.TreeMutation.NodeNotFound, Assert.Throws<TdcpException>(() => _env.Tree.DeleteNode("nope", false)).Message);

	// ── moveNode ─────────────────────────────────────────────────────────────

	[Fact]
	public void MoveNode_IntoAGroup_MovesTheSession_UpdatesItsWorkingDirectory_AndRaisesOneTreeChange()
	{
		var a     = _env.AddDirectory("a");
		var b     = _env.AddDirectory("b");
		var group = _env.Registry.AddGroup(b, "G");
		var s     = _env.AddSession(a, "S");
		var before = _env.TreeChanges;

		var result = _env.Tree.MoveNode(s.NodeId, group.NodeId, null);

		Assert.True(result.Moved);
		Assert.Empty(a.Sessions);
		Assert.Same(s, Assert.Single(group.Sessions));
		Assert.Equal(b.Path, s.WorkingDirectory);
		Assert.Equal(1, _env.TreeChanges - before);
	}

	[Fact]
	public void MoveNode_IndexCountsTopLevelSessionsOnly_AfterTheMovedSessionLeft()
	{
		var a = _env.AddDirectory("a");
		var b = _env.AddDirectory("b");
		var supervisor = _env.AddSession(b, "B1");
		_env.AddSession(b, "B1-worker", supervisorNodeId: supervisor.NodeId);
		_env.AddSession(b, "B2");
		var moved = _env.AddSession(a, "Moved");

		_env.Tree.MoveNode(moved.NodeId, b.NodeId, 1);

		// Top-level order is B1, Moved, B2: the worker stays next to its supervisor, the new session lands before B2.
		Assert.Equal(["B1", "B1-worker", "Moved", "B2"], TreeMutationTestEnv.Names(b.Sessions));
	}

	[Theory]
	[InlineData(null, new[] { "Moved", "X", "Y" })]
	[InlineData(0, new[] { "Moved", "X", "Y" })]
	[InlineData(1, new[] { "X", "Moved", "Y" })]
	[InlineData(2, new[] { "X", "Y", "Moved" })]
	[InlineData(99, new[] { "X", "Y", "Moved" })]
	public void MoveNode_WithinTheSameParent_ReordersWithTheIndexOfTheListWithoutTheMovedSession(int? index, string[] expected)
	{
		var dir = _env.AddDirectory("a");
		_env.AddSession(dir, "X");
		_env.AddSession(dir, "Y");
		var moved = _env.AddSession(dir, "Moved");
		// Start from "Moved" in the middle so a wrong index shows.
		dir.Sessions.Remove(moved);
		dir.Sessions.Insert(1, moved);

		_env.Tree.MoveNode(moved.NodeId, dir.NodeId, index);

		Assert.Equal(expected, TreeMutationTestEnv.Names(dir.Sessions));
	}

	[Fact]
	public void MoveNode_GitOriginRule_RefusesAnotherRepositoryAndAnOriginlessTarget_ButAllowsTheSameRepository()
	{
		var a     = _env.AddDirectory("a");
		var sameA = _env.AddDirectory("a-worktree");
		var b     = _env.AddDirectory("b");
		var plain = _env.AddDirectory("plain");
		_env.Origins.Origins[a.Path]     = OriginA;
		_env.Origins.Origins[sameA.Path] = OriginA.ToUpperInvariant();
		_env.Origins.Origins[b.Path]     = OriginB;
		var s = _env.AddSession(a, "S");

		var toOther = Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(s.NodeId, b.NodeId, null));
		var toPlain = Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(s.NodeId, plain.NodeId, null));
		_env.Tree.MoveNode(s.NodeId, sameA.NodeId, null);

		Assert.Equal(Constants.TreeMutation.GitOriginRefused, toOther.Message);
		Assert.Equal(Constants.TreeMutation.GitOriginRefused, toPlain.Message);
		Assert.Same(s, Assert.Single(sameA.Sessions));
		Assert.DoesNotContain(a.Path, toOther.Message);
	}

	[Fact]
	public void MoveNode_ASessionWithoutAnOrigin_MovesAnywhere_EvenIntoAGitDirectory()
	{
		var plain = _env.AddDirectory("plain");
		var git   = _env.AddDirectory("git");
		_env.Origins.Origins[git.Path] = OriginA;
		var s = _env.AddSession(plain, "S");

		_env.Tree.MoveNode(s.NodeId, git.NodeId, null);

		Assert.Same(s, Assert.Single(git.Sessions));
	}

	[Fact]
	public void MoveNode_Refusals_WorkerGroupUnknownTargetSessionTargetAndNegativeIndex()
	{
		var dir        = _env.AddDirectory("a");
		var group      = _env.Registry.AddGroup(dir, "G");
		var supervisor = _env.AddSession(dir, "Sup");
		var worker     = _env.AddSession(dir, "Worker", supervisorNodeId: supervisor.NodeId);
		var other      = _env.AddSession(dir, "Other");

		Assert.Equal(Constants.TreeMutation.WorkerMoveRefused, Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(worker.NodeId, dir.NodeId, null)).Message);
		Assert.Equal(Constants.TreeMutation.OnlySessionsMove, Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(group.NodeId, dir.NodeId, null)).Message);
		Assert.Equal(Constants.TreeMutation.NodeNotFound, Assert.Throws<TdcpException>(() => _env.Tree.MoveNode("nope", dir.NodeId, null)).Message);
		Assert.Equal(Constants.TreeMutation.ParentNotFound, Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(other.NodeId, "nope", null)).Message);
		Assert.Equal(Constants.TreeMutation.TargetMustBeContainer, Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(other.NodeId, supervisor.NodeId, null)).Message);
		Assert.Throws<TdcpInvalidParamsException>(() => _env.Tree.MoveNode(other.NodeId, dir.NodeId, -1));
	}

	[Fact]
	public void MoveNode_ARunningSession_IsRefused_AndStaysWhereItIs()
	{
		var a = _env.AddDirectory("a");
		var b = _env.AddDirectory("b");
		var s = _env.AddSession(a, "S");
		_env.Registry.SetSessionRunning(s.NodeId, true);

		var error = Assert.Throws<TdcpException>(() => _env.Tree.MoveNode(s.NodeId, b.NodeId, null));

		Assert.Equal(Constants.TreeMutation.SessionRunning, error.Message);
		Assert.Same(s, Assert.Single(a.Sessions));
		Assert.Empty(b.Sessions);
	}

	[Fact]
	public void MoveSession_ATargetGroupDeletedBetweenLookupAndMove_LosesNothing()
	{
		var a     = _env.AddDirectory("a");
		var group = _env.Registry.AddGroup(a, "G");
		var s     = _env.AddSession(a, "S");
		Assert.True(_env.Registry.TryDeleteGroup(a, group));

		var moved = _env.Registry.MoveSession(s, a, group, 0);

		Assert.False(moved);
		Assert.Same(s, Assert.Single(a.Sessions));
		Assert.Empty(group.Sessions);
	}

	[Fact]
	public void MoveSession_ADeletedTargetDirectory_LosesNothing_AndANestedGroupStillWorks()
	{
		var a      = _env.AddDirectory("a");
		var gone   = _env.AddDirectory("gone");
		var outer  = _env.Registry.AddGroup(a, "Outer");
		var inner  = _env.Registry.AddGroupToGroup(outer, "Inner");
		var s      = _env.AddSession(a, "S");
		Assert.True(_env.Registry.TryDeleteDirectory(gone));

		Assert.False(_env.Registry.MoveSession(s, gone, null, 0));
		Assert.True(_env.Registry.MoveSession(s, a, inner, 0));

		Assert.Same(s, Assert.Single(inner.Sessions));
		Assert.Empty(a.Sessions);
	}

	[Fact]
	public async Task MoveNode_TwoParallelMovesOfOneSession_LeaveItInExactlyOneContainer()
	{
		var a = _env.AddDirectory("a");
		var b = _env.AddDirectory("b");
		var c = _env.AddDirectory("c");
		var s = _env.AddSession(a, "S");

		await Task.WhenAll(
			Task.Run(() => _env.Tree.MoveNode(s.NodeId, b.NodeId, null)),
			Task.Run(() => _env.Tree.MoveNode(s.NodeId, c.NodeId, null)));

		Assert.Equal(1, _env.Registry.Directories.Sum(d => d.Sessions.Count(x => x == s)));
	}

	// ── moveTargets ──────────────────────────────────────────────────────────

	[Fact]
	public void MoveTargets_ListsEveryContainerTheRuleAllows_InTreeOrder()
	{
		var a     = _env.AddDirectory("a");
		var group = _env.Registry.AddGroup(a, "G");
		var inner = _env.Registry.AddGroupToGroup(group, "Inner");
		var same  = _env.AddDirectory("same");
		var b     = _env.AddDirectory("b");
		var plain = _env.AddDirectory("plain");
		_env.Origins.Origins[a.Path]    = OriginA;
		_env.Origins.Origins[same.Path] = OriginA;
		_env.Origins.Origins[b.Path]    = OriginB;
		var s = _env.AddSession(a, "S");

		var targets = _env.Tree.MoveTargets(s.NodeId).ParentIds;

		Assert.Equal([a.NodeId, group.NodeId, inner.NodeId, same.NodeId], targets);
		Assert.DoesNotContain(b.NodeId, targets);
		Assert.DoesNotContain(plain.NodeId, targets);
	}

	[Fact]
	public void MoveTargets_ASessionWithoutAnOrigin_MayGoAnywhere_AWorkerNowhere()
	{
		var a          = _env.AddDirectory("a");
		var b          = _env.AddDirectory("b");
		var supervisor = _env.AddSession(a, "Sup");
		var worker     = _env.AddSession(a, "Worker", supervisorNodeId: supervisor.NodeId);
		_env.Origins.Origins[b.Path] = OriginB;

		Assert.Equal([a.NodeId, b.NodeId], _env.Tree.MoveTargets(supervisor.NodeId).ParentIds);
		Assert.Empty(_env.Tree.MoveTargets(worker.NodeId).ParentIds);
		Assert.Equal(Constants.TreeMutation.OnlySessionsMove, Assert.Throws<TdcpException>(() => _env.Tree.MoveTargets(a.NodeId)).Message);
	}

	// ── nodeInfo / refreshGitOrigins ─────────────────────────────────────────

	[Fact]
	public void NodeInfo_AnswersKindPathWorkingDirectoryOriginAndLabel_InRequestOrder_AndSkipsUnknownIds()
	{
		var dir     = _env.AddDirectory("proj");
		var group   = _env.Registry.AddGroup(dir, "Group");
		var session = _env.AddSession(dir, "Session", group);
		_env.Origins.Origins[dir.Path] = OriginA;

		var items = _env.Tree.NodeInfo([session.NodeId, "unknown", dir.NodeId, group.NodeId]).Items;

		Assert.Equal(3, items.Count);
		Assert.Equal(new TreeNodeInfoItem(session.NodeId, "session", "Group / Session", dir.Path, OriginA, dir.Path), items[0]);
		Assert.Equal(new TreeNodeInfoItem(dir.NodeId, "directory", string.Empty, dir.Path, OriginA, dir.Path), items[1]);
		Assert.Equal(new TreeNodeInfoItem(group.NodeId, "group", "Group", dir.Path, OriginA, dir.Path), items[2]);
	}

	[Fact]
	public void NodeInfo_AcceptsFiveHundredIds_AndRefusesMore()
	{
		var ids = Enumerable.Range(0, Constants.TreeMutation.NodeInfoMaxIds).Select(i => "id" + i).ToList();

		Assert.Empty(_env.Tree.NodeInfo(ids).Items);
		ids.Add("one-more");
		Assert.Throws<TdcpInvalidParamsException>(() => _env.Tree.NodeInfo(ids));
	}

	[Fact]
	public void RefreshGitOrigins_StartsAScan_RaisesTreeChangedWhenDone_AndAnswersNullWhileOneRuns()
	{
		_env.AddDirectory("proj");
		var before = _env.TreeChanges;

		var started = _env.Tree.RefreshGitOrigins();
		_env.Origins.RefreshBusy = true;
		var busy = _env.Tree.RefreshGitOrigins();

		Assert.NotNull(started);
		Assert.Null(busy);
		Assert.Equal(1, _env.TreeChanges - before);
	}
}
