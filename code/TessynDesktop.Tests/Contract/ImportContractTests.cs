using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J4 on the wire (global search, import, schedules, the attention list of all sessions, the daemon health): golden result shapes, loopback-only
/// registration (the relay answers -32601), the capability flags, the error codes, and the rule that no result or error message carries a local path.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportContractTests : IDisposable
{
	public static readonly string[] J4Methods =
	[
		"search.global", "search.reindex",
		"import.list", "import.titles", "import.run",
		"schedules.list", "schedules.add", "schedules.remove",
		"attention.listAll", "daemon.health",
	];

	private static readonly DateTimeOffset FireAt = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

	private readonly ImportWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	// ── Registration, capabilities ───────────────────────────────────────────────────────────

	[Fact]
	public void TheJ4Methods_AreExactlyTheTen_AllLoopbackOnly()
	{
		var registry = new TdcpMethodRegistry();
		new ImportRpcModule(_env.Import.Service).Register(registry);
		new SearchRpcModule(_env.Search).Register(registry);
		new ScheduleRpcModule(_env.Schedules).Register(registry);
		new DaemonHealthRpcModule(_env.Health).Register(registry);
		new AttentionRpcModule(_env.Import.Registry, _env.Attention.Service, null!, null!, _env.Overview).Register(registry);

		var added = registry.Entries.Where(e => J4Methods.Contains(e.Name)).ToList();

		Assert.Equal(J4Methods.Order(), added.Select(e => e.Name).Order());
		Assert.All(added, e => Assert.False(e.RemoteCallable, e.Name));
	}

	[Fact]
	public void TheAttentionModule_RegistersTheListAllMethodOnlyWhenTheOverviewIsWired()
	{
		var registry = new TdcpMethodRegistry();
		new AttentionRpcModule(_env.Import.Registry, _env.Attention.Service, null!, null!).Register(registry);

		Assert.DoesNotContain(registry.Entries, e => e.Name == "attention.listAll");
		Assert.Contains(registry.Entries, e => e.Name == "attention.list" && e.RemoteCallable); // the web methods are untouched
	}

	[Theory]
	[MemberData(nameof(MethodNames))]
	public async Task EveryJ4Method_IsUnknownToTheRelay_ButKnownOnLoopback(string method)
	{
		var relay = await _env.CallAsRelayAsync(method);
		var loopback = await _env.CallAsync(method);

		Assert.Equal(-32601, relay["error"]!["code"]!.GetValue<int>());
		Assert.NotEqual(-32601, loopback["error"]?["code"]?.GetValue<int>() ?? 0);
	}

	public static TheoryData<string> MethodNames => new(J4Methods);

	[Fact]
	public void TheJ4MethodNames_AreNotPartOfTheRemoteMethodContract()
	{
		var contract = typeof(Constants.Remote.Methods).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToHashSet();

		Assert.DoesNotContain(J4Methods, contract.Contains);
	}

	[Fact]
	public async Task Hello_AnnouncesTheFiveCapabilities_WhenTheirServicesAreWired()
	{
		var result = (await _env.Host.CallAsync("client.hello"))["result"]!;

		var capabilities = result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.Equal(["events.local", "events.resume", "search.global", "import", "schedules", "attention.all", "daemon.health"], capabilities);
	}

	[Fact]
	public async Task Hello_OverTheRelay_AnnouncesNoCapabilityOfALoopbackOnlyPackage()
	{
		var request = """{"jsonrpc":"2.0","id":1,"method":"client.hello","params":{}}""";

		var relay = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Relay, CancellationToken.None);
		var loopback = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Loopback, CancellationToken.None);

		var relayCaps = relay!["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		var loopbackCaps = loopback!["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.Equal(["events.local", "events.resume"], relayCaps);
		Assert.Contains("import", loopbackCaps);
	}

	[Fact]
	public async Task AFireTimeWithoutAnOffset_IsReadAsUtc()
	{
		var added = await ResultAsync("schedules.add", Args(("nodeId", _env.Session.NodeId), ("prompt", "p"), ("kind", "at"), ("fireAtUtc", "2026-11-05T09:00:00")));
		var withOffset = await ResultAsync("schedules.add", Args(("nodeId", _env.Session.NodeId), ("prompt", "p"), ("kind", "at"), ("fireAtUtc", "2026-11-05T11:00:00+02:00")));

		Assert.Equal(FireAt, added["fireAtUtc"]!.GetValue<DateTimeOffset>());
		Assert.Equal(FireAt, withOffset["fireAtUtc"]!.GetValue<DateTimeOffset>());
	}

	[Fact]
	public async Task Hello_AnnouncesNoJ4Capability_WithoutTheServices()
	{
		using var bare = new ImportWireEnv(withServices: false);

		var result = (await bare.Host.CallAsync("client.hello"))["result"]!;

		Assert.Equal(["events.local", "events.resume"], result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()));
	}

	[Fact]
	public void TheScheduleChangedNotification_IsLocalOnly()
	{
		var notification = new RemoteNotification("schedule.changed", new JsonObject(), 1);

		Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(notification));
		Assert.Equal(["schedule.changed"], RemoteProtocol.Schedules.LocalOnlyEvents);
	}

	// ── Result shapes ────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task SearchGlobal_ResultShape()
	{
		_env.Import.Registry.AddSession(_env.Directory, "Imported one").ExternalId = "claude-1";
		_env.Import.Index.SearchResult =
		[
			new SessionSearchResultModel { ExternalId = "claude-1", ProjectSlug = "C--proj", Content = "  the build is broken  ", Role = "user", Rank = -3.5, SessionTitle = "Fix build" },
			new SessionSearchResultModel { ExternalId = "claude-2", ProjectSlug = "C--other", Content = "another match", Role = "assistant", Rank = -1.25 },
		];

		AssertGolden("search.global", await ResultAsync("search.global", Args(("query", "build"))));
	}

	[Fact]
	public async Task SearchReindex_ResultShape() =>
		AssertGolden("search.reindex", await ResultAsync("search.reindex", Args()));

	[Fact]
	public async Task ImportList_ResultShape_ForADirectory()
	{
		_env.AddClaudeSession("claude-a", "First prompt");
		_env.AddClaudeSession("claude-b", "Second prompt", messages: 4);
		_env.Import.Importer.CacheTitle("claude-a", "A cached title");

		AssertGolden("import.list", await ResultAsync("import.list", Args(("directoryId", _env.Directory.NodeId))));
	}

	[Fact]
	public async Task ImportList_ResultShape_Global()
	{
		_env.AddClaudeSession("claude-a", "First prompt", slug: "C--Projects-Other");
		_env.Import.Index.SearchResult = [new SessionSearchResultModel { ExternalId = "claude-a", ProjectSlug = "C--Projects-Other", Content = "x", Role = "user" }];

		AssertGolden("import.list.global", await ResultAsync("import.list", Args(("global", true), ("query", "First"))));
	}

	[Fact]
	public async Task ImportTitles_ResultShape()
	{
		_env.Import.Importer.CacheTitle("claude-a", "A cached title");

		AssertGolden("import.titles", await ResultAsync("import.titles", Args(("externalIds", new JsonArray("claude-a")))));
	}

	[Fact]
	public async Task ImportRun_ResultShape_ImportedAndFailed()
	{
		_env.AddClaudeSession("claude-a", "First prompt");

		var items = new JsonArray(new JsonObject { ["externalId"] = "claude-a", ["name"] = "My import" }, new JsonObject { ["externalId"] = "ghost" });
		var result = await ResultAsync("import.run", Args(("targetId", _env.Directory.NodeId), ("items", items)));

		AssertGolden("import.run", result);
		Assert.Equal("My import", _env.Directory.Sessions.Last().Name);
	}

	[Fact]
	public async Task Schedules_ResultShapes_AddListRemove()
	{
		var add = await ResultAsync("schedules.add", Args(("nodeId", _env.Session.NodeId), ("prompt", "wake up"), ("kind", "at"), ("fireAtUtc", FireAt.ToString("O")), ("note", "morning")));
		var cron = await ResultAsync("schedules.add", Args(("nodeId", _env.Session.NodeId), ("prompt", "tick"), ("kind", "cron"), ("cron", "0 9 * * *"), ("missedFire", "skip"), ("maxFires", 3)));
		var list = await ResultAsync("schedules.list", Args(("nodeId", _env.Session.NodeId)));
		var remove = await ResultAsync("schedules.remove", Args(("scheduleId", add["id"]!.GetValue<string>())));
		var removeAgain = await ResultAsync("schedules.remove", Args(("scheduleId", add["id"]!.GetValue<string>())));

		AssertGolden("schedules.add", add);
		AssertGolden("schedules.add.cron", cron);
		AssertGolden("schedules.list", list);
		AssertGolden("schedules.remove", remove);
		Assert.False(removeAgain["removed"]!.GetValue<bool>());
		Assert.Single((await ResultAsync("schedules.list", Args()))["schedules"]!.AsArray());
	}

	[Fact]
	public async Task AttentionListAll_ResultShape()
	{
		var other = _env.Import.Registry.AddSession(_env.Directory, "Other session");
		_env.Attention.Service.Post(_env.Session, [AttentionTestEnv.Req("Which database?", AttentionKind.Ask, options: ["pg", "sqlite"], recommendation: "pg")]);
		_env.Attention.Clock.Advance(TimeSpan.FromMinutes(1));
		_env.Attention.Service.Post(other, [AttentionTestEnv.Req("Disk almost full", AttentionKind.Critical, body: "98%")]);

		AssertGolden("attention.listAll", await ResultAsync("attention.listAll", Args()));
	}

	[Fact]
	public async Task DaemonHealth_ResultShape()
	{
		_env.IndexActivity.ActiveScans = 2;
		_env.IndexActivity.LastScanFinishedUtc = ImportWireEnv.Started.AddMinutes(5);
		_env.Attention.Clock.Advance(TimeSpan.FromHours(3));

		AssertGolden("daemon.health", await ResultAsync("daemon.health", Args()));
	}

	[Fact]
	public async Task ReadOnlyResults_AreTheSame_InProcessAndOverLoopbackHttp()
	{
		_env.AddClaudeSession("claude-a");

		foreach (var (method, args) in new (string, JsonNode)[] { ("import.list", Args(("directoryId", _env.Directory.NodeId))), ("schedules.list", Args()), ("attention.listAll", Args()) })
		{
			var viaHttp = (await _env.CallAsync(method, args))["result"]!.ToJsonString();
			var inProcess = (await ResultAsync(method, args)).ToJsonString();

			Assert.Equal(inProcess, viaHttp);
		}
	}

	// ── Errors ───────────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("search.global", """{}""")]
	[InlineData("search.global", """{"query":"  "}""")]
	[InlineData("search.global", """{"query":"x","projectSlug":"../up"}""")]
	[InlineData("import.list", """{}""")]
	[InlineData("import.list", """{"global":true}""")]
	[InlineData("import.titles", """{"externalIds":["../evil"]}""")]
	[InlineData("import.titles", """{}""")]
	[InlineData("import.run", """{"targetId":"x","items":"nope"}""")]
	[InlineData("schedules.add", """{"nodeId":"n","prompt":"p"}""")]
	[InlineData("schedules.add", """{"nodeId":"n","prompt":"p","kind":"weekly"}""")]
	[InlineData("schedules.remove", """{}""")]
	[InlineData("attention.listAll", """{"status":"closed"}""")]
	[InlineData("attention.listAll", """{"kinds":["urgent"]}""")]
	[InlineData("attention.listAll", """{"afterId":"nobody/A1"}""")]
	public async Task BadParameters_AreInvalidParams_WithoutAPath(string method, string paramsJson)
	{
		var response = await _env.CallAsync(method, JsonNode.Parse(paramsJson));

		Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
		AssertNoPath(response["error"]!["message"]!.GetValue<string>());
	}

	[Fact]
	public async Task UnknownNodes_AreDaemonErrors_WithoutAPath()
	{
		var calls = new (string, JsonNode)[]
		{
			("import.list", Args(("directoryId", "nope"))),
			("import.run", Args(("targetId", "nope"), ("items", new JsonArray()))),
			("schedules.add", Args(("nodeId", "nope"), ("prompt", "p"), ("kind", "at"), ("fireAtUtc", FireAt.ToString("O")))),
		};

		foreach (var (method, args) in calls)
		{
			var response = await _env.CallAsync(method, args);

			Assert.Equal(-32000, response["error"]!["code"]!.GetValue<int>());
			AssertNoPath(response["error"]!["message"]!.GetValue<string>());
		}
	}

	[Fact]
	public async Task NoResult_CarriesALocalPath_OrAWorkingDirectory()
	{
		_env.AddClaudeSession("claude-a", slug: "C--Projects-Other");
		_env.Import.Index.SearchResult = [new SessionSearchResultModel { ExternalId = "claude-a", ProjectSlug = "C--Projects-Other", Content = "x", Role = "user" }];
		_env.Attention.Service.Post(_env.Session, [AttentionTestEnv.Req("Question")]);
		await _env.CallAsync("schedules.add", Args(("nodeId", _env.Session.NodeId), ("prompt", "p"), ("kind", "at"), ("fireAtUtc", FireAt.ToString("O"))));

		var calls = new (string, JsonNode)[]
		{
			("search.global", Args(("query", "x"))), ("search.reindex", Args()),
			("import.list", Args(("directoryId", _env.Directory.NodeId))), ("import.list", Args(("global", true), ("query", "x"))),
			("import.titles", Args(("externalIds", new JsonArray("claude-a")))),
			("import.run", Args(("targetId", _env.Directory.NodeId), ("items", new JsonArray(new JsonObject { ["externalId"] = "claude-a" })))),
			("schedules.list", Args()), ("attention.listAll", Args()), ("daemon.health", Args()),
		};

		foreach (var (method, args) in calls)
		{
			var text = (await _env.CallAsync(method, args))["result"]!.ToJsonString();

			AssertNoPath(text);
		}
	}

	private void AssertNoPath(string text)
	{
		var escapedRoot = JsonSerializer.Serialize(_env.Root)[1..^1];
		Assert.DoesNotContain(_env.Root, text);
		Assert.DoesNotContain(escapedRoot, text);
		Assert.DoesNotMatch(@"[A-Za-z]:[\\/]", text);                       // a drive path, whatever the folder
		Assert.DoesNotContain("workingDirectory", text, StringComparison.OrdinalIgnoreCase);
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────

	private static JsonObject Args(params (string Key, object? Value)[] values)
	{
		var result = new JsonObject();
		foreach (var (key, value) in values)
			result[key] = value switch { null => null, JsonNode node => node, string s => JsonValue.Create(s), bool b => JsonValue.Create(b), int i => JsonValue.Create(i), _ => throw new ArgumentException(key) };
		return result;
	}

	private async Task<JsonNode> ResultAsync(string method, JsonNode args)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = args.DeepClone() }.ToJsonString();
		var response = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Loopback, CancellationToken.None);

		Assert.Null(response!["error"]);
		return response["result"]!;
	}

	private void AssertGolden(string name, JsonNode result)
	{
		var actual = Normalize(result);
		var path   = GoldenPath(name);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd(), actual.Replace("\r\n", "\n").TrimEnd());
	}

	/// <summary>Numbers the GUIDs and the schedule ids by first appearance, replaces the run id and the two process counters of the health, and checks that the temp root never shows.</summary>
	private string Normalize(JsonNode result)
	{
		var node = result.DeepClone();
		MaskNextCronFireTimes(node);
		foreach (var key in new[] { "workingSetMb", "gen2Count", "runId" })
		{
			if (node is JsonObject obj && obj.ContainsKey(key))
				obj[key] = key == "runId" ? "<run>" : 0;
		}

		var json = node.ToJsonString();
		Assert.DoesNotContain(JsonSerializer.Serialize(_env.Root)[1..^1], json);
		json = json.Replace(Constants.ClaudeSessions.BuildProjectSlug(_env.Root), "<rootslug>");   // the slug of the temp root names the user and the machine

		var ids = new System.Collections.Generic.Dictionary<string, string>();
		json = Regex.Replace(json, "[0-9a-f]{32}|(?<=\"id\":\")[0-9a-f]{8}(?=\")|(?<=\"scheduleId\":\")[0-9a-f]{8}(?=\")", m =>
		{
			if (!ids.TryGetValue(m.Value, out var placeholder))
				ids[m.Value] = placeholder = "<id" + (ids.Count + 1) + ">";
			return placeholder;
		});
		return JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	/// <summary>The first fire time of a cron schedule is the next match of the real clock, so a golden file cannot hold it; the test checks that it is at 09:00 in the future instead.</summary>
	private static void MaskNextCronFireTimes(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				if (obj["kind"]?.GetValue<string>() == "cron" && obj["fireAtUtc"] != null)
				{
					var next = obj["fireAtUtc"]!.GetValue<DateTimeOffset>();
					Assert.True(next > DateTimeOffset.UtcNow && next.Hour == 9 && next.Minute == 0, "the next cron fire is the next 09:00");
					obj["fireAtUtc"] = "<next cron time>";
				}

				foreach (var child in obj.Select(p => p.Value).ToList())
					MaskNextCronFireTimes(child);
				break;
			case JsonArray array:
				foreach (var child in array)
					MaskNextCronFireTimes(child);
				break;
		}
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
