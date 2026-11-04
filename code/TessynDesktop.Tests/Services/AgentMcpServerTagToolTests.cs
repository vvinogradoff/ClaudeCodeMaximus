using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.10–TC.TG.13 — the cmx <c>tag_session</c> tool (FR.24.4, FR.24.6). Built the way <see cref="AgentMcpServerNotifyUserTests"/> builds the server.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerTagToolTests : IDisposable
{
	private readonly string _tempDir;
	private readonly TagTestEnv _tags = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly RecordingSessionFileService _files = new();
	private readonly AgentMcpServer _server;
	private SessionNodeModel _node = TagTestEnv.Node("top");

	public AgentMcpServerTagToolTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"cmx-tag-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_server = new AgentMcpServer(
			new AppSettingsService(_tempDir),
			new Lazy<ISessionTurnService>(() => new FakeTurnService()),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			new FakeSessionRegistry(),
			_files,
			modelService: null!,
			tdcpServer: null!,
			configMerger: null!,
			new FakeTelegramBridge(),
			new FakeNotificationService(),
			new FakeDirectoryLabelService(),
			_attention.Service,
			_tags.Service);
	}

	public void Dispose()
	{
		_tags.Dispose();
		_attention.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private void SeedHse() =>
		_tags.Store.SaveWorkspaces(new System.Collections.Generic.Dictionary<string, string> { ["HSE"] = "hardstop" });

	private Task<JsonNode> Call(JsonObject arguments, SessionNodeModel? caller = null) =>
		_server.HandleToolCallAsync(new JsonObject { ["name"] = "tag_session", ["arguments"] = arguments }, caller ?? _node);

	private static JsonArray Arr(params string[] values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

	private async Task<string> Tag(string[]? add = null, string[]? remove = null, SessionNodeModel? caller = null)
	{
		var args = new JsonObject();
		if (add != null)
			args["add"] = Arr(add);
		if (remove != null)
			args["remove"] = Arr(remove);
		return TextOf(await Call(args, caller));
	}

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private string[] SystemEntries() =>
		_files.Appended.Where(e => e.role == Constants.SessionFile.RoleSystem).Select(e => e.content).ToArray();

	private static JsonNode ToolsList() =>
		(JsonNode)typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

	private const string Hse230Url = "https://linear.app/hardstop/issue/HSE-230";

	// ── TC.TG.10 result lines ─────────────────────────────────────────────────

	[Fact]
	public async Task Add_FullLinearUrl_ReturnsTaggedLineThenTagsLine()   // TC.TG.10-1
	{
		Assert.Equal($"Tagged HSE-230 - {Hse230Url}\nTags: HSE-230.", await Tag([Hse230Url]));
	}

	[Fact]
	public async Task Add_Again_ReturnsAlreadyTagged()   // TC.TG.10-2
	{
		await Tag([Hse230Url]);

		Assert.Equal("Already tagged HSE-230.\nTags: HSE-230.", await Tag([Hse230Url]));
	}

	[Fact]
	public async Task AddThenRemoveAPullRequest_UsesTheExactLines()   // TC.TG.10-3
	{
		Assert.Equal(
			"Tagged windows-app PR #215 - https://github.com/tryhardstop/windows-app/pull/215\nTags: windows-app PR #215.",
			await Tag(["tryhardstop/windows-app#215"]));

		Assert.Equal("Removed windows-app PR #215.\nTags: none.", await Tag(remove: ["tryhardstop/windows-app#215"]));
	}

	[Fact]
	public async Task Add_InvalidEntry_ReturnsNotTaggedLine()   // TC.TG.10-4
	{
		Assert.Equal(
			"Not tagged: 'foo' - not a Linear issue, GitHub pull request or repository.\nTags: none.",
			await Tag(["foo"]));
	}

	[Fact]
	public async Task Add_UnknownTeam_IsRejected_ThenFullUrlAndShortKeySucceed()   // TC.TG.10-5
	{
		Assert.StartsWith(
			"Not tagged: 'ABC-123' - unknown Linear team 'ABC'; pass the full https://linear.app/<workspace>/issue/ABC-123 URL once.\n",
			await Tag(["ABC-123"]));

		Assert.StartsWith("Tagged ABC-123 - https://linear.app/acme/issue/ABC-123\n", await Tag(["https://linear.app/acme/issue/ABC-123"]));
		Assert.StartsWith("Tagged ABC-124 - https://linear.app/acme/issue/ABC-124\n", await Tag(["ABC-124"]));
	}

	[Fact]
	public async Task Remove_UntaggedEntry_ReturnsNotRemovedNotTagged()   // TC.TG.10-6
	{
		Assert.Equal("Not removed: 'owner/other' - not tagged.\nTags: none.", await Tag(remove: ["owner/other"]));
	}

	[Fact]
	public async Task Remove_NameDerivedKey_IsRefusedWithTheRenameHint()   // TC.TG.10-7
	{
		SeedHse();
		_node = TagTestEnv.Node("top", "HSE-150 Android backend");

		Assert.Equal(
			"Not removed: 'HSE-150' - it comes from the session name; rename the session to drop it.\nTags: HSE-150.",
			await Tag(remove: ["HSE-150"]));
	}

	[Fact]
	public async Task Remove_InvalidEntry_ReturnsNotRemovedInvalidLine()   // TC.TG.10-8
	{
		Assert.Equal(
			"Not removed: 'foo' - not a Linear issue, GitHub pull request or repository.\nTags: none.",
			await Tag(remove: ["foo"]));
	}

	[Fact]
	public async Task FinalLine_ListsLabelsInTagRowOrder()   // TC.TG.10-9, TC.TG.08-3
	{
		SeedHse();
		_node = TagTestEnv.Node("top", "HSE-150 spike");

		var text = await Tag(["o/repo", "o/r#7", "HSE-230"]);

		Assert.EndsWith("Tags: HSE-150, HSE-230, r PR #7, repo.", text);
	}

	[Fact]
	public async Task EchoedEntry_IsTheTrimmedInput_NotANormalisedForm()   // TC.TG.10-10
	{
		Assert.StartsWith("Not tagged: 'Foo/Bar#x' -", await Tag(["  Foo/Bar#x  "]));
	}

	// ── TC.TG.11 partial success, limits, listing ───────────────────────────────

	[Fact]
	public async Task Add_MixedValidAndInvalid_ReportsEachInOrderAndStoresTheValidOnes()   // TC.TG.11-1
	{
		SeedHse();

		var text = await Tag(["HSE-230", "foo", "owner/repo#5"]);

		Assert.Equal(
			$"Tagged HSE-230 - {Hse230Url}\n" +
			"Not tagged: 'foo' - not a Linear issue, GitHub pull request or repository.\n" +
			"Tagged repo PR #5 - https://github.com/owner/repo/pull/5\n" +
			"Tags: HSE-230, repo PR #5.",
			text);
	}

	[Fact]
	public async Task Entries_30AreAccepted_31AreAToolError_NothingChanges()   // TC.TG.11-2
	{
		var thirty = Enumerable.Range(1, 30).Select(n => $"o/r{n}").ToArray();
		await Tag(thirty);

		var thirtyOne = Enumerable.Range(1, 31).Select(n => $"p/r{n}").ToArray();
		var addEx = await Assert.ThrowsAnyAsync<Exception>(() => Tag(thirtyOne));
		var removeEx = await Assert.ThrowsAnyAsync<Exception>(() => Tag(remove: thirtyOne));
		Assert.EndsWith("McpToolException", addEx.GetType().Name);
		Assert.EndsWith("McpToolException", removeEx.GetType().Name);
		Assert.Equal(30, _tags.Service.GetTags(_node.NodeId, null).Count);

		await Tag(add: thirty, remove: thirty);   // 30 + 30
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public async Task NoArgumentsOrEmptyOrBlankArrays_ListOnly(int variant)   // TC.TG.11-3, 11-4
	{
		await Tag(["o/r#1"]);
		_files.Appended.Clear();
		var events = 0;
		_tags.Service.SessionTagsChanged += (_, _) => events++;

		var text = variant switch
		{
			0 => TextOf(await Call(new JsonObject())),
			1 => await Tag([], []),
			_ => await Tag(["", "  "]),
		};

		Assert.Equal("Tags: r PR #1.", text);
		Assert.Empty(_files.Appended);
		Assert.Equal(0, events);
	}

	[Fact]
	public async Task Listing_WithNoTags_SaysNone()   // TC.TG.11-3
	{
		Assert.Equal("Tags: none.", TextOf(await Call(new JsonObject())));
	}

	[Fact]
	public async Task AddEntries_AreReportedBeforeRemoveEntries()   // TC.TG.11-5
	{
		await Tag(["o/r#1"]);

		var lines = (await Tag(add: ["o/r#2"], remove: ["o/r#1"])).Split('\n');

		Assert.Equal("Tagged r PR #2 - https://github.com/o/r/pull/2", lines[0]);
		Assert.Equal("Removed r PR #1.", lines[1]);
	}

	[Fact]
	public async Task Entries_AreCaseInsensitive()   // TC.TG.11-6
	{
		SeedHse();

		var text = await Tag(["hse-230", "TRYHARDSTOP/WINDOWS-APP"]);

		Assert.StartsWith($"Tagged HSE-230 - {Hse230Url}\nTagged WINDOWS-APP - https://github.com/TRYHARDSTOP/WINDOWS-APP\n", text);
	}

	// ── TC.TG.12 SYSTEM entry ────────────────────────────────────────────────

	[Fact]
	public async Task SystemEntry_AddedOnly()   // TC.TG.12-1
	{
		SeedHse();

		await Tag(["HSE-230", "tryhardstop/windows-app#215"]);

		Assert.Equal(["[Tags] added HSE-230, windows-app PR #215"], SystemEntries());
		Assert.Equal(Constants.SessionFile.RoleSystem, _files.Appended.Single().role);
		Assert.Equal(_node.FileName, _files.Appended.Single().fileName);
	}

	[Fact]
	public async Task SystemEntry_RemovedOnly()   // TC.TG.12-2
	{
		await Tag(["tryhardstop/windows-app"]);
		_files.Appended.Clear();

		await Tag(remove: ["tryhardstop/windows-app"]);

		Assert.Equal(["[Tags] removed windows-app"], SystemEntries());
	}

	[Fact]
	public async Task SystemEntry_AddedAndRemoved_IsOneEntryWithLabels()   // TC.TG.12-3, 12-4
	{
		SeedHse();
		await Tag(["tryhardstop/windows-app"]);
		_files.Appended.Clear();

		await Tag(["HSE-230", "tryhardstop/windows-app#215"], ["tryhardstop/windows-app"]);

		Assert.Equal(["[Tags] added HSE-230, windows-app PR #215; removed windows-app"], SystemEntries());
	}

	[Fact]
	public async Task SystemEntry_NotWrittenWhenNothingChanged()   // TC.TG.12-5
	{
		await Tag(["o/r#1"]);
		_files.Appended.Clear();

		await Tag(["o/r#1"]);                       // all already tagged
		await Tag(["foo"]);                         // all rejected
		await Tag(remove: ["o/r#9"]);               // not removed only
		await Tag();                                // listing

		Assert.Empty(_files.Appended);
	}

	[Fact]
	public async Task SystemEntry_NamesOnlyTheAppliedEntries_AndIsAscii()   // TC.TG.12-6, TC.TG.06-7 spirit
	{
		await Tag(["foo", "o/r#1", "ABC-9"]);

		var entry = Assert.Single(SystemEntries());
		Assert.Equal("[Tags] added r PR #1", entry);
		Assert.All(entry, c => Assert.True(c < 128));
	}

	// ── TC.TG.13 workers, description ─────────────────────────────────────────

	[Fact]
	public async Task Worker_MayTag_StoresOnItsNodeAndWritesTheSystemEntry()   // TC.TG.13-1, 13-2
	{
		var worker = TagTestEnv.Node("worker", depth: 1);

		var text = await Tag(["tryhardstop/windows-app#215"], caller: worker);

		Assert.StartsWith("Tagged windows-app PR #215 - https://github.com/tryhardstop/windows-app/pull/215", text);
		Assert.DoesNotContain("Not posted", text);
		Assert.Single(_tags.Service.GetTags("worker", null));
		Assert.Equal(["[Tags] added windows-app PR #215"], SystemEntries());
		Assert.Equal("worker.txt", _files.Appended.Single().fileName);
	}

	[Fact]
	public void ToolsList_DescribesTagSession()   // TC.TG.13-3
	{
		var tool = ToolsList()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "tag_session")!;

		Assert.Equal(Constants.SessionTags.ToolDescription, tool["description"]!.GetValue<string>());
		Assert.StartsWith("Tag this session with the Linear issues, GitHub pull requests and GitHub repositories it works on.", Constants.SessionTags.ToolDescription);
		Assert.EndsWith("Call with no arguments to list the current tags.", Constants.SessionTags.ToolDescription);
		Assert.Contains("add/remove take full URLs (best) or short forms: HSE-230, owner/repo#215, owner/repo.", Constants.SessionTags.ToolDescription);

		var props = tool["inputSchema"]!["properties"]!.AsObject();
		Assert.Equal(["add", "remove"], props.Select(p => p.Key).OrderBy(k => k).ToArray());
		foreach (var name in new[] { "add", "remove" })
		{
			Assert.Equal("array", props[name]!["type"]!.GetValue<string>());
			Assert.Equal("string", props[name]!["items"]!["type"]!.GetValue<string>());
			Assert.Equal(30, props[name]!["maxItems"]!.GetValue<int>());
		}
	}
}
