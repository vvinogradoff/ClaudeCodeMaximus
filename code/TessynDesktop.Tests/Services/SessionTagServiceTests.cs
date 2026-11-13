using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.04, 06, 07, 08, 09, 15 — <see cref="SessionTagService"/> (FR.24.2–FR.24.7).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagServiceTests : IDisposable
{
	private const string Hse230 = "https://linear.app/hardstop/issue/HSE-230";
	private const string Pr215  = "tryhardstop/windows-app#215";

	private readonly TagTestEnv _env = new();
	private SessionNodeModel _node = TagTestEnv.Node();

	public void Dispose() => _env.Dispose();

	private SessionTagService Svc => _env.Service;

	private void SeedHse() =>
		_env.Store.SaveWorkspaces(new Dictionary<string, string> { ["HSE"] = "hardstop" });

	private SessionTagEditResultModel Add(params string[] entries) => Svc.Edit(_node, entries, []);

	private SessionTagEditResultModel Remove(params string[] entries) => Svc.Edit(_node, [], entries);

	private SessionTagModel[] Tags(string? name = null) => Svc.GetTags(_node.NodeId, name ?? _node.Name).ToArray();

	private List<string?> CaptureEvents()
	{
		var events = new List<string?>();
		Svc.SessionTagsChanged += (_, e) => events.Add(e.NodeId);
		return events;
	}

	// ── TC.TG.04 workspace map ────────────────────────────────────────────────

	[Fact]
	public void Add_FullLinearUrl_LearnsAndPersistsTheTeam()   // TC.TG.04-1
	{
		Add(Hse230);

		Assert.Contains("\"HSE\": \"hardstop\"", File.ReadAllText(Path.Combine(_env.Root, "linear-workspaces.json")));

		var restarted = _env.NewServiceOverSameRoot();
		var result = restarted.Edit(_node, ["HSE-231"], []);
		Assert.Equal("https://linear.app/hardstop/issue/HSE-231", result.Outcomes.Single().Tag!.Url);
	}

	[Fact]
	public void Add_AnotherWorkspaceForTheSameTeam_LastWins_StoredTagsKeepTheirUrl()   // TC.TG.04-2 (brief section 6 item 2)
	{
		Add(Hse230);

		Add("https://linear.app/other/issue/HSE-1");
		var next = Add("HSE-2").Outcomes.Single().Tag!;

		Assert.Equal("https://linear.app/other/issue/HSE-2", next.Url);
		Assert.Equal(Hse230, Tags().Single(t => t.Key == "HSE-230").Url);
	}

	[Fact]
	public void Add_ShortKeyOfUnknownTeam_IsRejectedWithoutChangingAnything()   // TC.TG.04-3
	{
		var result = Add("ABC-123");

		Assert.Equal(SessionTagEntryOutcomeKind.NotTaggedUnknownTeam, result.Outcomes.Single().Kind);
		Assert.Empty(Tags());
		Assert.False(File.Exists(Path.Combine(_env.Root, "linear-workspaces.json")));
	}

	[Fact]
	public void Learning_ANewTeam_RaisesTheNullEvent_RelearningTheSamePairDoesNot()   // TC.TG.04-4
	{
		var events = CaptureEvents();

		Add(Hse230);
		Assert.Equal([(string?)null], events);

		events.Clear();
		Add("https://linear.app/hardstop/issue/HSE-999");   // same pair, new tag
		Assert.Equal([_node.NodeId], events);
	}

	[Fact]
	public void CorruptWorkspaceFile_IsMovedAsideAndMapStartsEmpty()   // TC.TG.04-5
	{
		Directory.CreateDirectory(_env.Root);
		File.WriteAllText(Path.Combine(_env.Root, "linear-workspaces.json"), "{ not json");

		var result = Add("HSE-230");

		Assert.Equal(SessionTagEntryOutcomeKind.NotTaggedUnknownTeam, result.Outcomes.Single().Kind);
		Assert.Single(Directory.GetFiles(_env.Root, "linear-workspaces.json.bad-*"));
	}

	[Fact]
	public void Save_LeavesNoTempFileBehind()   // TC.TG.04-6
	{
		Add(Hse230);

		Assert.Empty(Directory.GetFiles(_env.Root, "*.tmp"));
	}

	// ── TC.TG.05 (service side) ───────────────────────────────────────────────

	[Fact]
	public void Tags_AreLoadedOnceAndCached_DeletingTheFileDoesNotChangeWhatIsReturned()   // TC.TG.05-6
	{
		Add(Pr215);
		var restarted = _env.NewServiceOverSameRoot();
		Assert.Single(restarted.GetTags(_node.NodeId, null));

		File.Delete(Path.Combine(_env.Root, _node.NodeId + ".json"));

		Assert.Single(restarted.GetTags(_node.NodeId, null));
	}

	[Fact]
	public async Task ConcurrentAdds_LeaveAValidFileWith20DistinctTags()   // TC.TG.05-7
	{
		await Task.WhenAll(Enumerable.Range(1, 20).Select(n => Task.Run(() => Svc.Edit(_node, [$"o/r#{n}"], []))));

		var loaded = new SessionTagStore(_env.Root).LoadTags(_node.NodeId);
		Assert.Equal(20, loaded.Count);
		Assert.Equal(20, loaded.Select(t => t.Key).Distinct().Count());
	}

	// ── TC.TG.06 add / already tagged / remove ───────────────────────────────

	[Fact]
	public void Add_NewKey_IsStoredWithSessionSourceAndTime()   // TC.TG.06-1
	{
		var result = Add(Pr215);

		var tag = Assert.Single(Tags());
		Assert.Equal(SessionTagSource.Session, tag.Source);
		Assert.Equal(_env.Clock.GetUtcNow(), tag.AddedUtc);
		Assert.Equal(SessionTagEntryOutcomeKind.Tagged, result.Outcomes.Single().Kind);
		Assert.Equal(["windows-app PR #215"], result.AddedLabels);
	}

	[Theory]
	[InlineData("tryhardstop/windows-app#215")]
	[InlineData("TRYHARDSTOP/WINDOWS-APP#215")]
	[InlineData("https://github.com/tryhardstop/windows-app/pull/215")]
	public void Add_SameKeyAnyCaseOrForm_IsAlreadyTagged(string again)   // TC.TG.06-2
	{
		Add(Pr215);
		var first = Tags().Single().AddedUtc;
		_env.Clock.Advance(TimeSpan.FromMinutes(5));

		var result = Add(again);

		Assert.Equal(SessionTagEntryOutcomeKind.AlreadyTagged, result.Outcomes.Single().Kind);
		Assert.False(result.HasChanges);
		Assert.Equal(first, Assert.Single(Tags()).AddedUtc);
	}

	[Fact]
	public void Add_KeyAlreadyDerivedFromTheName_IsAlreadyTaggedAndStoresNothing()   // TC.TG.06-3
	{
		SeedHse();
		_node = TagTestEnv.Node(name: "HSE-150 Android backend");

		var result = Add("HSE-150");

		Assert.Equal(SessionTagEntryOutcomeKind.AlreadyTagged, result.Outcomes.Single().Kind);
		Assert.Empty(new SessionTagStore(_env.Root).LoadTags(_node.NodeId));
		Assert.False(File.Exists(Path.Combine(_env.Root, _node.NodeId + ".json")));
	}

	[Fact]
	public void Remove_Declared_RemovesIt_SecondRemovalReportsNotTagged()   // TC.TG.06-4
	{
		Add(Pr215);

		var first  = Remove(Pr215);
		var second = Remove(Pr215);

		Assert.Equal(SessionTagEntryOutcomeKind.Removed, first.Outcomes.Single().Kind);
		Assert.Equal(["windows-app PR #215"], first.RemovedLabels);
		Assert.Empty(Tags());
		Assert.Equal(SessionTagEntryOutcomeKind.NotRemovedNotTagged, second.Outcomes.Single().Kind);
	}

	[Fact]
	public void Remove_NameDerived_IsRefused()   // TC.TG.06-5
	{
		SeedHse();
		_node = TagTestEnv.Node(name: "HSE-150 spike");

		var result = Remove("HSE-150");

		Assert.Equal(SessionTagEntryOutcomeKind.NotRemovedFromName, result.Outcomes.Single().Kind);
		Assert.Single(Tags());
	}

	[Fact]
	public void Remove_Invalid_ReportsInvalid()   // TC.TG.06-6
	{
		Assert.Equal(SessionTagEntryOutcomeKind.NotRemovedInvalid, Remove("foo").Outcomes.Single().Kind);
	}

	[Fact]
	public void Edit_AddThenRemoveOfTheSameKey_LeavesItUntagged()   // TC.TG.06-7
	{
		var result = Svc.Edit(_node, [Pr215], [Pr215]);

		Assert.Empty(Tags());
		Assert.Equal(
			[SessionTagEntryOutcomeKind.Tagged, SessionTagEntryOutcomeKind.Removed],
			result.Outcomes.Select(o => o.Kind).ToArray());
	}

	// ── TC.TG.07 name-derived ─────────────────────────────────────────────────

	[Fact]
	public void GetTags_FromTheName_IsANameSourceTagWithoutTime()   // TC.TG.07-1
	{
		SeedHse();

		var tag = Assert.Single(Tags("HSE-150 Android backend"));

		Assert.Equal(SessionTagKind.LinearIssue, tag.Kind);
		Assert.Equal("HSE-150", tag.Key);
		Assert.Equal(SessionTagSource.Name, tag.Source);
		Assert.Null(tag.AddedUtc);
		Assert.Equal("https://linear.app/hardstop/issue/HSE-150", tag.Url);
	}

	[Fact]
	public void GetTags_SeveralKeysInTheName_GiveSeveralTags()   // TC.TG.07-2
	{
		SeedHse();

		var tags = Tags("HSE-150 and HSE-151");

		Assert.Equal(["HSE-150", "HSE-151"], tags.Select(t => t.Key).ToArray());
		Assert.All(tags, t => Assert.Equal(SessionTagSource.Name, t.Source));
	}

	[Fact]
	public void GetTags_UnknownTeamsInTheName_AreIgnored()   // TC.TG.07-3
	{
		SeedHse();

		Assert.Equal(["HSE-150"], Tags("KVM-1 DNS-01 HSE-150").Select(t => t.Key).ToArray());
	}

	[Fact]
	public void GetTags_Rename_ChangesNameDerivedTags_AndPersistsNothing()   // TC.TG.07-4
	{
		SeedHse();

		Assert.Equal(["HSE-150"], Tags("HSE-150 Android backend").Select(t => t.Key).ToArray());
		Assert.Equal(["HSE-165"], Tags("HSE-165 W14 rebase").Select(t => t.Key).ToArray());
		Assert.False(File.Exists(Path.Combine(_env.Root, _node.NodeId + ".json")));
	}

	[Fact]
	public void TheServiceSurface_HasNoTextScanningApi()   // TC.TG.07-5 (FR.24.10)
	{
		var methods = typeof(ISessionTagService).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

		Assert.Equal(["Edit", "GetTags", "Parse", "TakeTagGuidanceDue", "add_SessionTagsChanged", "remove_SessionTagsChanged"], methods);
	}

	[Fact]
	public void GetTags_TeamLearnedLater_MakesTheNameTokenAppear()   // TC.TG.07-6
	{
		Assert.Empty(Tags("ABC-9 spike"));

		Add("https://linear.app/acme/issue/ABC-1");

		Assert.Contains(Tags("ABC-9 spike"), t => t.Key == "ABC-9" && t.Source == SessionTagSource.Name);
	}

	[Fact]
	public void GetTags_DeclaredAndNameDerivedWithTheSameKey_ShowOneNameTag()   // TC.TG.07-7
	{
		Add(Hse230);

		var tag = Assert.Single(Tags("HSE-230 something"));

		Assert.Equal(SessionTagSource.Name, tag.Source);
	}

	[Fact]
	public void GetTags_AfterRenamingAwayFromTheKey_TheDeclaredCopyShowsAgain()   // TC.TG.07-8
	{
		Add(Hse230);

		Assert.Equal(SessionTagSource.Name, Assert.Single(Tags("HSE-230 something")).Source);
		var again = Assert.Single(Tags("renamed"));
		Assert.Equal(SessionTagSource.Session, again.Source);
		Assert.NotNull(again.AddedUtc);
	}

	// ── TC.TG.08 ordering ─────────────────────────────────────────────────────

	[Fact]
	public void GetTags_OrdersByKindThenNameFirstThenNewestDeclared()   // TC.TG.08-1, 08-2
	{
		SeedHse();
		Add("o/repo");                                      // repository
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		Add("o/r#1");                                       // older PR
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		Add("HSE-230");                                     // older declared issue
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		Add("o/r#2");                                       // newer PR
		_env.Clock.Advance(TimeSpan.FromMinutes(1));
		Add("HSE-231");                                     // newer declared issue

		var keys = Tags("HSE-150 spike").Select(t => t.Key).ToArray();

		Assert.Equal(["HSE-150", "HSE-231", "HSE-230", "o/r#2", "o/r#1", "o/repo"], keys);
	}

	// ── TC.TG.09 events ───────────────────────────────────────────────────────

	[Fact]
	public void Edit_ThatChangesSomething_RaisesOneEventPerCall()   // TC.TG.09-1
	{
		var events = CaptureEvents();

		Add("o/r#1", "o/r#2", "o/r#3");
		Remove("o/r#1", "o/r#2");

		Assert.Equal([_node.NodeId, _node.NodeId], events);
	}

	[Fact]
	public void Edit_ThatChangesNothing_RaisesNoEvent()   // TC.TG.09-2
	{
		Add("o/r#1");
		var events = CaptureEvents();

		Add("o/r#1");          // already tagged
		Remove("o/r#9");       // not tagged
		Add("foo");            // rejected
		Add("ABC-1");          // unknown team
		Remove("foo");         // invalid
		Svc.Edit(_node, [], []);
		Svc.GetTags(_node.NodeId, null);

		Assert.Empty(events);
	}

	[Fact]
	public void Edit_ThatLearnsATeam_RaisesTheNullEventOnce()   // TC.TG.09-3
	{
		var events = CaptureEvents();

		Svc.Edit(_node, [Hse230, "o/r#1"], []);

		Assert.Equal([(string?)null], events);
	}

	// ── TC.TG.15 once per run ─────────────────────────────────────────────────

	[Fact]
	public void TakeTagGuidanceDue_IsTrueOncePerNode()   // TC.TG.15-1, 15-2
	{
		Assert.True(Svc.TakeTagGuidanceDue("a"));
		Assert.False(Svc.TakeTagGuidanceDue("a"));
		Assert.False(Svc.TakeTagGuidanceDue("a"));
		Assert.True(Svc.TakeTagGuidanceDue("b"));
	}

	[Fact]
	public void TakeTagGuidanceDue_IsInMemoryOnly_ANewServiceIsDueAgain()   // TC.TG.15-3
	{
		Assert.True(Svc.TakeTagGuidanceDue("a"));

		Assert.True(_env.NewServiceOverSameRoot().TakeTagGuidanceDue("a"));
	}

	[Fact]
	public async Task TakeTagGuidanceDue_20ConcurrentCalls_ReturnExactlyOneTrue()   // TC.TG.15-4
	{
		var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Svc.TakeTagGuidanceDue("a"))));

		Assert.Equal(1, results.Count(r => r));
	}
}
