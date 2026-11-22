using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// TC.CR.04 to TC.CR.20 (daemon side): <c>autocomplete.suggest</c> in session mode (Code references off, FR.7.14) over the real registry of a
/// <see cref="ComposerWireEnv"/> and the fixture S of TC.CR.01, plus the unchanged symbol, file and path modes and the flip of the setting without a restart.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodeReferencesSessionSuggestTests : IDisposable
{
	private readonly ComposerWireEnv _env = new();
	private readonly string _pathP;
	private readonly string _pathQ;
	private readonly DirectoryNodeModel _p;
	private readonly GroupNodeModel _g1;
	private readonly GroupNodeModel _g2;
	private readonly SessionNodeModel _current;
	private readonly SessionNodeModel _alphaTests;
	private readonly SessionNodeModel _alphaLoose;
	private int _nextId;

	public CodeReferencesSessionSuggestTests()
	{
		_pathP = Path.Combine(_env.ProjectDirectory, "P");
		_pathQ = Path.Combine(_env.ProjectDirectory, "Q");
		var registry = _env.Registry;

		// Fixture S (TC.CR.01). Project P first, then Q, so the tree order is P, Q. Fixed ids keep the golden file stable.
		_p  = registry.AddDirectory(_pathP);
		_g1 = registry.AddGroup(_p, "G1");
		_g2 = registry.AddGroup(_p, "G2");
		_current    = Fix(registry.AddSessionToGroup(_g1, "Alpha build"));
		_alphaTests = Fix(registry.AddSessionToGroup(_g1, "Alpha tests"));
		Fix(registry.AddSessionToGroup(_g1, "Beta alpha"));
		Fix(registry.AddSessionToGroup(_g1, "alpha fav")).IsFavorite = true;
		Fix(registry.AddSessionToGroup(_g2, "Alpha docs"));
		_alphaLoose = Fix(registry.AddSession(_p, "Alpha loose"));
		Fix(registry.AddSession(_p, "Gamma"));

		var q = registry.AddDirectory(_pathQ);
		Fix(registry.AddSession(q, "Alpha web"));
		Fix(registry.AddSession(q, "Alpha qa"));

		Fix(registry.AddSessionToGroup(_g1, "Alpha worker")).SupervisorNodeId = _current.NodeId;
		Fix(registry.AddSessionToGroup(_g1, "Alpha deep")).OrchestrationDepth = 1;
	}

	public void Dispose() => _env.Dispose();

	private SessionNodeModel Fix(SessionNodeModel node)
	{
		node.NodeId = $"{++_nextId:D8}-0000-4000-8000-000000000000";
		return node;
	}

	private async Task<JsonNode> SuggestAsync(string text, int? caret = null, SessionNodeModel? from = null)
	{
		var response = await _env.Host.CallAsync("autocomplete.suggest", new JsonObject
		{
			["nodeId"] = (from ?? _current).NodeId, ["text"] = text, ["caret"] = caret ?? text.Length,
		});
		return response["result"]!;
	}

	private static string[] Labels(JsonNode result) => result["items"]!.AsArray().Select(i => i!["label"]!.GetValue<string>()).ToArray();

	private static string Mode(JsonNode result) => result["mode"]!.GetValue<string>();

	private void AddMatchingToP(int count)
	{
		for (var i = 0; i < count; i++)
			Fix(_env.Registry.AddSessionToGroup(_g2, $"Alpha extra {i:D2}"));
	}

	[Fact]
	public async Task TCCR04_ABareHash_AnswersNone()
	{
		var result = await SuggestAsync("see #");

		Assert.Equal("none", Mode(result));
		Assert.Empty(result["items"]!.AsArray());
	}

	[Theory]
	[InlineData("#zzz")]
	[InlineData("#alpha zz")]
	public async Task TCCR06_NoMatchingName_AnswersNone(string text)
	{
		var result = await SuggestAsync(text);

		Assert.Equal("none", Mode(result));
		Assert.Empty(result["items"]!.AsArray());
	}

	[Fact]
	public async Task TCCR05_ALineBreakBetweenTheHashAndTheCaret_AnswersNone() =>
		Assert.Equal("none", Mode(await SuggestAsync("#alpha\nte")));

	[Fact]
	public async Task TCCR03_ASpaceInTheQuery_StillMatches_AndTheCaretLimitsTheQuery()
	{
		var result = await SuggestAsync("#alpha te");
		Assert.Equal("session", Mode(result));
		Assert.Equal(["Alpha tests"], Labels(result));
		Assert.Equal((0, 9), (result["triggerStart"]!.GetValue<int>(), result["triggerLength"]!.GetValue<int>()));

		var midText = await SuggestAsync("#alpha te and more", caret: 9);
		Assert.Equal(["Alpha tests"], Labels(midText));
	}

	[Theory]
	[InlineData("#ALPHA tests", 0)]
	[InlineData("#pha te", 2)]
	[InlineData("#LPH", 1)]
	public async Task TCCR07_MatchingIsACaseInsensitiveSubstring_AndTheFirstOccurrenceIsMarked(string text, int matchStart)
	{
		var result = await SuggestAsync(text);

		var item = result["items"]!.AsArray().Single(i => i!["label"]!.GetValue<string>() == "Alpha tests")!;
		Assert.Equal(matchStart, item["matchStart"]!.GetValue<int>());
		Assert.Equal(text.Length - 1, item["matchLength"]!.GetValue<int>());
	}

	[Fact]
	public async Task TCCR08_09_TheTiersComeInOrder_AndWithinATierStartsWithFirst_ThenFavourites_ThenTreeOrder()
	{
		var result = await SuggestAsync("see #alpha");

		Assert.Equal("session", Mode(result));
		Assert.Equal(
		[
			"alpha fav", "Alpha tests", "Beta alpha",     // tier 1: the group G1 (favourite first, the contains-match last)
			"Alpha docs", "Alpha loose",                  // tier 2: the rest of P, the groups before the loose sessions (tree order)
			"Alpha web", "Alpha qa",                      // tier 3: the other project, only because fewer than 10 were found
		], Labels(result));
	}

	[Fact]
	public async Task TCCR10_TheOtherProjectsAreAddedOnlyBelowTenResults()
	{
		AddMatchingToP(4);     // 5 + 4 = 9 in tiers 1 and 2
		Assert.Contains("Alpha web", Labels(await SuggestAsync("#alpha")));

		AddMatchingToP(1);     // 10
		var ten = Labels(await SuggestAsync("#alpha"));
		Assert.Equal(10, ten.Length);
		Assert.DoesNotContain(ten, l => l is "Alpha web" or "Alpha qa");
	}

	[Fact]
	public async Task TCCR10_NineInTheNearbyTiers_AddTierThree()
	{
		AddMatchingToP(4);

		var labels = Labels(await SuggestAsync("#alpha"));

		Assert.Equal(11, labels.Length);
		Assert.Equal(["Alpha web", "Alpha qa"], labels.TakeLast(2));
	}

	[Fact]
	public async Task TCCR11_TheTotalNeverExceedsFifteen_AndFollowsTheTierOrder()
	{
		AddMatchingToP(30);

		var labels = Labels(await SuggestAsync("#alpha"));

		Assert.Equal(15, labels.Length);
		Assert.Equal(["alpha fav", "Alpha tests", "Beta alpha"], labels.Take(3));
		Assert.DoesNotContain(labels, l => l is "Alpha web" or "Alpha qa");
	}

	[Fact]
	public async Task TCCR12_WorkerSessionsAreNeverOffered()
	{
		var labels = Labels(await SuggestAsync("#alpha"));

		Assert.DoesNotContain("Alpha worker", labels);
		Assert.DoesNotContain("Alpha deep", labels);
		Assert.Empty(Labels(await SuggestAsync("#worker")));
		Assert.Empty(Labels(await SuggestAsync("#deep")));
	}

	[Fact]
	public async Task TCCR13_TheCurrentSessionIsNotOffered()
	{
		Assert.Equal("none", Mode(await SuggestAsync("#alpha build")));
		Assert.DoesNotContain("Alpha build", Labels(await SuggestAsync("#alpha")));
	}

	[Fact]
	public async Task TCCR14_ALooseSessionCountsAsOneGroup()
	{
		Fix(_env.Registry.AddSession(_p, "Alpha loose 2"));

		// From a loose session: the other loose sessions are tier 1, the grouped sessions of P tier 2.
		var fromLoose = Labels(await SuggestAsync("#alpha", from: _alphaLoose));
		Assert.Equal(
		[
			"Alpha loose 2",
			"alpha fav", "Alpha build", "Alpha tests", "Alpha docs", "Beta alpha",
			"Alpha web", "Alpha qa",
		], fromLoose);

		// From a grouped session the loose sessions are tier 2: after the group's contains-match.
		var fromGroup = Labels(await SuggestAsync("#alpha")).ToList();
		Assert.True(fromGroup.IndexOf("Beta alpha") < fromGroup.IndexOf("Alpha loose"));
		Assert.True(fromGroup.IndexOf("Beta alpha") < fromGroup.IndexOf("Alpha loose 2"));
	}

	[Fact]
	public async Task TCCR14_TwoGroupsWithTheSameName_AreNotTheSameGroup()
	{
		var twin = _env.Registry.AddGroup(_p, "G1");
		Fix(_env.Registry.AddSessionToGroup(twin, "Alpha twin"));

		var labels = Labels(await SuggestAsync("#alpha")).ToList();

		Assert.True(labels.IndexOf("Beta alpha") < labels.IndexOf("Alpha twin"));   // the twin group is tier 2, behind the whole of tier 1
	}

	[Fact]
	public async Task TCCR15_RowAndInsertFormat()
	{
		var result = await SuggestAsync("see #alpha te");

		var item = result["items"]!.AsArray().Single()!;
		var shortId = new SessionListIndex(_env.Registry.SnapshotSessionsForListing()).GetShortId(_alphaTests.NodeId);
		Assert.Equal(("session", "Alpha tests", $"{_pathP} / G1", $"session \"Alpha tests\" ({shortId})"),
			(item["kind"]!.GetValue<string>(), item["label"]!.GetValue<string>(), item["detail"]!.GetValue<string>(), item["insertText"]!.GetValue<string>()));
		Assert.Equal(Constants.Agent.ShortNodeIdMinLength, shortId.Length);
		Assert.Equal(_alphaTests.NodeId[..shortId.Length], shortId);
		Assert.Equal((4, 9), (result["triggerStart"]!.GetValue<int>(), result["triggerLength"]!.GetValue<int>()));
	}

	[Fact]
	public async Task TCCR15_ALooseSession_ShowsTheProjectLabelOnly()
	{
		var item = (await SuggestAsync("#alpha loose"))["items"]!.AsArray().Single()!;

		Assert.Equal(_pathP, item["detail"]!.GetValue<string>());
	}

	[Fact]
	public async Task TCCR15_SessionsWhoseIdsShareTheFirstEightCharacters_GetLongerIds()
	{
		_alphaTests.NodeId = "abcdef12-0001-0000-0000-000000000000";
		_alphaLoose.NodeId = "abcdef12-0002-0000-0000-000000000000";
		var index = new SessionListIndex(_env.Registry.SnapshotSessionsForListing());

		var items = (await SuggestAsync("#alpha"))["items"]!.AsArray();

		string IdOf(string label) => items.Single(i => i!["label"]!.GetValue<string>() == label)!["insertText"]!.GetValue<string>().Split('(', ')')[1];
		var tests = IdOf("Alpha tests");
		var loose = IdOf("Alpha loose");
		Assert.Equal(index.GetShortId(_alphaTests.NodeId), tests);
		Assert.Equal(index.GetShortId(_alphaLoose.NodeId), loose);
		Assert.NotEqual(tests, loose);
		Assert.True(tests.Length > Constants.Agent.ShortNodeIdMinLength);
	}

	[Fact]
	public async Task TCCR15_TheSessionAnswerHasTheGoldenShape()
	{
		var result = await SuggestAsync("see #alpha");

		Assert.Equal(7, result["items"]!.AsArray().Count);   // all three tiers
		AssertGolden("autocomplete.suggest.session", result);
	}

	[Fact]
	public async Task TCCR16_NoIndexIsAskedFor_AndTheAnswerIsReady()
	{
		var result = await SuggestAsync("#alpha").WaitAsync(TimeSpan.FromSeconds(5));   // the fake index would still be building

		Assert.True(result["indexReady"]!.GetValue<bool>());
		Assert.Equal(0, _env.CodeIndex.BuildStarts);
		Assert.Equal(0, _env.CodeIndex.Acquired);
	}

	[Fact]
	public async Task TCCR17_WithCodeReferencesOn_HashAnswersSymbols_AndNeverASession()
	{
		_env.Settings.Settings.CodeReferencesEnabled = true;
		_env.CodeIndex.Symbols =
		[
			new CodeSymbolModel { Name = "Foo", FullyQualifiedName = "Foo", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "Foo.cs", Line = 3 },
		];
		_env.CodeIndex.SetReady();

		AssertGolden("autocomplete.suggest.symbol", await SuggestAsync("#Fo"));
		Assert.Equal("symbol", Mode(await SuggestAsync("#alpha")));
	}

	[Fact]
	public async Task TCCR20_FlippingTheSetting_ChangesTheNextAnswer_WithoutARestart()
	{
		_env.CodeIndex.Symbols =
		[
			new CodeSymbolModel { Name = "Alphabet", FullyQualifiedName = "Alphabet", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "A.cs", Line = 1 },
		];
		_env.CodeIndex.SetReady();
		using var sections = new SettingsSectionService(_env.Settings, [new GeneralSettingsSection()]);

		Assert.Equal("session", Mode(await SuggestAsync("#alpha")));

		sections.Update("general", JsonNode.Parse("""{"codeReferencesEnabled":true}""")!, 1);
		Assert.Equal("symbol", Mode(await SuggestAsync("#alpha")));

		sections.Update("general", JsonNode.Parse("""{"codeReferencesEnabled":false}""")!, 2);
		Assert.Equal("session", Mode(await SuggestAsync("#alpha")));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task TCCR18_FilesAndDrivePaths_AnswerTheSameWhetherCodeReferencesIsOnOrOff(bool codeReferences)
	{
		_env.Settings.Settings.CodeReferencesEnabled = codeReferences;
		var tree = Path.Combine(_env.ProjectDirectory, "tree-" + codeReferences);
		string[] files = ["logo.png", "intro.mp4", "README.md", ".env", "data.csv", "index.html", "notes.txt", "Logo.cs"];
		Directory.CreateDirectory(tree);
		foreach (var file in files)
			File.WriteAllText(Path.Combine(tree, file), "x");

		var directory = _env.Registry.AddDirectory(tree);
		var session   = _env.Registry.AddSession(directory, "Tree session");
		var index = new CodeIndexService(_env.Settings);
		using var module = new AutocompleteRpcModule(_env.Registry, index, _env.Settings);
		using var host = new TdcpTestHost(new TdcpDispatcher([module]));

		async Task<JsonNode> Ask(string text) =>
			(await host.CallAsync("autocomplete.suggest", new JsonObject { ["nodeId"] = session.NodeId, ["text"] = text, ["caret"] = text.Length }))["result"]!;

		// The first request starts the index in the background (ISSUE.016); ask again until it is ready.
		JsonNode first;
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
		while (true)
		{
			first = await Ask("##logo");
			if (first["indexReady"]!.GetValue<bool>() || DateTime.UtcNow > deadline)
				break;
			await Task.Delay(100);
		}

		Assert.Equal("file", Mode(first));
		Assert.Equal(["Logo.cs", "logo.png"], Labels(first).Order(StringComparer.Ordinal));
		foreach (var (query, expected) in new[] { ("intro", "intro.mp4"), ("README", "README.md"), ("env", ".env"), ("data", "data.csv"), ("index", "index.html"), ("notes", "notes.txt") })
			Assert.Contains(expected, Labels(await Ask("##" + query)));

		var path = await Ask(tree + "\\");
		Assert.Equal("path", Mode(path));
		Assert.Equal(files.Order(StringComparer.Ordinal), Labels(path).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task TCCR19_AnIndexThatHoldsSymbols_IsNotUsedForHashWhileTheSettingIsOff()
	{
		var tree = Path.Combine(_env.ProjectDirectory, "symbols");
		Directory.CreateDirectory(tree);
		File.WriteAllText(Path.Combine(tree, "Foo.cs"), "namespace N { public class Foo {} }");
		var directory = _env.Registry.AddDirectory(tree);
		var session   = _env.Registry.AddSession(directory, "Symbols session");
		_env.Settings.Settings.CodeReferencesEnabled = true;
		var index = new CodeIndexService(_env.Settings);
		Assert.True(await index.GetOrCreateIndexAsync(tree));
		Assert.NotEmpty(index.SearchSymbols(tree, "Foo"));

		_env.Settings.Settings.CodeReferencesEnabled = false;
		using var module = new AutocompleteRpcModule(_env.Registry, index, _env.Settings);
		using var host = new TdcpTestHost(new TdcpDispatcher([module]));
		var result = (await host.CallAsync("autocomplete.suggest", new JsonObject { ["nodeId"] = session.NodeId, ["text"] = "#Foo", ["caret"] = 4 }))["result"]!;

		Assert.NotEmpty(index.SearchSymbols(tree, "Foo"));                        // not dropped
		Assert.NotEqual("symbol", Mode(result));                                  // and not used for #
		index.ReleaseIndex(tree);
	}

	// -- Golden ---------------------------------------------------------------------------------------------------------------------

	private void AssertGolden(string name, JsonNode result, [CallerFilePath] string thisFile = "")
	{
		// The temp paths of the two projects are not part of the shape.
		var actual = result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n")
			.Replace(_pathP.Replace("\\", "\\\\"), "<P>").Replace(_pathQ.Replace("\\", "\\\\"), "<Q>");
		var path = Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
			File.WriteAllText(path, actual + "\n");

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd(), actual.TrimEnd());
	}
}
