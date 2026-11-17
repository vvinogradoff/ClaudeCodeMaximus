using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The resumable flag of a session (<c>SessionStateDto.Resumable</c>, the blue square of the Avalonia tree) comes from one cached batch sweep, and the snapshot flags the groups that
/// hold only workers (<c>TreeGroupDto.HoldsOnlyWorkers</c>, the husks the Avalonia tree prunes).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionResumabilityCacheTests : IDisposable
{
	private readonly RemoteTreeTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private SessionNodeModel AddRun(DirectoryNodeModel dir, string name, string claudeId, bool resumable)
	{
		var session = _env.AddSession(dir, name);
		session.ClaudeSessionId = claudeId;
		if (resumable)
			_env.Status.ResumableSessionIds.Add(claudeId);
		return session;
	}

	// ---- the cache ----

	[Fact]
	public void BeforeTheFirstSweep_NothingIsKnown_AndTheFirstSweepOnlySetsTheBaseline()
	{
		var dir = _env.AddDirectory("p");
		var yes = AddRun(dir, "Yes", "c-yes", resumable: true);
		var no = AddRun(dir, "No", "c-no", resumable: false);
		var never = _env.AddSession(dir, "Never ran");
		var changed = 0;
		_env.Resumable.Changed += ids => changed += ids.Count;

		Assert.Null(_env.Resumable.TryGet(yes.NodeId));

		var flipped = _env.Resumable.Refresh();

		Assert.Empty(flipped);
		Assert.Equal(0, changed);
		Assert.True(_env.Resumable.TryGet(yes.NodeId));
		Assert.False(_env.Resumable.TryGet(no.NodeId));
		Assert.False(_env.Resumable.TryGet(never.NodeId));   // no Claude session id: not resumable
		Assert.Null(_env.Resumable.TryGet("unknown"));
	}

	[Fact]
	public void ASweepReportsTheSessionsWhoseFlagFlipped_AndRaisesChangedOnce()
	{
		var dir = _env.AddDirectory("p");
		var a = AddRun(dir, "A", "c-a", resumable: true);
		var b = AddRun(dir, "B", "c-b", resumable: false);
		_env.Resumable.Refresh();
		var raised = new List<IReadOnlyList<string>>();
		_env.Resumable.Changed += raised.Add;

		_env.Status.ResumableSessionIds.Remove("c-a");
		_env.Status.ResumableSessionIds.Add("c-b");
		var flipped = _env.Resumable.Refresh();

		Assert.Equal(new[] { a.NodeId, b.NodeId }.OrderBy(x => x), flipped.OrderBy(x => x));
		Assert.Single(raised);
		Assert.False(_env.Resumable.TryGet(a.NodeId));
		Assert.True(_env.Resumable.TryGet(b.NodeId));
		Assert.Empty(_env.Resumable.Refresh());   // nothing changed: nothing raised
		Assert.Single(raised);
	}

	[Fact]
	public void ASessionAddedAfterASweep_IsNewNotFlipped_AndADeletedOneIsForgotten()
	{
		var dir = _env.AddDirectory("p");
		var gone = AddRun(dir, "Gone", "c-gone", resumable: true);
		_env.Resumable.Refresh();

		var added = AddRun(dir, "Added", "c-added", resumable: true);
		_env.Registry.RemoveSessionFromModel(gone);
		var flipped = _env.Resumable.Refresh();

		Assert.Empty(flipped);   // a new session is not a flip
		Assert.True(_env.Resumable.TryGet(added.NodeId));
		Assert.Null(_env.Resumable.TryGet(gone.NodeId));
	}

	[Fact]
	public void TheSweepIsOneBatch_NotAProbePerSession()
	{
		var dir = _env.AddDirectory("p");
		for (var i = 0; i < 20; i++)
			AddRun(dir, "S" + i, "c" + i, resumable: i % 2 == 0);

		_env.Resumable.Refresh();
		_env.Resumable.Refresh();

		Assert.Empty(_env.Status.ResumabilityChecks);   // IsSessionResumable (the per-session probe) was never called
	}

	[Fact]
	public void TheStatePutsTheFlagOnTheWire_OnlyOnceKnown()
	{
		var dir = _env.AddDirectory("p");
		var yes = AddRun(dir, "Yes", "c-yes", resumable: true);

		Assert.Null(_env.States.Build(yes).Resumable);   // before the first sweep: omitted
		_env.Resumable.Refresh();
		Assert.True(_env.States.Build(yes).Resumable);

		var withoutCache = new SessionStateBuilder(_env.Registry, new FakeStreamingManagerForTurnService(), _env.Attention, _env.Activity);
		Assert.Null(withoutCache.Build(yes).Resumable);
	}

	[Fact]
	public async Task TheSnapshotCarriesTheFlag()
	{
		var dir = _env.AddDirectory("p");
		var yes = AddRun(dir, "Yes", "c-yes", resumable: true);
		var no = AddRun(dir, "No", "c-no", resumable: false);
		_env.Resumable.Refresh();

		var result = await _env.CallAsync("tree.snapshot");

		var sessions = result["directories"]![0]!["sessions"]!.AsArray();
		Assert.True(sessions.Single(s => s!["id"]!.GetValue<string>() == yes.NodeId)!["state"]!["resumable"]!.GetValue<bool>());
		Assert.False(sessions.Single(s => s!["id"]!.GetValue<string>() == no.NodeId)!["state"]!["resumable"]!.GetValue<bool>());
	}

	// ---- husk groups ----

	private async Task<Dictionary<string, bool>> GroupFlagsAsync()
	{
		var result = await _env.CallAsync("tree.snapshot");
		var flags = new Dictionary<string, bool>();
		void Walk(JsonArray array)
		{
			foreach (var group in array)
			{
				flags[group!["name"]!.GetValue<string>()] = group["holdsOnlyWorkers"]!.GetValue<bool>();
				Walk(group["groups"]!.AsArray());
			}
		}

		Walk(result["directories"]![0]!["groups"]!.AsArray());
		return flags;
	}

	[Fact]
	public async Task AGroupThatHoldsOnlyWorkers_IsFlagged_ButNotOneThatWasCreatedEmptyOrHoldsARealSession()
	{
		var dir = _env.AddDirectory("p");
		var supervisor = _env.AddSession(dir, "Supervisor");
		var husk = _env.Registry.AddGroup(dir, "pilot-run");
		_env.AddSession(dir, "w1", husk, supervisor.NodeId);
		_env.AddSession(dir, "w2", husk, supervisor.NodeId);
		var mixed = _env.Registry.AddGroup(dir, "mixed");
		_env.AddSession(dir, "w3", mixed, supervisor.NodeId);
		_env.AddSession(dir, "Real", mixed);
		_env.Registry.AddGroup(dir, "Ideas");   // created empty by the user

		var flags = await GroupFlagsAsync();

		Assert.True(flags["pilot-run"]);
		Assert.False(flags["mixed"]);
		Assert.False(flags["Ideas"]);
	}

	[Fact]
	public async Task AGroupWithOnlyWorkersAndOnlyHuskSubgroups_IsAHuskToo_ButNotWhenASubgroupHoldsSomething()
	{
		var dir = _env.AddDirectory("p");
		var supervisor = _env.AddSession(dir, "Supervisor");
		var outer = _env.Registry.AddGroup(dir, "outer");
		_env.AddSession(dir, "w1", outer, supervisor.NodeId);
		var innerHusk = _env.Registry.AddGroupToGroup(outer, "inner-husk");
		_env.AddSession(dir, "w2", innerHusk, supervisor.NodeId);

		var outer2 = _env.Registry.AddGroup(dir, "outer2");
		_env.AddSession(dir, "w3", outer2, supervisor.NodeId);
		var inner2 = _env.Registry.AddGroupToGroup(outer2, "inner-real");
		_env.AddSession(dir, "Real", inner2);

		var flags = await GroupFlagsAsync();

		Assert.True(flags["outer"]);
		Assert.True(flags["inner-husk"]);
		Assert.False(flags["outer2"]);
		Assert.False(flags["inner-real"]);
	}
}
