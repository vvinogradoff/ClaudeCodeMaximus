using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.29, TC.TG.30, TC.TG.31 — <see cref="SessionTagSearch"/> (FR.24.11, FR.24.12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagSearchTests
{
	private static readonly SessionTagModel Hse254 = new() { Kind = SessionTagKind.LinearIssue, Key = "HSE-254", Label = "HSE-254", Url = "https://linear.app/hardstop/issue/HSE-254" };
	private static readonly SessionTagModel Pr303  = new() { Kind = SessionTagKind.GitHubPullRequest, Key = "tryhardstop/backend#303", Label = "backend PR #303", Url = "https://github.com/tryhardstop/backend/pull/303" };

	private static SessionTagUsageModel Usage(SessionTagKind kind, string key, string label, params string[] nodeIds) =>
		new() { Tag = new SessionTagModel { Kind = kind, Key = key, Label = label }, NodeIds = nodeIds.ToList() };

	// ── Matches (TC.TG.29) ──────────────────────────────────────────────────────────

	[Fact]
	public void Matches_SinglePart_HitsLabel()   // TC.TG.29
	{
		Assert.True(SessionTagSearch.Matches(Hse254, "HSE-254"));
		Assert.True(SessionTagSearch.Matches(Pr303, "backend"));
	}

	[Fact]
	public void Matches_HashPart_HitsTheKeyAndTheLabel()   // TC.TG.29
	{
		Assert.True(SessionTagSearch.Matches(Pr303, "#303"));
		Assert.True(SessionTagSearch.Matches(Pr303, "tryhardstop/"));   // key only, not in the label
	}

	[Fact]
	public void Matches_SeveralParts_AllMustOccur_InAnyOrder()   // TC.TG.29
	{
		Assert.True(SessionTagSearch.Matches(Pr303, "backend 303"));
		Assert.True(SessionTagSearch.Matches(Pr303, "303   backend"));
		Assert.False(SessionTagSearch.Matches(Pr303, "backend 304"));
	}

	[Fact]
	public void Matches_IsCaseInsensitive()   // TC.TG.29
	{
		Assert.True(SessionTagSearch.Matches(Hse254, "hse-254"));
		Assert.True(SessionTagSearch.Matches(Pr303, "BACKEND pr"));
	}

	[Fact]
	public void Matches_DoesNotSearchTheUrl()   // TC.TG.29
	{
		Assert.False(SessionTagSearch.Matches(Hse254, "hardstop"));
		Assert.False(SessionTagSearch.Matches(Hse254, "linear"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t \n")]
	public void Matches_NullOrBlankQuery_MatchesEverything(string? query)   // TC.TG.29
	{
		Assert.True(SessionTagSearch.Matches(Hse254, query));
	}

	[Fact]
	public void Matches_NoPartOccurs_ReturnsFalse()   // TC.TG.29
	{
		Assert.False(SessionTagSearch.Matches(Hse254, "zzz"));
	}

	// ── SessionsWithAnyTag (TC.TG.30) ───────────────────────────────────────────────

	private static List<SessionTagUsageModel> SampleIndex() =>
	[
		Usage(SessionTagKind.LinearIssue, "HSE-254", "HSE-254", "a", "b"),
		Usage(SessionTagKind.GitHubPullRequest, "tryhardstop/backend#303", "backend PR #303", "b", "c"),
		Usage(SessionTagKind.GitHubRepository, "tryhardstop/backend", "backend", "d"),
	];

	[Fact]
	public void SessionsWithAnyTag_UnionsTheSessionsOfTheSelectedKeys()   // TC.TG.30
	{
		var ids = SessionTagSearch.SessionsWithAnyTag(SampleIndex(), ["HSE-254", "tryhardstop/backend#303"]);
		Assert.Equal(["a", "b", "c"], ids.OrderBy(x => x).ToArray());
	}

	[Fact]
	public void SessionsWithAnyTag_KeysCompareCaseInsensitively_NodeIdsOrdinal()   // TC.TG.30
	{
		var ids = SessionTagSearch.SessionsWithAnyTag(SampleIndex(), ["hse-254"]);
		Assert.Equal(["a", "b"], ids.OrderBy(x => x).ToArray());
		Assert.Contains("a", ids);
		Assert.DoesNotContain("A", ids);
	}

	[Fact]
	public void SessionsWithAnyTag_UnknownKey_AddsNothing()   // TC.TG.30
	{
		Assert.Empty(SessionTagSearch.SessionsWithAnyTag(SampleIndex(), ["nope"]));
		var ids = SessionTagSearch.SessionsWithAnyTag(SampleIndex(), ["nope", "tryhardstop/backend"]);
		Assert.Equal(["d"], ids.ToArray());
	}

	[Fact]
	public void SessionsWithAnyTag_EmptySelection_IsEmpty()   // TC.TG.30
	{
		Assert.Empty(SessionTagSearch.SessionsWithAnyTag(SampleIndex(), []));
	}

	// ── Order (TC.TG.31) ────────────────────────────────────────────────────────────

	private static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Order_SortsByKindFirst_RegardlessOfRecency()   // TC.TG.31
	{
		var usages = new[]
		{
			Usage(SessionTagKind.GitHubRepository, "r", "r", "n1"),
			Usage(SessionTagKind.GitHubPullRequest, "p", "p", "n2"),
			Usage(SessionTagKind.LinearIssue, "l", "l", "n3"),
		};
		var ordered = SessionTagSearch.Order(usages, id => id == "n1" ? T0.AddDays(5) : T0);
		Assert.Equal(["l", "p", "r"], ordered.Select(u => u.Tag.Key).ToArray());
	}

	[Fact]
	public void Order_WithinAKind_NewestActivityAmongCarriersFirst()   // TC.TG.31
	{
		var usages = new[]
		{
			Usage(SessionTagKind.LinearIssue, "old", "old", "n1"),
			Usage(SessionTagKind.LinearIssue, "new", "new", "n2", "n3"),
		};
		var activity = new Dictionary<string, DateTimeOffset?> { ["n1"] = T0, ["n2"] = T0.AddDays(-3), ["n3"] = T0.AddDays(2) };
		var ordered = SessionTagSearch.Order(usages, id => activity[id]);
		Assert.Equal(["new", "old"], ordered.Select(u => u.Tag.Key).ToArray());
	}

	[Fact]
	public void Order_NoActivitySortsLast()   // TC.TG.31
	{
		var usages = new[]
		{
			Usage(SessionTagKind.LinearIssue, "none", "aaa", "n1"),
			Usage(SessionTagKind.LinearIssue, "some", "zzz", "n2"),
		};
		var ordered = SessionTagSearch.Order(usages, id => id == "n2" ? T0 : null);
		Assert.Equal(["some", "none"], ordered.Select(u => u.Tag.Key).ToArray());
	}

	[Fact]
	public void Order_TieBreaksByLabel_CaseInsensitively()   // TC.TG.31
	{
		var usages = new[]
		{
			Usage(SessionTagKind.LinearIssue, "k2", "bravo", "n1"),
			Usage(SessionTagKind.LinearIssue, "k1", "Alpha", "n2"),
			Usage(SessionTagKind.LinearIssue, "k3", "charlie", "n3"),
		};
		var ordered = SessionTagSearch.Order(usages, _ => T0);
		Assert.Equal(["Alpha", "bravo", "charlie"], ordered.Select(u => u.Tag.Label).ToArray());
	}

	[Fact]
	public void Order_DoesNotMutateTheInput()   // TC.TG.31
	{
		var usages = new List<SessionTagUsageModel>
		{
			Usage(SessionTagKind.GitHubRepository, "r", "r", "n1"),
			Usage(SessionTagKind.LinearIssue, "l", "l", "n2"),
		};
		SessionTagSearch.Order(usages, _ => null);
		Assert.Equal("r", usages[0].Tag.Key);
	}
}
