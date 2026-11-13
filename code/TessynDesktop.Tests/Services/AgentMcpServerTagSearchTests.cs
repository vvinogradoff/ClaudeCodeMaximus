using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// <see cref="ISessionTagService"/> decorator over a real service that runs a hook (and counts) on every
/// <c>GetTags</c>, so lock tests can probe the settings lock from inside the tag read (TC.TG.53).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class HookedTagService : ISessionTagService
{
	private readonly ISessionTagService _inner;

	public HookedTagService(ISessionTagService inner) => _inner = inner;

	public Action? OnGetTags { get; set; }
	public int GetTagsCalls { get; private set; }

	public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged
	{
		add => _inner.SessionTagsChanged += value;
		remove => _inner.SessionTagsChanged -= value;
	}

	public SessionTagEditResultModel Edit(SessionNodeModel node, IReadOnlyList<string> add, IReadOnlyList<string> remove) =>
		_inner.Edit(node, add, remove);

	public IReadOnlyList<SessionTagModel> GetTags(string nodeId, string? sessionName)
	{
		GetTagsCalls++;
		OnGetTags?.Invoke();
		return _inner.GetTags(nodeId, sessionName);
	}

	public SessionTagParseResultModel Parse(string entry) => _inner.Parse(entry);

	public bool TakeTagGuidanceDue(string nodeId) => _inner.TakeTagGuidanceDue(nodeId);
}

