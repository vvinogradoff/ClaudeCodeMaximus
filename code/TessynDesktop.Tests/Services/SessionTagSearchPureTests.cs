using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Pure tests of <see cref="SessionTagSearchFormatter"/>, <see cref="SessionTagSearchPageBuilder"/>, the
/// top-level roll-up of <see cref="SessionListIndex"/> and the <c>tags</c> filter of
/// <see cref="SessionListPageBuilder"/> (FR.24.15, FR.15.13). The end-to-end variants live in
/// <see cref="AgentMcpServerTagSearchTests"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagSearchPureTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

	private static string Id(string tag) => tag.PadRight(32, '-');

	private static SessionSnapshotModel Snap(string tag, string? sup = null, string dir = "A", string? name = null) => new()
	{
		NodeId             = Id(tag),
		Name               = name ?? tag,
		FileName           = tag + ".txt",
		WorkingDirectory   = "C:\\" + dir,
		OrchestrationDepth = sup == null ? 0 : 1,
		SupervisorNodeId   = sup == null ? null : Id(sup),
		DirectoryPath      = "C:\\" + dir,
		DirectoryLabel     = dir,
		GroupPath          = [],
	};

	private static SessionTagUsageModel Usage(string label, string url, params string[] carriers) => new()
	{
		Tag     = new SessionTagModel { Kind = SessionTagKind.LinearIssue, Key = label, Label = label, Url = url },
		NodeIds = carriers.Select(Id).ToList(),
	};

	private static SessionTagSearchPageModel Page(
		IReadOnlyList<SessionTagSearchRowModel> rows, int total, int tagsInScope = 1, int offset = 0,
		SessionListScope scope = SessionListScope.All, string? query = null) => new()
	{
		Scope = scope, Rows = rows, TotalRows = total, TagsInScope = tagsInScope, Offset = offset, Limit = 30,
		Query = query, DirectoryLabel = "Dir\nLabel", DirectoryPath = "C:\\Dir\\",
	};

	// ── Formatter (TC.TG.50-6) ────────────────────────────────────────────────

	[Fact]
	public void Formatter_TC_TG_50_6_ControlCharactersInALabelBecomeSpaces_AndTheRowStaysOnOneLine()
	{
		var row  = new SessionTagSearchRowModel { Label = "a\r\nb\tc", Url = "https://x/y", SessionCount = 1, CarrierShortIds = ["abcdef01"] };

		var text = new SessionTagSearchFormatter().Format(Page([row], 1));

		var lines = text.Split('\n');
		Assert.Equal("a  b c  https://x/y  1 session: abcdef01", lines[1]);
		Assert.Equal(4, lines.Length);
	}

	[Fact]
	public void Formatter_TC_TG_50_6_ProjectHeader_SanitisesLabelAndDropsTheTrailingSeparator()
	{
		var row = new SessionTagSearchRowModel { Label = "L", Url = "https://x", SessionCount = 1, CarrierShortIds = ["abcdef01"] };

		var header = new SessionTagSearchFormatter().Format(Page([row], 1, scope: SessionListScope.Project, query: "q")).Split('\n')[0];

		Assert.Equal("Tags in Dir Label (C:\\Dir) - 1 tags, matching \"q\"", header);
	}

	[Fact]
	public void Formatter_ToolsOwnWordsAreAscii()
	{
		var rows = new[]
		{
			new SessionTagSearchRowModel { Label = "L", Url = "https://x", SessionCount = 7, CarrierShortIds = ["a", "b", "c", "d", "e"] },
		};

		var text = new SessionTagSearchFormatter().Format(Page(rows, 9, offset: 0));

		Assert.All(text, c => Assert.True(c < 128));
		Assert.Contains("7 sessions: a, b, c, d, e +2", text);
		Assert.Contains("Rows 1-1 of 9. Next page: offset=1.", text);
	}

	// ── Page builder ──────────────────────────────────────────────────────────

	private static SessionTagSearchPageModel Build(
		IReadOnlyList<SessionSnapshotModel> sessions, string caller, SessionTagSearchQueryModel query,
		IReadOnlyList<SessionTagUsageModel> usages, List<string?>? scopes = null) =>
		new SessionTagSearchPageBuilder().Build(sessions, caller, query, (_, dir) => { scopes?.Add(dir); return usages; }, s => Now);

	[Fact]
	public void PageBuilder_ClampsLimitAndOffset()
	{
		var sessions = new[] { Snap("c"), Snap("t") };
		var usages   = Enumerable.Range(1, 3).Select(i => Usage($"T{i}", $"https://x/{i}", "t")).ToList();

		var zero = Build(sessions, Id("c"), new SessionTagSearchQueryModel { Limit = 0, Offset = -4 }, usages);
		var huge = Build(sessions, Id("c"), new SessionTagSearchQueryModel { Limit = 99999 }, usages);

		Assert.Equal(1, zero.Limit);
		Assert.Equal(0, zero.Offset);
		Assert.Single(zero.Rows);
		Assert.Equal(Constants.Agent.ListSessionsMaxLimit, huge.Limit);
		Assert.Equal(3, huge.Rows.Count);
	}

	[Fact]
	public void PageBuilder_ProjectScopeUsesTheCallersDirectory_AllScopeNull()
	{
		var sessions = new[] { Snap("c", dir: "B"), Snap("t", dir: "A") };
		var scopes   = new List<string?>();

		Build(sessions, Id("c"), new SessionTagSearchQueryModel(), [], scopes);
		var all = Build(sessions, Id("c"), new SessionTagSearchQueryModel { Scope = SessionListScope.All }, [], scopes);

		Assert.Equal(new string?[] { "C:\\B", null }, scopes);
		Assert.Equal("c", all.Caller!.ShortId[..1]);
	}

	[Fact]
	public void PageBuilder_ProjectScopeWithACallerOutsideTheTree_IsEmptyWithoutBuildingAnIndex()
	{
		var scopes = new List<string?>();

		var page = Build([Snap("t")], Id("ghost"), new SessionTagSearchQueryModel(), [Usage("T", "https://x", "t")], scopes);

		Assert.Empty(scopes);
		Assert.Null(page.Caller);
		Assert.Equal(0, page.TotalRows);
		Assert.Equal(0, page.TagsInScope);
	}

	[Fact]
	public void PageBuilder_LastActivityIsReadOnlyForCarriers_OnceEach()
	{
		var sessions = new[] { Snap("c"), Snap("t1"), Snap("t2"), Snap("idle") };
		var usages   = new[] { Usage("T", "https://x", "t1", "t2"), Usage("U", "https://y", "t1") };
		var lookups  = new List<string>();

		new SessionTagSearchPageBuilder().Build(
			sessions, Id("c"), new SessionTagSearchQueryModel(), (_, _) => usages, s => { lookups.Add(s.NodeId); return Now; });

		Assert.Equal(new[] { Id("t1"), Id("t2") }.OrderBy(x => x), lookups.OrderBy(x => x));
	}

	// ── Top-level roll-up and the tags filter ─────────────────────────────────

	[Fact]
	public void ListBuilder_TagCarrierFilter_OnASupervisorCycle_KeepsEveryMemberAndWhatLeadsIntoIt()
	{
		var sessions = new[] { Snap("me"), Snap("b", sup: "c"), Snap("c", sup: "a"), Snap("a", sup: "b"), Snap("x", sup: "c"), Snap("y") };
		var tagIndex = sessions.Select(s => new SessionTagIndexSessionModel(s.NodeId, s.Name, s.SupervisorNodeId, s.DirectoryPath)).ToList();
		var built    = SessionTagIndexBuilder.Build(tagIndex, s => s.Name is "y" or "me" ? [] : [new SessionTagModel { Key = "K", Label = "K", Url = "u" }], null);
		var carriers = SessionTagSearch.SessionsWithAnyTag(built, ["K"]);

		var page = new SessionListPageBuilder().Build(
			sessions, Id("me"), new SessionListQueryModel { IncludeWorkers = true, TagCarrierNodeIds = carriers }, _ => Now, _ => true, Now);

		Assert.Equal(new[] { "a", "b", "c", "x" }, page.Rows.Select(r => r.NodeId.TrimEnd('-')).OrderBy(x => x, StringComparer.Ordinal));
	}

	[Fact]
	public void ListBuilder_TagCarrierFilter_KeepsCandidatesWhoseTopLevelSessionCarries()
	{
		var sessions = new[] { Snap("me"), Snap("top"), Snap("w", sup: "top"), Snap("other"), Snap("ow", sup: "other") };

		var page = new SessionListPageBuilder().Build(
			sessions,
			Id("me"),
			new SessionListQueryModel { IncludeWorkers = true, TagCarrierNodeIds = new HashSet<string> { Id("top") } },
			_ => Now, _ => true, Now);

		Assert.Equal(new[] { "top", "w" }, page.Rows.Select(r => r.NodeId.TrimEnd('-')).OrderBy(x => x == "top" ? 0 : 1));
		Assert.Equal(1, page.Rows.Single(r => r.NodeId == Id("w")).IndentLevel);
	}

	[Fact]
	public void ListBuilder_EmptyCarrierSet_MatchesNothing_NullMeansNoFilter()
	{
		var sessions = new[] { Snap("me"), Snap("top") };
		SessionListPageModel Run(IReadOnlySet<string>? carriers) => new SessionListPageBuilder().Build(
			sessions, Id("me"), new SessionListQueryModel { TagCarrierNodeIds = carriers }, _ => Now, _ => true, Now);

		Assert.Empty(Run(new HashSet<string>()).Rows);
		Assert.Equal(2, Run(null).Rows.Count);
	}
}
