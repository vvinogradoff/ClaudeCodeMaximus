using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary><c>tags.search</c> (contract 3.8; FR.24.11–FR.24.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagsRemoteRpcModuleTests : IDisposable
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	private readonly RemoteTreeTestEnv _env = new();
	private readonly DirectoryNodeModel _dirA;

	public TagsRemoteRpcModuleTests() => _dirA = _env.AddDirectory("a");

	public void Dispose() => _env.Dispose();

	private static SessionTagModel Linear(string key, string? url = null) => new()
	{
		Kind = SessionTagKind.LinearIssue, Key = key, Label = key, Url = url ?? $"https://linear.app/team/issue/{key}",
	};

	private static SessionTagModel Pr(string key, string label) => new()
	{
		Kind = SessionTagKind.GitHubPullRequest, Key = key, Label = label, Url = "https://github.com/" + key,
	};

	private SessionNodeModel Tagged(DirectoryNodeModel dir, string name, params SessionTagModel[] tags)
	{
		var s = _env.AddSession(dir, name);
		_env.Tags.TagsByNode[s.NodeId] = tags.ToList();
		return s;
	}

	private static string RefJson(SessionTagModel tag) =>
		$$"""{"key":"{{tag.Key}}","label":"{{tag.Label}}","url":"{{tag.Url}}","kind":"{{(tag.Kind == SessionTagKind.LinearIssue ? "linearIssue" : tag.Kind == SessionTagKind.GitHubPullRequest ? "gitHubPullRequest" : "gitHubRepository")}}"}""";

	private Task<JsonNode> Search(string text = "", string? scope = null, params SessionTagModel[] selected)
	{
		var scopeJson = scope == null ? "null" : $"\"{scope}\"";
		var selectedJson = string.Join(",", selected.Select(RefJson));
		return _env.CallAsync("tags.search", $$"""{"text":"{{text}}","scopeNodeId":{{scopeJson}},"selected":[{{selectedJson}}]}""");
	}

	private static string[] ChipKeys(JsonNode result) => result["chips"]!.AsArray().Select(c => c!["key"]!.GetValue<string>()).ToArray();

	// ── chips ─────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Chips_OneChipPerDistinctKey_WithSessionCountsOfTopLevelSessions()
	{
		Tagged(_dirA, "S1", Linear("HSE-1"));
		Tagged(_dirA, "S2", Linear("HSE-1"), Linear("HSE-2"));

		var result = await Search();

		Assert.Equal(["HSE-1", "HSE-2"], ChipKeys(result).OrderBy(k => k));
		Assert.Equal(2, result["chips"]!.AsArray().Single(c => c!["key"]!.GetValue<string>() == "HSE-1")!["sessionCount"]!.GetValue<int>());
		Assert.Null(result["emptyReason"]);
	}

	[Fact]
	public async Task Chips_WorkerTagsAreAttributedToTheirTopLevelSession_CountedOnce()
	{
		var sup = Tagged(_dirA, "Sup", Linear("HSE-1"));
		var worker = _env.AddSession(_dirA, "Worker", supervisorNodeId: sup.NodeId);
		_env.Tags.TagsByNode[worker.NodeId] = [Linear("HSE-1"), Linear("HSE-9")];

		var result = await Search();

		var chips = result["chips"]!.AsArray();
		Assert.Equal(1, chips.Single(c => c!["key"]!.GetValue<string>() == "HSE-1")!["sessionCount"]!.GetValue<int>());
		Assert.Equal(1, chips.Single(c => c!["key"]!.GetValue<string>() == "HSE-9")!["sessionCount"]!.GetValue<int>());
	}

	[Fact]
	public async Task Chips_TextFilter_RequiresEveryPartInLabelOrKey_CaseInsensitive()
	{
		Tagged(_dirA, "S", Linear("HSE-230"), Pr("tryhardstop/windows-app#215", "windows-app PR #215"), Linear("ABC-1"));

		Assert.Equal(["HSE-230"], ChipKeys(await Search("hse-230")));
		Assert.Equal(["tryhardstop/windows-app#215"], ChipKeys(await Search("WINDOWS pr")));
		Assert.Equal(["tryhardstop/windows-app#215"], ChipKeys(await Search("tryhardstop 215")));
		Assert.Empty(ChipKeys(await Search("windows linear")));
	}

	[Fact]
	public async Task Chips_Order_KindThenNewestActivityThenLabel()
	{
		var withNewer = Tagged(_dirA, "N", Linear("HSE-3"), Pr("o/r#2", "r PR #2"));
		var withOlder = Tagged(_dirA, "O", Linear("HSE-2"));
		Tagged(_dirA, "None", Linear("HSE-1"));   // no activity
		_env.Activity.Set(withNewer.NodeId, T0.AddHours(5));
		_env.Activity.Set(withOlder.NodeId, T0);

		var result = await Search();

		// LinearIssue kind before GitHubPullRequest; within Linear: has-activity first, newest first, no-activity last.
		Assert.Equal(["HSE-3", "HSE-2", "HSE-1", "o/r#2"], ChipKeys(result));
	}

	[Fact]
	public async Task Chips_SelectedFirst_EvenWhenTheyDoNotMatchTheText_AndMarkedSelected()
	{
		var linear1 = Linear("HSE-1");
		Tagged(_dirA, "S", linear1, Pr("o/r#7", "r PR #7"), Linear("HSE-2"));

		var result = await Search("PR", null, linear1);

		Assert.Equal(["HSE-1", "o/r#7"], ChipKeys(result));
		var chips = result["chips"]!.AsArray();
		Assert.True(chips[0]!["selected"]!.GetValue<bool>());
		Assert.False(chips[1]!["selected"]!.GetValue<bool>());
	}

	[Fact]
	public async Task Chips_SelectedTagThatLeftTheIndex_ComesBackAsAStubWithZeroSessions()
	{
		Tagged(_dirA, "S", Linear("HSE-1"));
		var gone = Linear("GONE-1");

		var result = await Search("", null, gone);

		var chips = result["chips"]!.AsArray();
		Assert.Equal(["GONE-1", "HSE-1"], ChipKeys(result));
		Assert.Equal(0, chips[0]!["sessionCount"]!.GetValue<int>());
		Assert.True(chips[0]!["selected"]!.GetValue<bool>());
		Assert.Equal("linearIssue", chips[0]!["kind"]!.GetValue<string>());
		Assert.Equal(gone.Url, chips[0]!["url"]!.GetValue<string>());
	}

	[Fact]
	public async Task Chips_SelectedKeyMatchesCaseInsensitively_AndUsesTheIndexCopy()
	{
		Tagged(_dirA, "S", Linear("HSE-1"));
		var lower = Linear("hse-1");
		lower.Label = "stale label";

		var result = await Search("", null, lower);

		var chip = Assert.Single(result["chips"]!.AsArray())!;
		Assert.Equal("HSE-1", chip["label"]!.GetValue<string>());
		Assert.Equal(1, chip["sessionCount"]!.GetValue<int>());
		Assert.True(chip["selected"]!.GetValue<bool>());
	}

	[Fact]
	public async Task Chips_UrlsOutsideTheOpenPolicy_AreBlanked()
	{
		Tagged(_dirA, "S", Linear("HSE-1", url: "javascript:alert(1)"), Linear("HSE-2", url: "http://linear.app/x"), Linear("HSE-3", url: "https://evil.example/x"), Linear("HSE-4"));
		var stub = Linear("STUB-1", url: "https://evil.example/stub");

		var result = await Search("", null, stub);

		var urls = result["chips"]!.AsArray().ToDictionary(c => c!["key"]!.GetValue<string>(), c => c!["url"]!.GetValue<string>());
		Assert.Equal("", urls["HSE-1"]);
		Assert.Equal("", urls["HSE-2"]);
		Assert.Equal("", urls["HSE-3"]);
		Assert.StartsWith("https://linear.app/", urls["HSE-4"]);
		Assert.Equal("", urls["STUB-1"]);
	}

	// ── scope ─────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Scope_LimitsTheIndexToTheScopeSessionsDirectory()
	{
		var dirB = _env.AddDirectory("b");
		var inA = Tagged(_dirA, "InA", Linear("A-1"));
		Tagged(dirB, "InB", Linear("B-1"));

		var scoped = await Search("", inA.NodeId);
		var unscoped = await Search();

		Assert.Equal(["A-1"], ChipKeys(scoped));
		Assert.Equal(["A-1", "B-1"], ChipKeys(unscoped).OrderBy(k => k));
	}

	[Fact]
	public async Task Scope_OfAWorker_IsTheDirectoryOfItsTopLevelSession()
	{
		var dirB = _env.AddDirectory("b");
		var sup = Tagged(_dirA, "Sup", Linear("A-1"));
		var worker = _env.AddSession(dirB, "Worker in B", supervisorNodeId: sup.NodeId);
		Tagged(dirB, "Other in B", Linear("B-1"));

		var result = await Search("", worker.NodeId);

		Assert.Equal(["A-1"], ChipKeys(result));
	}

	[Fact]
	public async Task Scope_UnknownSession_IsSessionNotFound()
	{
		var response = await _env.CallRawAsync("tags.search", """{"text":"","scopeNodeId":"nope","selected":[]}""");

		Assert.Equal(-32000, RemoteTreeTestEnv.ErrorCode(response));
	}

	// ── empty reasons ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task EmptyReason_NoTagsAtAll_ScopedOrUnscoped()
	{
		var s = _env.AddSession(_dirA, "Untagged");

		var unscoped = await Search();
		var scoped = await Search("", s.NodeId);

		Assert.Equal("noTagsUnscoped", unscoped["emptyReason"]!.GetValue<string>());
		Assert.Equal("noTagsScoped", scoped["emptyReason"]!.GetValue<string>());
		Assert.Empty(unscoped["chips"]!.AsArray());
	}

	[Fact]
	public async Task EmptyReason_TagsExistButNoneMatchesTheText()
	{
		Tagged(_dirA, "S", Linear("HSE-1"));

		var result = await Search("zzz");

		Assert.Equal("noMatchingTags", result["emptyReason"]!.GetValue<string>());
	}

	[Fact]
	public async Task EmptyReason_IsOmittedWhenThereAreChips()
	{
		Tagged(_dirA, "S", Linear("HSE-1"));

		Assert.Null((await Search())["emptyReason"]);
	}

	// ── session groups ────────────────────────────────────────────────────────────

	[Fact]
	public async Task Sessions_NothingSelected_SelectTagsAndNoGroups()
	{
		Tagged(_dirA, "S", Linear("HSE-1"));

		var result = await Search();

		Assert.Equal("selectTags", result["sessionsEmptyReason"]!.GetValue<string>());
		Assert.Empty(result["groups"]!.AsArray());
	}

	[Fact]
	public async Task Sessions_SelectedTagWithoutSessionsInScope_NoSessions()
	{
		var dirB = _env.AddDirectory("b");
		var inA = Tagged(_dirA, "InA", Linear("A-1"));
		var b1 = Linear("B-1");
		Tagged(dirB, "InB", b1);

		var result = await Search("", inA.NodeId, b1);

		Assert.Equal("noSessions", result["sessionsEmptyReason"]!.GetValue<string>());
		Assert.Empty(result["groups"]!.AsArray());
	}

	[Fact]
	public async Task Sessions_UnionOfSelectedTags_GroupedByDirectory_DraftsFirstThenNewestActivity()
	{
		var dirB = _env.AddDirectory("b");
		var t1 = Linear("T-1");
		var t2 = Linear("T-2");
		var oldest = Tagged(_dirA, "Oldest", t1);
		var newest = Tagged(_dirA, "Newest", t2);
		var draft = Tagged(_dirA, "Draft", t1);
		var noActivity = Tagged(_dirA, "NoActivity", t1, t2);
		var inB = Tagged(dirB, "InB", t2);
		Tagged(_dirA, "Unrelated", Linear("X-1"));
		_env.Activity.Set(oldest.NodeId, T0);
		_env.Activity.Set(newest.NodeId, T0.AddHours(3));
		_env.Activity.Set(draft.NodeId, T0.AddHours(-10), hasDraft: true);
		_env.Activity.Set(inB.NodeId, T0);

		var result = await Search("", null, t1, t2);

		var groups = result["groups"]!.AsArray();
		Assert.Equal([_dirA.NodeId, dirB.NodeId], groups.Select(g => g!["directoryId"]!.GetValue<string>()));
		Assert.Equal(_dirA.Path, groups[0]!["directoryLabel"]!.GetValue<string>());
		Assert.Equal(["Draft", "Newest", "Oldest", "NoActivity"], groups[0]!["sessions"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));
		Assert.Equal(["InB"], groups[1]!["sessions"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));
		Assert.Null(result["sessionsEmptyReason"]);
		Assert.Equal(noActivity.NodeId, groups[0]!["sessions"]![3]!["id"]!.GetValue<string>());
		Assert.Equal(noActivity.NodeId, groups[0]!["sessions"]![3]!["state"]!["id"]!.GetValue<string>());
	}

	[Fact]
	public async Task Sessions_AWorkersTag_ListsItsTopLevelSupervisorOnly()
	{
		var tag = Linear("T-1");
		var sup = _env.AddSession(_dirA, "Sup");
		var worker = _env.AddSession(_dirA, "Worker", supervisorNodeId: sup.NodeId);
		_env.Tags.TagsByNode[worker.NodeId] = [tag];

		var result = await Search("", null, tag);

		var names = result["groups"]![0]!["sessions"]!.AsArray().Select(s => s!["name"]!.GetValue<string>());
		Assert.Equal(["Sup"], names);
	}

	[Fact]
	public async Task Sessions_SelectedTagsStayListedWhenTheTextFiltersTheirChipsOut()
	{
		var tag = Linear("HSE-1");
		Tagged(_dirA, "S", tag);

		var result = await Search("nothing-matches", null, tag);

		Assert.Equal(["S"], result["groups"]![0]!["sessions"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));
	}

	// ── params ────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("""{"selected":"x"}""")]
	[InlineData("""{"selected":[{"label":"no key"}]}""")]
	[InlineData("""{"selected":[{"key":"K","kind":"nonsense"}]}""")]
	[InlineData("""{"selected":[5]}""")]
	[InlineData("""{"text":5}""")]
	[InlineData("""{"scopeNodeId":5}""")]
	public async Task InvalidParams_AreRejected(string paramsJson)
	{
		var response = await _env.CallRawAsync("tags.search", paramsJson);

		Assert.Equal(-32602, RemoteTreeTestEnv.ErrorCode(response));
	}

	[Fact]
	public async Task AllParamsAreOptional()
	{
		var result = await _env.CallAsync("tags.search");

		Assert.Equal("noTagsUnscoped", result["emptyReason"]!.GetValue<string>());
		Assert.Equal("selectTags", result["sessionsEmptyReason"]!.GetValue<string>());
	}
}