/// <summary>
/// TC.TG.49–TC.TG.53 — the cmx <c>search_tags</c> tool and the <c>list_sessions</c> <c>tags</c> filter
/// (FR.24.15, FR.15.13), over a real <see cref="SessionRegistry"/> and a real <see cref="SessionTagService"/>.
/// Test names carry the variant id (<c>TC_TG_49_1_...</c>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerTagSearchTests : IDisposable
{
	private const string HseUrl   = "https://linear.app/hardstop/issue/HSE-254";
	private const string PrUrl    = "https://github.com/tryhardstop/backend/pull/303";
	private const string RepoUrl  = "https://github.com/tryhardstop/backend";
	private const string PrKey    = "tryhardstop/backend#303";
	private const string RepoKey  = "tryhardstop/backend";
	private const string PrLabel  = "backend PR #303";
	private const string HintsAll = "Sessions: list_sessions tags=[\"<url or key>\"].";
	private const string HintsProject = HintsAll + " Other projects: scope=all.";

	private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

	private readonly string _tempDir;
	private readonly AppSettingsService _settings;
	private readonly FakeSessionFileService _files = new();
	private readonly SessionRegistry _registry;
	private readonly TagTestEnv _env = new();
	private readonly HookedTagService _tags;
	private readonly AgentMcpServer _server;
	private readonly DirectoryNodeModel _dir1;
	private readonly DirectoryNodeModel _dir2;
	private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

	public AgentMcpServerTagSearchTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_env.Store.SaveWorkspaces(new Dictionary<string, string> { ["HSE"] = "hardstop" });
		_settings = new AppSettingsService(_tempDir);
		_registry = new SessionRegistry(_settings, _files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		_tags     = new HookedTagService(_env.Service);
		_server   = NewServer(_tags);
		_dir1     = _registry.AddDirectory(Path.Combine(_tempDir, "dir1"));
		_dir2     = _registry.AddDirectory(Path.Combine(_tempDir, "dir2"));
	}

	public void Dispose()
	{
		_env.Dispose();
		Directory.Delete(_tempDir, recursive: true);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private AgentMcpServer NewServer(ISessionTagService tags) => new(
		_settings,
		new Lazy<ISessionTurnService>(() => new RecordingTurnService()),
		new Lazy<ISchedulerService>(() => new RecordingScheduler()),
		_registry,
		_files,
		modelService: null!,
		tdcpServer: null!,
		configMerger: null!,
		new FakeTelegramBridge(),
		new FakeNotificationService(),
		new FakeDirectoryLabelService(),
		new AttentionService(new AttentionLedgerStore(Path.Combine(_tempDir, "attention")), _files),
		tags);

	/// <param name="minutesAgo">Minutes since the last write; null = the session file is missing.</param>
	private SessionNodeModel Add(string name, DirectoryNodeModel? dir = null, int? minutesAgo = 5, SessionNodeModel? supervisor = null)
	{
		var session = _registry.AddSession(dir ?? _dir1, name);
		if (minutesAgo != null)
			_files.LastWriteTimes[session.FileName] = _now.AddMinutes(-minutesAgo.Value);
		if (supervisor != null)
		{
			session.SupervisorNodeId   = supervisor.NodeId;
			session.OrchestrationDepth = 1;
		}
		return session;
	}

	private SessionNodeModel Tagged(string name, string tag, DirectoryNodeModel? dir = null, int? minutesAgo = 5, SessionNodeModel? supervisor = null)
	{
		var session = Add(name, dir, minutesAgo, supervisor);
		Tag(session, tag);
		return session;
	}

	private void Tag(SessionNodeModel session, params string[] entries) => _env.Service.Edit(session, entries, []);

	private Task<JsonNode> Call(string tool, JsonObject arguments, SessionNodeModel caller, AgentMcpServer? server = null) =>
		(server ?? _server).HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, caller);

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private async Task<string> Search(SessionNodeModel caller, JsonObject? arguments = null) =>
		TextOf(await Call("search_tags", arguments ?? new JsonObject(), caller));

	private async Task<string> List(SessionNodeModel caller, JsonObject? arguments = null, AgentMcpServer? server = null) =>
		TextOf(await Call("list_sessions", arguments ?? new JsonObject(), caller, server));

	private static JsonObject Args(string name, JsonNode? value) => new() { [name] = value };

	private static JsonArray Arr(params string[] values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

	private static string ShortOf(SessionNodeModel session) => session.NodeId[..Constants.Agent.ShortNodeIdMinLength];

	private static string PathOf(DirectoryNodeModel dir) => Path.TrimEndingDirectorySeparator(dir.Path);

	private static async Task<string> ErrorOf(Func<Task<JsonNode>> action)
	{
		var ex = await Assert.ThrowsAnyAsync<Exception>(action);
		return ex.Message;
	}

	private static JsonNode ToolsList()
	{
		var method = typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!;
		return (JsonNode)method.Invoke(null, null)!;
	}

	/// <summary>The tag rows of a <c>search_tags</c> text: every line carrying a URL.</summary>
	private static List<string> RowLines(string text) => text.Split('\n').Where(l => l.Contains("  http")).ToList();

	private static List<string> Labels(string text) => RowLines(text).Select(l => l.Split("  ")[0]).ToList();

	/// <summary>Names of the session rows of a <c>list_sessions</c> text, in order.</summary>
	private static List<string> ListedNames(string text) =>
		text.Split('\n')
			.Select(l => Regex.Match(l, @"^\s*[0-9a-f]{8}  (.+?)  "))
			.Where(m => m.Success)
			.Select(m => m.Groups[1].Value)
			.ToList();

	private bool CanAcquireSettingsLockFromAnotherThread() =>
		Task.Run(() =>
		{
			if (!Monitor.TryEnter(_settings.SyncRoot, LockTimeout))
				return false;

			Monitor.Exit(_settings.SyncRoot);
			return true;
		}).Result;

	/// <summary>dir1: Linear HSE-254, PR #303 (two sessions each partly), repo; dir2: HSE-254 and the repo.</summary>
	private (SessionNodeModel Caller, SessionNodeModel Hse, SessionNodeModel Pr, SessionNodeModel Repo, SessionNodeModel OtherHse) SeedMixed()
	{
		var caller   = Add("Caller", _dir1, minutesAgo: 1);
		var hse      = Tagged("Alpha", HseUrl, _dir1, 10);
		var pr       = Tagged("Beta", PrUrl, _dir1, 20);
		var repo     = Tagged("Gamma", RepoKey, _dir1, 30);
		var otherHse = Tagged("Epsilon", HseUrl, _dir2, 40);
		return (caller, hse, pr, repo, otherHse);
	}

	// ── TC.TG.49 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task TC_TG_49_1_ProjectScopeDefault_ListsOnlyTagsOfTheCallersDirectory()
	{
		var s = SeedMixed();
		Tagged("Zeta", RepoUrl, _dir2);

		var text = await Search(s.Caller);

		Assert.Equal(new[] { "HSE-254", PrLabel, "backend" }, Labels(text));
		Assert.Contains($"{ShortOf(s.Hse)}", text);
		Assert.DoesNotContain(ShortOf(s.OtherHse), text);
	}

	[Fact]
	public async Task TC_TG_49_1_ProjectScope_ForAWorkerCaller_UsesTheDirectoryItIsNestedUnder()
	{
		var s      = SeedMixed();
		var worker = Add("Helper", _dir1, supervisor: s.Caller);
		Tagged("Zeta", "tryhardstop/other", _dir2);

		var text = await Search(worker);

		Assert.Equal(new[] { "HSE-254", PrLabel, "backend" }, Labels(text));
		Assert.Contains("worker (depth 1) of " + ShortOf(s.Caller), text.Split('\n')[0]);
	}

	[Fact]
	public async Task TC_TG_49_2_AllScope_ReturnsTheTagsOfEveryDirectory()
	{
		var s = SeedMixed();
		Tagged("Zeta", "tryhardstop/other", _dir2);

		var text = await Search(s.Caller, Args("scope", "all"));

		Assert.Equal(new[] { "HSE-254", PrLabel, "other", "backend" }, Labels(text));
		Assert.Contains("2 sessions: ", RowLines(text)[0]);
	}

	[Fact]
	public async Task TC_TG_49_3_Matching_FollowsFr2411()
	{
		var s = SeedMixed();

		Assert.Equal(new[] { PrLabel }, Labels(await Search(s.Caller, Args("query", "backend 303"))));
		Assert.Equal(new[] { PrLabel }, Labels(await Search(s.Caller, Args("query", "#303"))));
		Assert.Equal(new[] { "HSE-254" }, Labels(await Search(s.Caller, Args("query", "hse"))));
		Assert.Equal(3, Labels(await Search(s.Caller, Args("query", "   "))).Count);

		var urlOnly = await Search(s.Caller, Args("query", "linear.app"));
		Assert.Empty(RowLines(urlOnly));
		Assert.Contains("No matching tags", urlOnly);
	}

	[Fact]
	public async Task TC_TG_49_4_Order_IsKindThenNewestActivityThenLabel_AsSessionTagSearchOrder()
	{
		var caller = Add("Caller", _dir1, minutesAgo: 1);
		var shared = _now.AddMinutes(-5);
		Tagged("n1", "HSE-1", minutesAgo: 10);
		Tagged("n2", "HSE-2", minutesAgo: 1);
		var tie4 = Tagged("n4", "HSE-4", minutesAgo: null);
		var tie5 = Tagged("n5", "HSE-5", minutesAgo: null);
		_files.LastWriteTimes[tie4.FileName] = shared;
		_files.LastWriteTimes[tie5.FileName] = shared;
		Tagged("n3", "HSE-3", minutesAgo: null);
		Tagged("n6", PrUrl, minutesAgo: 0);
		Tagged("n7", RepoKey, minutesAgo: 0);

		var text = await Search(caller);

		// Linear first (newest carrier, ties by label, no activity last), then the PR, then the repository.
		Assert.Equal(new[] { "HSE-2", "HSE-4", "HSE-5", "HSE-1", "HSE-3", PrLabel, "backend" }, Labels(text));

		var index = new SessionTagIndexProvider(_registry, _env.Service).GetIndex(_dir1.Path);
		var reference = SessionTagSearch.Order(index, id => _files.GetLastWriteTimeUtc(_registry.FindModelByNodeId(id)!.FileName));
		Assert.Equal(reference.Select(u => u.Tag.Label), Labels(text));
	}

	[Fact]
	public async Task TC_TG_49_5_RollUp_CountsAWorkersTagForItsTopLevelSupervisor()
	{
		var caller     = Add("Caller", _dir1);
		var supervisor = Add("Boss", _dir1);
		var worker     = Add("Helper", _dir1, supervisor: supervisor);
		Tag(worker, PrUrl);

		var text = await Search(caller);

		var row = Assert.Single(RowLines(text));
		Assert.EndsWith($"1 session: {ShortOf(supervisor)}", row);
		Assert.DoesNotContain(ShortOf(worker), row);
	}

	[Fact]
	public async Task TC_TG_49_6_Carriers_AreNewestFirst_AtMostFive_ThenPlusK()
	{
		var caller   = Add("Caller", _dir1);
		var carriers = Enumerable.Range(1, 7).Select(i => Tagged($"C{i}", HseUrl, minutesAgo: i)).ToList();
		var single   = Tagged("Solo", RepoKey, minutesAgo: 50);

		var rows = RowLines(await Search(caller));

		var expected = string.Join(", ", carriers.Take(5).Select(ShortOf));
		Assert.EndsWith($"7 sessions: {expected} +2", rows[0]);
		Assert.EndsWith($"1 session: {ShortOf(single)}", rows[1]);
	}

	[Fact]
	public async Task TC_TG_49_7_RepresentativeCopy_ComesFromTheIndex()
	{
		var caller = Add("Caller", _dir1);
		Add("HSE-254 login fix", _dir1);
		Tagged("Declared", HseUrl, _dir1);

		var row = Assert.Single(RowLines(await Search(caller)));

		Assert.StartsWith($"HSE-254  {HseUrl}  2 sessions: ", row);
	}

	// ── TC.TG.50 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task TC_TG_50_1_YouLine_IsTheOneOfListSessions()
	{
		var s = SeedMixed();
		var worker = Add("Helper", _dir1, supervisor: s.Caller);

		foreach (var caller in new[] { s.Caller, worker })
		{
			var searchFirst = (await Search(caller)).Split('\n')[0];
			var listFirst   = (await List(caller)).Split('\n')[0];
			Assert.Equal(listFirst, searchFirst);
			Assert.StartsWith("You: ", searchFirst);
		}
	}

	[Fact]
	public async Task TC_TG_50_2_Header_ProjectAndAllWithQuery()
	{
		var s = SeedMixed();

		var project = (await Search(s.Caller)).Split('\n')[1];
		var all     = (await Search(s.Caller, Args("scope", "all"))).Split('\n')[1];
		var query   = (await Search(s.Caller, Args("query", " backend "))).Split('\n')[1];

		Assert.Equal($"Tags in {PathOf(_dir1)} ({PathOf(_dir1)}) - 3 tags", project);
		Assert.Equal("Tags in all directories - 3 tags", all);
		Assert.Equal($"Tags in {PathOf(_dir1)} ({PathOf(_dir1)}) - 2 tags, matching \"backend\"", query);
	}

	[Fact]
	public async Task TC_TG_50_3_Row_UsesShortNodeIds()
	{
		var caller = Add("Caller", _dir1);
		var a = Tagged("A", HseUrl, minutesAgo: 1);
		var b = Tagged("B", HseUrl, minutesAgo: 2);

		var row = Assert.Single(RowLines(await Search(caller)));

		Assert.Equal($"HSE-254  {HseUrl}  2 sessions: {ShortOf(a)}, {ShortOf(b)}", row);
	}

	[Fact]
	public async Task TC_TG_50_4_Footer_PagingAndHints()
	{
		var s = SeedMixed();

		var first = (await Search(s.Caller, new JsonObject { ["scope"] = "all", ["limit"] = 2 })).Split('\n');
		var last  = (await Search(s.Caller, new JsonObject { ["scope"] = "all", ["limit"] = 2, ["offset"] = 2 })).Split('\n');
		var project = (await Search(s.Caller)).Split('\n');

		Assert.Equal("Rows 1-2 of 3. Next page: offset=2.", first[^2]);
		Assert.Equal(HintsAll, first[^1]);
		Assert.Equal("Rows 3-3 of 3.", last[^2]);
		Assert.Equal(HintsAll, last[^1]);
		Assert.Equal("Rows 1-3 of 3.", project[^2]);
		Assert.Equal(HintsProject, project[^1]);
	}

	[Fact]
	public async Task TC_TG_50_5_EmptyStates()
	{
		var caller = Add("Caller", _dir1);

		var noneProject = (await Search(caller)).Split('\n');
		var noneAll     = (await Search(caller, Args("scope", "all"))).Split('\n');
		Assert.Equal(new[] { "No tags in this directory", HintsProject }, noneProject[1..]);
		Assert.Equal(new[] { "No tags yet", HintsAll }, noneAll[1..]);

		Tagged("Alpha", HseUrl, _dir1);
		var noMatch = (await Search(caller, Args("query", "zzz"))).Split('\n');
		Assert.Equal(new[] { "No matching tags", HintsProject }, noMatch[1..]);

		var pastEnd = (await Search(caller, Args("offset", 99))).Split('\n');
		Assert.Equal("No rows at offset=99; 1 in total.", pastEnd[^2]);
		Assert.Equal(HintsProject, pastEnd[^1]);

		var otherDirCaller = Add("Lonely", _dir2);
		var otherDir = (await Search(otherDirCaller)).Split('\n');
		Assert.Equal("No tags in this directory", otherDir[1]);
	}

	// ── TC.TG.51 ──────────────────────────────────────────────────────────────

	[Fact]
	public void TC_TG_51_1_ToolsList_ListsSearchTagsWithTheFr2415Description()
	{
		var tool = ToolsList()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "search_tags")!;
		var properties = tool["inputSchema"]!["properties"]!.AsObject();

		Assert.Equal(
			"Find the Linear issues, GitHub pull requests and repositories that sessions are tagged with, and how many sessions carry each. Use it to find sessions by what they work on: search here, then call list_sessions with tags=[...]. Defaults to your project; scope=all for every directory.",
			tool["description"]!.GetValue<string>());
		Assert.Equal(new[] { "limit", "offset", "query", "scope" }, properties.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
		Assert.Equal(new[] { "project", "all" }, properties["scope"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
		Assert.Equal("integer", properties["limit"]!["type"]!.GetValue<string>());
		Assert.Equal("integer", properties["offset"]!["type"]!.GetValue<string>());
	}

	[Fact]
	public void TC_TG_51_1_ToolsList_ListSessionsDeclaresTags_AsAnArrayOfStrings()
	{
		var tool = ToolsList()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "list_sessions")!;
		var tags = tool["inputSchema"]!["properties"]!["tags"]!;

		Assert.Equal("array", tags["type"]!.GetValue<string>());
		Assert.Equal("string", tags["items"]!["type"]!.GetValue<string>());
		Assert.Contains("tags=[...]", tool["description"]!.GetValue<string>());
	}

	[Theory]
	[InlineData("galaxy")]
	[InlineData("project,all")]
	[InlineData("children")]
	public async Task TC_TG_51_2_ScopeParsing_UnknownScopeIsAToolError(string scope)
	{
		var caller = Add("Caller");

		var error = await ErrorOf(() => Call("search_tags", Args("scope", scope), caller));

		Assert.Contains($"Unknown scope '{scope}'", error);
	}

	[Fact]
	public async Task TC_TG_51_2_ScopeParsing_NonStringScopeIsAToolError_AndPaddedUpperCaseWorks()
	{
		var s = SeedMixed();

		var error = await ErrorOf(() => Call("search_tags", Args("scope", 2), s.Caller));
		var text  = await Search(s.Caller, Args("scope", " ALL "));

		Assert.Contains("'scope' must be a string", error);
		Assert.Equal("Tags in all directories - 3 tags", text.Split('\n')[1]);
	}

	[Fact]
	public async Task TC_TG_51_3_Paging_LimitIsClamped_NegativeOffsetCountsAsZero()
	{
		var caller = Add("Caller");
		for (var i = 1; i <= 3; i++)
			Tagged($"T{i}", $"HSE-{i}");

		var limitZero = (await Search(caller, Args("limit", 0))).Split('\n');
		var limitHuge = (await Search(caller, Args("limit", 100000))).Split('\n');
		var negative  = (await Search(caller, Args("offset", -5))).Split('\n');

		Assert.Equal("Rows 1-1 of 3. Next page: offset=1.", limitZero[^2]);
		Assert.Equal("Rows 1-3 of 3.", limitHuge[^2]);
		Assert.Equal("Rows 1-3 of 3.", negative[^2]);
	}

	[Theory]
	[InlineData("limit", "x", "'limit' must be an integer")]
	[InlineData("offset", 1.5, "'offset' must be an integer")]
	[InlineData("query", 5, "'query' must be a string")]
	public async Task TC_TG_51_3_WrongJsonTypes_AreToolErrorsNamingTheArgument(string name, object value, string expected)
	{
		var caller = Add("Caller");
		JsonNode node = value switch
		{
			string s => JsonValue.Create(s)!,
			double d => JsonValue.Create(d)!,
			_        => JsonValue.Create((int)value)!,
		};

		var error = await ErrorOf(() => Call("search_tags", Args(name, node), caller));

		Assert.Contains(expected, error);
	}

	// ── TC.TG.52 ──────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(HseUrl, "Alpha")]
	[InlineData("HSE-254", "Alpha")]
	[InlineData("hse-254", "Alpha")]
	[InlineData(PrUrl, "Beta")]
	[InlineData(PrKey, "Beta")]
	[InlineData(RepoKey, "Gamma")]
	[InlineData(RepoUrl, "Gamma")]
	public async Task TC_TG_52_1_Forms_EachKeepsExactlyTheCarryingSessions(string entry, string expected)
	{
		var s = SeedMixed();

		var names = ListedNames(await List(s.Caller, Args("tags", Arr(entry))));

		Assert.Equal(new[] { expected }, names);
	}

	[Fact]
	public async Task TC_TG_52_2_Or_TwoTagsListTheSessionsCarryingEither()
	{
		var s = SeedMixed();

		var names = ListedNames(await List(s.Caller, Args("tags", Arr("HSE-254", RepoKey))));

		Assert.Equal(new[] { "Alpha", "Gamma" }, names);
	}

	[Fact]
	public async Task TC_TG_52_3_RollUp_ATopLevelSessionIsListedForItsWorkersTag()
	{
		var caller   = Add("Caller");
		var top      = Add("Top");
		var worker   = Add("TopWorker", supervisor: top);
		Tag(worker, "https://github.com/o/w/pull/9");
		var other    = Add("Other");
		Add("OtherWorker", supervisor: other);

		var plain    = await List(caller, Args("tags", Arr("o/w#9")));
		var withAll  = await List(caller, new JsonObject { ["tags"] = Arr("o/w#9"), ["includeWorkers"] = true });

		Assert.Equal(new[] { "Top" }, ListedNames(plain));
		Assert.Contains("1 worker", plain);
		Assert.Equal(new[] { "Top", "TopWorker" }, ListedNames(withAll));
		Assert.Contains("  " + ShortOf(worker) + "  TopWorker", withAll);
	}

	[Fact]
	public async Task TC_TG_52_4_Combines_ScopeQueryRunningAndPaging()
	{
		var s = SeedMixed();

		var project = ListedNames(await List(s.Caller, Args("tags", Arr("HSE-254"))));
		var all     = ListedNames(await List(s.Caller, new JsonObject { ["tags"] = Arr("HSE-254"), ["scope"] = "all" }));
		var query   = ListedNames(await List(s.Caller, new JsonObject { ["tags"] = Arr("HSE-254"), ["scope"] = "all", ["query"] = "eps" }));
		_registry.SetSessionRunning(s.Hse.NodeId, true);
		var running = ListedNames(await List(s.Caller, new JsonObject { ["tags"] = Arr("HSE-254"), ["scope"] = "all", ["running"] = true }));
		var paged   = await List(s.Caller, new JsonObject { ["tags"] = Arr("HSE-254"), ["scope"] = "all", ["limit"] = 1, ["offset"] = 1 });

		Assert.Equal(new[] { "Alpha" }, project);
		Assert.Equal(new[] { "Alpha", "Epsilon" }, all);
		Assert.Equal(new[] { "Epsilon" }, query);
		Assert.Equal(new[] { "Alpha" }, running);
		Assert.Equal(new[] { "Epsilon" }, ListedNames(paged));
		Assert.Contains("Rows 2-2 of 2.", paged);
	}

	[Fact]
	public async Task TC_TG_52_5_UnknownLinearTeam_FiltersByItsKey_AndDoesNotChangeTheMap()
	{
		using var env = new TagTestEnv();
		env.Store.SaveWorkspaces(new Dictionary<string, string> { ["ABC"] = "acme" });
		var caller  = Add("Caller");
		var carrier = Add("Carrier");
		env.Service.Edit(carrier, ["ABC-12"], []);
		env.Store.SaveWorkspaces(new Dictionary<string, string>());
		var restarted = NewServer(env.NewServiceOverSameRoot());

		var listed   = ListedNames(await List(caller, Args("tags", Arr("ABC-12")), restarted));
		var notThere = await List(caller, Args("tags", Arr("ZZZ-1")), restarted);

		Assert.Equal(new[] { "Carrier" }, listed);
		Assert.Contains("No sessions match.", notThere);
		Assert.Empty(env.Store.LoadWorkspaces());
	}

	[Fact]
	public async Task TC_TG_52_6_Errors_NotATag_TooManyEntries_BlankMeansNoFilter()
	{
		var s = SeedMixed();

		var notATag  = await ErrorOf(() => Call("list_sessions", Args("tags", Arr("HSE-254", "xyz")), s.Caller));
		var tooMany  = await ErrorOf(() => Call("list_sessions", Args("tags", Arr(Enumerable.Repeat("HSE-254", 31).ToArray())), s.Caller));
		var wrongType = await ErrorOf(() => Call("list_sessions", Args("tags", "HSE-254"), s.Caller));
		var unfiltered = await List(s.Caller);
		var emptyArray = await List(s.Caller, Args("tags", new JsonArray()));
		var blanks     = await List(s.Caller, Args("tags", Arr("", "   ")));
		var atLimit    = await List(s.Caller, Args("tags", Arr(Enumerable.Repeat("HSE-254", 30).ToArray())));

		Assert.Equal("Not a tag: 'xyz' - not a Linear issue, GitHub pull request or repository.", notATag);
		Assert.Contains("at most 30", tooMany);
		Assert.Contains("'tags' must be an array of strings", wrongType);
		Assert.Equal(unfiltered, emptyArray);
		Assert.Equal(unfiltered, blanks);
		Assert.Equal(new[] { "Alpha" }, ListedNames(atLimit));
	}

	[Fact]
	public async Task TC_TG_52_7_NoCarrier_GivesNoSessionsMatchWithTheUsualHints()
	{
		var s = SeedMixed();

		var lines = (await List(s.Caller, Args("tags", Arr("tryhardstop/nobody")))).Split('\n');

		Assert.Equal("No sessions match.", lines[^2]);
		Assert.Equal("Other projects: scope=all. Narrow: query=, running=, activeWithinHours=.", lines[^1]);
	}

	// ── TC.TG.53 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task TC_TG_53_1_Lock_SettingsLockIsFreeWhileTagsAreRead()
	{
		var s = SeedMixed();
		var acquired = new List<bool>();
		_tags.OnGetTags = () => acquired.Add(CanAcquireSettingsLockFromAnotherThread());

		await Search(s.Caller, Args("scope", "all"));
		var afterSearch = acquired.Count;
		await List(s.Caller, Args("tags", Arr("HSE-254")));

		Assert.True(afterSearch > 0);
		Assert.True(acquired.Count > afterSearch);
		Assert.All(acquired, Assert.True);
	}

	[Fact]
	public async Task TC_TG_53_1_Lock_SettingsLockIsFreeWhileLastWriteTimesAreRead()
	{
		var s = SeedMixed();
		var acquired = new List<bool>();
		_files.OnGetLastWriteTime = _ => acquired.Add(CanAcquireSettingsLockFromAnotherThread());

		await Search(s.Caller, Args("scope", "all"));

		Assert.NotEmpty(acquired);
		Assert.All(acquired, Assert.True);
	}

	[Fact]
	public async Task TC_TG_53_2_NoContentReads_NeitherToolReadsSessionFileContents()
	{
		var s = SeedMixed();

		await Search(s.Caller);
		await Search(s.Caller, Args("scope", "all"));
		await List(s.Caller, Args("tags", Arr("HSE-254")));

		Assert.Equal(0, _files.ReadEntriesCalls);
	}

	// ── Review round: roll-up across directories, cycles, learning, callers outside the tree ──

	[Fact]
	public async Task RollUp_AcrossDirectories_WorkerInB_UnderSupervisorInA_CountsForTheSupervisorsDirectory()
	{
		var callerA = Add("CallerA", _dir1);
		var callerB = Add("CallerB", _dir2);
		var top     = Add("Top", _dir1);
		Tagged("Helper", PrUrl, _dir2, supervisor: top);

		var searchA   = await Search(callerA);
		var searchB   = await Search(callerB);
		var searchAll = await Search(callerB, Args("scope", "all"));
		var listA     = await List(callerA, Args("tags", Arr(PrUrl)));
		var listAWithWorkers = await List(callerA, new JsonObject { ["tags"] = Arr(PrUrl), ["includeWorkers"] = true });
		var listB     = await List(callerB, new JsonObject { ["tags"] = Arr(PrUrl), ["includeWorkers"] = true });
		var listAll   = await List(callerB, new JsonObject { ["tags"] = Arr(PrUrl), ["scope"] = "all", ["includeWorkers"] = true });

		Assert.EndsWith($"1 session: {ShortOf(top)}", Assert.Single(RowLines(searchA)));
		Assert.Contains("No tags in this directory", searchB);
		Assert.EndsWith($"1 session: {ShortOf(top)}", Assert.Single(RowLines(searchAll)));
		Assert.Equal(new[] { "Top" }, ListedNames(listA));
		Assert.Equal(new[] { "Top" }, ListedNames(listAWithWorkers));
		Assert.Contains("No sessions match.", listB);
		Assert.Equal(new[] { "Helper", "Top" }, ListedNames(listAll).OrderBy(n => n, StringComparer.Ordinal));
	}

	[Fact]
	public async Task SupervisorCycle_IsRolledUpToTheLowestNodeId_ByBothTools()
	{
		var caller = Add("Caller", _dir1);
		var a = Add("CycleA", _dir1);
		var b = Add("CycleB", _dir1);
		var c = Add("CycleC", _dir1);
		a.SupervisorNodeId = b.NodeId;
		b.SupervisorNodeId = c.NodeId;
		c.SupervisorNodeId = a.NodeId;
		foreach (var s in new[] { a, b, c })
			s.OrchestrationDepth = 1;
		Tag(b, PrUrl);
		var lowest = new[] { a, b, c }.OrderBy(s => s.NodeId, StringComparer.Ordinal).First();

		var search = await Search(caller);
		var list   = await List(caller, new JsonObject { ["tags"] = Arr(PrUrl), ["includeWorkers"] = true });

		Assert.EndsWith($"1 session: {ShortOf(lowest)}", Assert.Single(RowLines(search)));
		Assert.Equal(new[] { "CycleA", "CycleB", "CycleC" }, ListedNames(list).OrderBy(n => n, StringComparer.Ordinal));
	}

	[Fact]
	public async Task ListSessionsTags_FullLinearUrlOfANewTeam_Filters_AndDoesNotChangeTheMap()
	{
		using var env = new TagTestEnv();
		env.Store.SaveWorkspaces(new Dictionary<string, string> { ["NEW"] = "newws" });
		var caller  = Add("Caller");
		var carrier = Add("Carrier");
		env.Service.Edit(carrier, ["NEW-7"], []);
		env.Store.SaveWorkspaces(new Dictionary<string, string>());
		var restarted = NewServer(env.NewServiceOverSameRoot());

		var listed = ListedNames(await List(caller, Args("tags", Arr("https://linear.app/newws/issue/NEW-7")), restarted));

		Assert.Equal(new[] { "Carrier" }, listed);
		Assert.Empty(env.Store.LoadWorkspaces());
	}

	[Fact]
	public async Task SearchTags_CallerOutsideTheTree_ProjectScopeIsEmpty_AllScopeStillListsTags()
	{
		Tagged("Alpha", HseUrl, _dir1);
		var ghost = TagTestEnv.Node("ghost-node", "Ghost");

		var project = (await Search(ghost)).Split('\n');
		var all     = (await Search(ghost, Args("scope", "all"))).Split('\n');

		Assert.Equal(new[] { "No tags in this directory", HintsProject }, project);
		Assert.Equal("Tags in all directories - 1 tags", all[0]);
		Assert.StartsWith("HSE-254  ", all[1]);
	}
}
