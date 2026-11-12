using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>FR.1.9 roll-up of Worker Sessions onto their top-level session and FR.1.12 ranking.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeSearchRankerTests
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	private static RemoteSessionRecord Rec(string id, string? supervisor = null) => new(
		new SessionNodeModel { Name = id, FileName = id + ".txt", NodeId = id },
		id, id, supervisor, "dir", @"C:\dir", @"C:\dir", id, false, null);

	private static SessionSearchScoreModel Score(int strict, int loose) =>
		new() { ExternalId = "x", StrictMatchCount = strict, LooseMatchCount = loose };

	private static IReadOnlyList<Models.Remote.TreeSearchMatchDto> Rank(
		IReadOnlyList<RemoteSessionRecord> sessions,
		Dictionary<string, SessionSearchScoreModel> scores,
		Dictionary<string, DateTimeOffset>? lastPrompt = null) =>
		TreeSearchRanker.Rank(
			sessions,
			s => scores.GetValueOrDefault(s.NodeId),
			id => lastPrompt != null && lastPrompt.TryGetValue(id, out var t) ? t : null);

	[Fact]
	public void NoScores_NoMatches()
	{
		var result = Rank([Rec("a"), Rec("b")], []);

		Assert.Empty(result);
	}

	[Fact]
	public void TopLevelMatch_ReportsItselfWithItsOwnCounts()
	{
		var result = Rank([Rec("a"), Rec("b")], new() { ["b"] = Score(2, 5) });

		var match = Assert.Single(result);
		Assert.Equal("b", match.Id);
		Assert.Equal(2, match.Strict);
		Assert.Equal(5, match.Loose);
		Assert.True(match.MatchedSelf);
		Assert.Empty(match.WorkerIds);
	}

	[Fact]
	public void WorkerMatch_RollsUpOntoItsTopLevelSession_WithoutMatchedSelf()
	{
		var sessions = new[] { Rec("sup"), Rec("w1", "sup"), Rec("w2", "sup") };

		var result = Rank(sessions, new() { ["w2"] = Score(0, 3) });

		var match = Assert.Single(result);
		Assert.Equal("sup", match.Id);
		Assert.False(match.MatchedSelf);
		Assert.Equal(["w2"], match.WorkerIds);
		Assert.Equal(3, match.Loose);
	}

	[Fact]
	public void SupervisorAndWorkers_KeyIsTheBestOfThem_NotTheSum()
	{
		var sessions = new[] { Rec("sup"), Rec("w1", "sup"), Rec("w2", "sup") };

		var result = Rank(sessions, new()
		{
			["sup"] = Score(0, 9),
			["w1"]  = Score(1, 1),   // a strict match outranks any loose-only key
			["w2"]  = Score(0, 4),
		});

		var match = Assert.Single(result);
		Assert.Equal(1, match.Strict);
		Assert.Equal(1, match.Loose);
		Assert.True(match.MatchedSelf);
		Assert.Equal(["w1", "w2"], match.WorkerIds);
	}

	[Fact]
	public void NestedWorker_RollsUpToTheTopOfTheChain()
	{
		var sessions = new[] { Rec("top"), Rec("mid", "top"), Rec("leaf", "mid") };

		var result = Rank(sessions, new() { ["leaf"] = Score(1, 1) });

		var match = Assert.Single(result);
		Assert.Equal("top", match.Id);
		Assert.Equal(["leaf"], match.WorkerIds);
	}

	[Fact]
	public void OrphanWorker_IsItsOwnTopLevelSession()
	{
		var result = Rank([Rec("orphan", "gone")], new() { ["orphan"] = Score(1, 1) });

		var match = Assert.Single(result);
		Assert.Equal("orphan", match.Id);
		Assert.True(match.MatchedSelf);
	}

	[Fact]
	public void SupervisorCycle_TerminatesAndStillReportsTheMatch()
	{
		var sessions = new[] { Rec("a", "b"), Rec("b", "a") };

		var result = Rank(sessions, new() { ["a"] = Score(1, 1) });

		Assert.Single(result);
	}

	[Fact]
	public void Ranking_StrictTierFirst_ThenStrictCount_ThenLooseCount()
	{
		var sessions = new[] { Rec("looseOnly"), Rec("strict1"), Rec("strict3"), Rec("strict1MoreLoose") };

		var result = Rank(sessions, new()
		{
			["looseOnly"]       = Score(0, 50),
			["strict1"]         = Score(1, 1),
			["strict3"]         = Score(3, 3),
			["strict1MoreLoose"] = Score(1, 7),
		});

		Assert.Equal(["strict3", "strict1MoreLoose", "strict1", "looseOnly"], result.Select(m => m.Id));
	}

	[Fact]
	public void EqualCounts_NewestLastPromptFirst_ThenNullLast()
	{
		var sessions = new[] { Rec("noPrompt"), Rec("old"), Rec("new") };

		var result = Rank(
			sessions,
			new() { ["noPrompt"] = Score(1, 1), ["old"] = Score(1, 1), ["new"] = Score(1, 1) },
			new() { ["old"] = T0, ["new"] = T0.AddHours(1) });

		Assert.Equal(["new", "old", "noPrompt"], result.Select(m => m.Id));
	}

	[Fact]
	public void FullyEqualKeys_KeepTreeOrder()
	{
		var result = Rank([Rec("first"), Rec("second"), Rec("third")],
			new() { ["first"] = Score(1, 1), ["second"] = Score(1, 1), ["third"] = Score(1, 1) });

		Assert.Equal(["first", "second", "third"], result.Select(m => m.Id));
	}

	[Fact]
	public void LastPromptTiebreak_IsOnlyAskedForMatchingSessions()
	{
		var asked = new List<string>();
		TreeSearchRanker.Rank(
			[Rec("hit"), Rec("miss")],
			s => s.NodeId == "hit" ? Score(1, 1) : null,
			id => { asked.Add(id); return null; });

		Assert.Equal(["hit"], asked);
	}
}
