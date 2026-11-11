using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.32, TC.TG.33, TC.TG.34 — <see cref="SessionTagIndexBuilder"/> (FR.24.12, FR.24.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagIndexBuilderTests
{
	private const string DirA = @"C:\Proj\A";
	private const string DirB = @"C:\Proj\B";

	private static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

	private static SessionTagIndexSessionModel S(string id, string? supervisor = null, string dir = DirA) =>
		new(id, "name-" + id, supervisor, dir);

	private static SessionTagModel Declared(string key, DateTimeOffset? added = null, string? label = null) => new()
	{
		Kind = SessionTagKind.LinearIssue, Key = key, Label = label ?? key, Source = SessionTagSource.Session, AddedUtc = added ?? T0,
	};

	private static SessionTagModel Derived(string key, string? label = null) => new()
	{
		Kind = SessionTagKind.LinearIssue, Key = key, Label = label ?? key, Source = SessionTagSource.Name,
	};

	private static IReadOnlyList<SessionTagUsageModel> Build(
		IReadOnlyList<SessionTagIndexSessionModel> sessions, Dictionary<string, List<SessionTagModel>> tags, string? dir = null) =>
		SessionTagIndexBuilder.Build(sessions, s => tags.TryGetValue(s.NodeId, out var t) ? t : [], dir);

	private static SessionTagUsageModel Find(IReadOnlyList<SessionTagUsageModel> index, string key) =>
		index.Single(u => string.Equals(u.Tag.Key, key, StringComparison.OrdinalIgnoreCase));

	// ── Aggregation, dedupe, representative copy (TC.TG.32) ─────────────────────────

	[Fact]
	public void Build_SameKeyOnSeveralSessions_IsOneEntryWithAllNodeIds()   // TC.TG.32
	{
		var index = Build([S("a"), S("b"), S("c")], new()
		{
			["a"] = [Declared("HSE-1")],
			["b"] = [Declared("hse-1")],     // key case differs
			["c"] = [Declared("HSE-2")],
		});

		Assert.Equal(2, index.Count);
		Assert.Equal(["a", "b"], Find(index, "HSE-1").NodeIds);
		Assert.Equal(["c"], Find(index, "HSE-2").NodeIds);
	}

	[Fact]
	public void Build_NodeIdsAreDistinct_WhenWorkersAndSupervisorCarryTheSameTag()   // TC.TG.32
	{
		var index = Build([S("top"), S("w1", "top"), S("w2", "top")], new()
		{
			["top"] = [Declared("HSE-1")],
			["w1"]  = [Declared("HSE-1")],
			["w2"]  = [Declared("HSE-1")],
		});

		Assert.Equal(["top"], Assert.Single(index).NodeIds);
	}

	[Fact]
	public void Build_RepresentativeCopy_NameDerivedWinsOverDeclared_WhicheverComesFirst()   // TC.TG.32
	{
		var declaredFirst = new Dictionary<string, List<SessionTagModel>>
		{
			["a"] = [Declared("HSE-1", T0.AddDays(9), "declared")],
			["b"] = [Derived("HSE-1", "derived")],
		};
		var derivedFirst = new Dictionary<string, List<SessionTagModel>>
		{
			["a"] = [Derived("HSE-1", "derived")],
			["b"] = [Declared("HSE-1", T0.AddDays(9), "declared")],
		};

		Assert.Equal("derived", Assert.Single(Build([S("a"), S("b")], declaredFirst)).Tag.Label);
		Assert.Equal("derived", Assert.Single(Build([S("a"), S("b")], derivedFirst)).Tag.Label);
	}

	[Fact]
	public void Build_RepresentativeCopy_WithoutNameDerived_IsTheMostRecentlyAddedDeclared()   // TC.TG.32
	{
		var index = Build([S("a"), S("b"), S("c")], new()
		{
			["a"] = [Declared("HSE-1", T0.AddDays(1), "mid")],
			["b"] = [Declared("HSE-1", T0.AddDays(5), "newest")],
			["c"] = [Declared("HSE-1", T0, "oldest")],
		});

		Assert.Equal("newest", Assert.Single(index).Tag.Label);
	}

	[Fact]
	public void Build_SessionsWithoutTags_ProduceNoEntries()   // TC.TG.32
	{
		Assert.Empty(Build([S("a")], new()));
		Assert.Empty(SessionTagIndexBuilder.Build([], _ => [], null));
	}

	[Fact]
	public void Build_SessionsFromGroupsAndDirectLevel_AreAllIncluded()   // TC.TG.32
	{
		// Groups are flattened by the registry snapshot; the builder only sees the Directory node path.
		var index = Build([S("inGroup", dir: DirA), S("direct", dir: DirA)], new()
		{
			["inGroup"] = [Declared("HSE-1")],
			["direct"]  = [Declared("HSE-1")],
		}, DirA);

		Assert.Equal(["inGroup", "direct"], Assert.Single(index).NodeIds);
	}

	// ── Worker roll-up (TC.TG.33) ───────────────────────────────────────────────────

	[Fact]
	public void Build_WorkerTagsCountForTheTopLevelSupervisor_OverSeveralLevels()   // TC.TG.33
	{
		var index = Build([S("top"), S("mid", "top"), S("leaf", "mid")], new()
		{
			["leaf"] = [Declared("HSE-9")],
		});

		Assert.Equal(["top"], Assert.Single(index).NodeIds);
	}

	[Fact]
	public void Build_MissingSupervisor_StopsAtTheLastSessionFound()   // TC.TG.33
	{
		var index = Build([S("mid", "ghost"), S("leaf", "mid")], new()
		{
			["leaf"] = [Declared("HSE-9")],
		});

		Assert.Equal(["mid"], Assert.Single(index).NodeIds);
	}

	[Fact]
	public void Build_SupervisorCycle_EverySessionInTheCycleRollsUpToTheLowestNodeId()   // TC.TG.33
	{
		var sessions = new[] { S("b", "a"), S("a", "b") };
		foreach (var carrier in new[] { "a", "b" })
		{
			var index = Build(sessions, new() { [carrier] = [Declared("HSE-9")] });
			Assert.Equal(["a"], Assert.Single(index).NodeIds);
		}
	}

	[Fact]
	public void Build_SessionLeadingIntoACycle_RollsUpToTheSameIdAsTheCycleMembers()   // TC.TG.33
	{
		var index = Build([S("c", "a"), S("a", "b"), S("b", "a")], new()
		{
			["c"] = [Declared("HSE-9")],
			["b"] = [Declared("HSE-9")],
		});

		Assert.Equal(["a"], Assert.Single(index).NodeIds);
	}

	[Fact]
	public void Build_NodeIds_KeepFirstSeenOrder()   // TC.TG.32
	{
		var index = Build([S("z"), S("m"), S("a"), S("m2", "m")], new()
		{
			["z"]  = [Declared("HSE-1")],
			["m"]  = [Declared("HSE-1")],
			["a"]  = [Declared("HSE-1")],
			["m2"] = [Declared("HSE-1")],
		});

		Assert.Equal(["z", "m", "a"], Assert.Single(index).NodeIds);
	}

	[Fact]
	public void Build_SelfSupervisedSession_TerminatesAndCountsForItself()   // TC.TG.33
	{
		var index = Build([S("a", "a")], new() { ["a"] = [Declared("HSE-9")] });
		Assert.Equal(["a"], Assert.Single(index).NodeIds);
	}

	// ── Scope (TC.TG.34) ────────────────────────────────────────────────────────────

	[Fact]
	public void Build_NullDirectory_MeansEveryDirectory()   // TC.TG.34
	{
		var index = Build([S("a", dir: DirA), S("b", dir: DirB)], new()
		{
			["a"] = [Declared("HSE-1")],
			["b"] = [Declared("HSE-2")],
		}, dir: null);

		Assert.Equal(2, index.Count);
	}

	[Fact]
	public void Build_DirectoryScope_KeepsOnlyThatDirectory_CaseInsensitively()   // TC.TG.34
	{
		var tags = new Dictionary<string, List<SessionTagModel>>
		{
			["a"] = [Declared("HSE-1")],
			["b"] = [Declared("HSE-2")],
		};

		var index = Build([S("a", dir: DirA), S("b", dir: DirB)], tags, dir: DirA.ToLowerInvariant());

		Assert.Equal("HSE-1", Assert.Single(index).Tag.Key);
	}

	[Fact]
	public void Build_WorkerInAnotherDirectory_CountsUnderItsSupervisorsDirectory()   // TC.TG.34
	{
		var sessions = new[] { S("top", dir: DirA), S("worker", "top", DirB) };
		var tags = new Dictionary<string, List<SessionTagModel>> { ["worker"] = [Declared("HSE-9")] };

		var inA = Build(sessions, tags, DirA);
		var inB = Build(sessions, tags, DirB);

		Assert.Equal(["top"], Assert.Single(inA).NodeIds);
		Assert.Empty(inB);
	}

	[Fact]
	public void Build_ReadsTagsOnlyForSessionsInScope()   // TC.TG.34
	{
		var read = new List<string>();
		SessionTagIndexBuilder.Build([S("a", dir: DirA), S("b", dir: DirB)], s => { read.Add(s.NodeId); return []; }, DirA);
		Assert.Equal(["a"], read);
	}
}
