using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class JsonlWriteBackPlannerTests
{
	private readonly JsonlWriteBackPlanner _planner = new();

	private static SessionEntryModel Entry(string role, string content) => new()
	{
		Timestamp = DateTimeOffset.UtcNow,
		Role      = role,
		Content   = content,
	};

	private static List<SessionEntryModel> Entries(params (string role, string content)[] items) =>
		items.Select(i => Entry(i.role, i.content)).ToList();

	[Fact]
	public void NothingBeyondBaseline_WritesNothingAndKeepsBaseline()
	{
		var jsonl = Entries(("USER", "a"), ("ASSISTANT", "b"));

		var plan = _planner.Plan(jsonl, 2, [], false);

		Assert.Empty(plan.EntriesToWrite);
		Assert.Equal(2, plan.NewBaselineCount);
	}

	[Fact]
	public void ExternalTurn_NotInTxt_IsWrittenOnce()
	{
		var jsonl = Entries(("USER", "old"), ("USER", "from terminal"), ("ASSISTANT", "answer"));
		var tail  = Entries(("USER", "old"));

		var plan = _planner.Plan(jsonl, 1, tail, false);

		Assert.Equal(["from terminal", "answer"], plan.EntriesToWrite.Select(e => e.Content));
		Assert.Equal(3, plan.NewBaselineCount);

		// Next pass with the updated baseline writes nothing more.
		var again = _planner.Plan(jsonl, plan.NewBaselineCount, tail, false);
		Assert.Empty(again.EntriesToWrite);
	}

	[Fact]
	public void HeadlessTurnActive_WritesOnlyToolLines_AndAdvancesBaseline()
	{
		// USER/ASSISTANT are absent from the tail (Core has not flushed them) but must still be skipped.
		var jsonl = Entries(("USER", "old"), ("USER", "[Scheduled] result"), ("SYSTEM", "[Tool: Bash] ls"), ("ASSISTANT", "reply"));

		var plan = _planner.Plan(jsonl, 1, [], true);

		Assert.Equal(["[Tool: Bash] ls"], plan.EntriesToWrite.Select(e => e.Content));
		Assert.Equal(4, plan.NewBaselineCount);
	}

	[Fact]
	public void HeadlessTurnActive_PassBySteps_WritesEachToolLineOnce()
	{
		var pass1 = Entries(("USER", "p"), ("SYSTEM", "[Tool: Read] a"));
		var plan1 = _planner.Plan(pass1, 0, [], true);
		var pass2 = pass1.Concat(Entries(("SYSTEM", "[Tool: Read] b"), ("ASSISTANT", "r"))).ToList();
		var tail  = Entries(("USER", "p"), ("SYSTEM", "[Tool: Read] a"));
		var plan2 = _planner.Plan(pass2, plan1.NewBaselineCount, tail, true);

		Assert.Equal(["[Tool: Read] a"], plan1.EntriesToWrite.Select(e => e.Content));
		Assert.Equal(["[Tool: Read] b"], plan2.EntriesToWrite.Select(e => e.Content));
	}

	[Fact]
	public void HeadlessTurnActive_RepeatedIdenticalToolLines_AreAllWritten()
	{
		var pass1 = Entries(("USER", "p"), ("SYSTEM", "[Tool: Bash] poll"), ("SYSTEM", "[Tool: Bash] poll"));
		var plan1 = _planner.Plan(pass1, 0, [], true);
		var pass2 = pass1.Concat(Entries(("SYSTEM", "[Tool: Bash] poll"))).ToList();
		// The tail already holds the first two lines, as after the first pass was written.
		var tail  = Entries(("SYSTEM", "[Tool: Bash] poll"), ("SYSTEM", "[Tool: Bash] poll"));
		var plan2 = _planner.Plan(pass2, plan1.NewBaselineCount, tail, true);

		Assert.Equal(2, plan1.EntriesToWrite.Count);
		Assert.Single(plan2.EntriesToWrite);
	}

	[Fact]
	public void LongHeadlessTurn_BeyondTailWindow_ProducesNoDuplicatesAfterItEnds()
	{
		var all = new List<SessionEntryModel> { Entry("USER", "prompt") };
		for (var i = 0; i < Constants.SessionFile.WriteBackTailWindow * 3; i++)
			all.Add(Entry(i % 2 == 0 ? "SYSTEM" : "ASSISTANT", $"entry {i}"));

		// Passes ran during the turn, so the baseline already covers everything.
		var during = _planner.Plan(all, 0, [], true);
		Assert.Equal(all.Count, during.NewBaselineCount);

		var tail  = all.TakeLast(Constants.SessionFile.WriteBackTailWindow).ToList();
		var after = _planner.Plan(all, during.NewBaselineCount, tail, false);
		Assert.Empty(after.EntriesToWrite);
	}

	[Fact]
	public void EntryAlreadyWrittenByCore_IsSkippedEvenWhenNotTheLastTxtEntry()
	{
		// ISSUE.014: Core wrote USER and ASSISTANT; the JSONL replays both after the turn ended.
		var jsonl = Entries(("USER", "old"), ("USER", "prompt"), ("ASSISTANT", "reply"));
		var tail  = Entries(("USER", "old"), ("USER", "prompt"), ("SYSTEM", "[note]"), ("ASSISTANT", "reply"), ("SYSTEM", "[later]"));

		var plan = _planner.Plan(jsonl, 1, tail, false);

		Assert.Empty(plan.EntriesToWrite);
		Assert.Equal(3, plan.NewBaselineCount);
	}

	[Fact]
	public void BatchWithPartialOverlap_WritesOnlyMissingEntries()
	{
		var jsonl = Entries(("USER", "p1"), ("ASSISTANT", "r1"), ("USER", "p2"), ("ASSISTANT", "r2"));
		var tail  = Entries(("USER", "p1"), ("ASSISTANT", "r1"));

		var plan = _planner.Plan(jsonl, 0, tail, false);

		Assert.Equal(["p2", "r2"], plan.EntriesToWrite.Select(e => e.Content));
	}

	[Fact]
	public void TailEntryAbsorbsOnlyOneCandidate_RepeatedExternalMessageStillWritten()
	{
		var jsonl = Entries(("USER", "continue"), ("USER", "continue"));
		var tail  = Entries(("USER", "continue"));

		var plan = _planner.Plan(jsonl, 0, tail, false);

		Assert.Single(plan.EntriesToWrite);
	}

	[Fact]
	public void RoleMustMatch()
	{
		var jsonl = Entries(("ASSISTANT", "same text"));
		var tail  = Entries(("USER", "same text"));

		var plan = _planner.Plan(jsonl, 0, tail, false);

		Assert.Single(plan.EntriesToWrite);
	}

	[Fact]
	public void LateFlushAfterTurnEnd_IsDedupedAgainstTxt()
	{
		// Turn ran (baseline advanced to 2 while active), the final assistant entry flushes to the JSONL late.
		var jsonl = Entries(("USER", "old"), ("USER", "p"), ("ASSISTANT", "final"));
		var tail  = Entries(("USER", "p"), ("ASSISTANT", "final"));

		var plan = _planner.Plan(jsonl, 2, tail, false);

		Assert.Empty(plan.EntriesToWrite);
	}

	[Fact]
	public void WhitespaceDifferencesAreIgnored()
	{
		var jsonl = Entries(("ASSISTANT", "text\n"));
		var tail  = Entries(("ASSISTANT", "text"));

		Assert.Empty(_planner.Plan(jsonl, 0, tail, false).EntriesToWrite);
	}
}
