using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.ElevatedRuns;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// TC.ER.45 and TC.ER.48 to TC.ER.50: the wire of elevated runs (tdcp-spec 10.A13) with golden files: <c>elevated.*</c> and its error texts, the <c>action</c> of
/// <c>attention.list</c> in its local and relay variants, the loopback-only rule and the <c>elevatedRuns</c> capability.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunContractTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();
	private readonly RemoteEventBus _bus = new();
	private readonly EventLog _log;
	private readonly TdcpDispatcher _dispatcher;
	private int _id;

	public ElevatedRunContractTests()
	{
		_log = new EventLog(_bus);
		_dispatcher = new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: new DaemonRunInfo(), eventLog: _log, elevatedRuns: _env.Service),
			new ElevatedRunRpcModule(_env.Service),
			new AttentionRpcModule(_env.Registry, _env.Attention, null!, null!, null!, null, _env.Service),
		]);
	}

	public void Dispose()
	{
		_env.Dispose();
		_log.Dispose();
		_bus.Dispose();
	}

	private async Task<JsonNode> CallAsync(string method, string? paramsJson = null, TdcpTransport transport = TdcpTransport.Loopback)
	{
		var body = $$"""{"jsonrpc":"2.0","id":{{++_id}},"method":"{{method}}"{{(paramsJson == null ? "" : ",\"params\":" + paramsJson)}}}""";
		return (await _dispatcher.HandleTextAsync(body, transport, CancellationToken.None))!;
	}

	private static string Q(string text) => JsonSerializer.Serialize(text);

	private async Task<ElevatedRunModel> RequestAsync(SessionNodeModel? caller = null, string command = "echo hi") =>
		await _env.RequestAsync(caller ?? _env.Lead, command);

	private static void AssertError(JsonNode response, int code, string? message = null)
	{
		Assert.NotNull(response["error"]);
		Assert.Equal(code, response["error"]!["code"]!.GetValue<int>());
		if (message != null)
			Assert.Equal(message, response["error"]!["message"]!.GetValue<string>());
	}

	// ── TC.ER.48: the methods ─────────────────────────────────────────────────

	[Fact]
	public async Task List_HasTheGoldenShape_NewestFirstAndFilterable_TC_ER_48()
	{
		var run = await RequestAsync();
		await RequestAsync(_env.Other, "echo other");

		var all    = await CallAsync("elevated.list", """{"nodeId":""" + Q(_env.Lead.NodeId) + "}");
		var active = await CallAsync("elevated.list");

		AssertGolden("elevated.list", all["result"]!, run);
		Assert.Equal(2, active["result"]!["runs"]!.AsArray().Count);
		Assert.Equal(1, all["result"]!["runs"]!.AsArray().Count);
	}

	[Fact]
	public async Task List_StatusAllIncludesFinishedRuns_AnythingElseIsInvalid_TC_ER_48()
	{
		var run = await RequestAsync();
		_env.Service.Decline(run.Id, null);

		var active = await CallAsync("elevated.list");
		var all    = await CallAsync("elevated.list", """{"status":"all"}""");
		var bad    = await CallAsync("elevated.list", """{"status":"x"}""");

		Assert.Empty(active["result"]!["runs"]!.AsArray());
		Assert.Single(all["result"]!["runs"]!.AsArray());
		AssertError(bad, RemoteProtocol.ErrorCodes.InvalidParams);
	}

	[Fact]
	public async Task Get_WithAndWithoutATail_HasTheGoldenShapes_TC_ER_48()
	{
		var run = await RequestAsync();
		_env.MakeRunning(run.Id, 70);
		File.WriteAllText(Path.Combine(_env.RunDir(run.Id), "output.log"), "one\r\ntwo\r\nthree\r\n");

		var plain = await CallAsync("elevated.get", $$"""{"requestId":{{Q(run.Id)}}}""");
		var tail  = await CallAsync("elevated.get", $$"""{"requestId":{{Q(run.Id)}},"tailLines":2}""");

		AssertGolden("elevated.get", plain["result"]!, run);
		AssertGolden("elevated.get.tail", tail["result"]!, run);
		Assert.Null(plain["result"]!["outputTail"]);
		Assert.Equal("two\nthree", tail["result"]!["outputTail"]!.GetValue<string>());
	}

	[Fact]
	public async Task Get_UnknownId_AndATailOutOfRange_AreInvalidParams_TC_ER_48()
	{
		var run = await RequestAsync();

		AssertError(await CallAsync("elevated.get", """{"requestId":"ffffffff"}"""), RemoteProtocol.ErrorCodes.InvalidParams, "Unknown elevated run request.");
		AssertError(await CallAsync("elevated.get", $$"""{"requestId":{{Q(run.Id)}},"tailLines":201}"""), RemoteProtocol.ErrorCodes.InvalidParams);
		AssertError(await CallAsync("elevated.get", $$"""{"requestId":{{Q(run.Id)}},"tailLines":-1}"""), RemoteProtocol.ErrorCodes.InvalidParams);
	}

	[Fact]
	public async Task Approve_AnswersTheLaunchDescription_AndOnlyTheFirstCallWins_TC_ER_48()
	{
		var run = await RequestAsync();

		var first  = await CallAsync("elevated.approve", $$"""{"requestId":{{Q(run.Id)}}}""");
		var second = await CallAsync("elevated.approve", $$"""{"requestId":{{Q(run.Id)}}}""");

		AssertGolden("elevated.approve", first["result"]!, run);
		AssertError(second, RemoteProtocol.ErrorCodes.DaemonError, "This request is no longer pending (launching).");
		Assert.Equal(_env.RunDir(run.Id), first["result"]!["launch"]!["runDir"]!.GetValue<string>());
		Assert.Equal("runas", first["result"]!["launch"]!["verb"]!.GetValue<string>());
		Assert.Equal(60, first["result"]!["launch"]!["startTimeoutSeconds"]!.GetValue<int>());
	}

	[Fact]
	public async Task Approve_UnknownAndExpired_HaveTheirCodesAndTexts_TC_ER_48()
	{
		var run = await RequestAsync();
		_env.Clock.Advance(TimeSpan.FromHours(24));

		AssertError(await CallAsync("elevated.approve", """{"requestId":"ffffffff"}"""), RemoteProtocol.ErrorCodes.InvalidParams);
		AssertError(await CallAsync("elevated.approve", $$"""{"requestId":{{Q(run.Id)}}}"""), RemoteProtocol.ErrorCodes.DaemonError, "This request expired.");
		AssertError(await CallAsync("elevated.approve"), RemoteProtocol.ErrorCodes.InvalidParams);
	}

	[Fact]
	public async Task LaunchedLaunchFailedDeclineAndStop_HaveTheGoldenEmptyResults_AndTheirErrors_TC_ER_48()
	{
		var launched = await RequestAsync(command: "echo 1");
		var failed   = await RequestAsync(command: "echo 2");
		var declined = await RequestAsync(command: "echo 3");

		var approve1 = await CallAsync("elevated.approve", $$"""{"requestId":{{Q(launched.Id)}}}""");
		var ok1 = await CallAsync("elevated.launched", $$"""{"requestId":{{Q(launched.Id)}}}""");
		await CallAsync("elevated.approve", $$"""{"requestId":{{Q(failed.Id)}}}""");
		var ok2 = await CallAsync("elevated.launchFailed", $$"""{"requestId":{{Q(failed.Id)}},"declined":false,"reason":"boom"}""");
		var ok3 = await CallAsync("elevated.decline", $$"""{"requestId":{{Q(declined.Id)}},"note":"no"}""");
		_env.WriteStarted(launched.Id, 71);
		_env.Service.Poll();
		var ok4 = await CallAsync("elevated.stop", $$"""{"requestId":{{Q(launched.Id)}}}""");

		Assert.NotNull(approve1["result"]);
		AssertGolden("elevated.launched", ok1["result"]!, launched);
		AssertGolden("elevated.launchFailed", ok2["result"]!, failed);
		AssertGolden("elevated.decline", ok3["result"]!, declined);
		AssertGolden("elevated.stop", ok4["result"]!, launched);
		Assert.True(File.Exists(Path.Combine(_env.RunDir(launched.Id), "stop")));
		Assert.Equal("boom", _env.Service.Find(failed.Id)!.Failure);
		Assert.Equal("no", _env.Service.Find(declined.Id)!.Note);

		AssertError(await CallAsync("elevated.launched", $$"""{"requestId":{{Q(declined.Id)}}}"""), RemoteProtocol.ErrorCodes.DaemonError, "This request is not launching (declined).");
		AssertError(await CallAsync("elevated.decline", $$"""{"requestId":{{Q(launched.Id)}}}"""), RemoteProtocol.ErrorCodes.DaemonError, "This request is no longer pending (running).");
		AssertError(await CallAsync("elevated.stop", $$"""{"requestId":{{Q(declined.Id)}}}"""), RemoteProtocol.ErrorCodes.DaemonError, "This request is not running (declined).");
		AssertError(await CallAsync("elevated.launchFailed", $$"""{"requestId":{{Q(failed.Id)}},"declined":true}"""), RemoteProtocol.ErrorCodes.DaemonError);
		AssertError(await CallAsync("elevated.launchFailed", $$"""{"requestId":{{Q(failed.Id)}}}"""), RemoteProtocol.ErrorCodes.InvalidParams);
	}

	[Fact]
	public async Task ElevatedChanged_IsPublishedAfterEveryChange_AndNeverOverTheRelay_TC_ER_48()
	{
		var daemon = FakeDaemonProxy.Create(out _);
		var received = new System.Collections.Generic.List<RemoteNotification>();
		_bus.Published += n => { lock (received) received.Add(n); };
		var files = new RecordingSessionFileService();
		using var epochs = new SessionEpochTracker(files);
		var publisher = new RemoteNotificationPublisher(daemon, files, _bus, new TreeVersionTracker(), epochs, new FakeStateBuilder(), elevatedRuns: _env.Service);
		publisher.Start();

		var run = await RequestAsync();
		_env.Service.Approve(run.Id);
		_env.Service.ReportLaunchFailed(run.Id, declined: true, null);
		await WaitForAsync(() => received.Count(n => n.Method == "elevated.changed") >= 3);
		publisher.Stop();

		var events = received.Where(n => n.Method == "elevated.changed").Select(n => n.Params!.Deserialize<ElevatedChangedPayload>(TdcpJsonOptions.Default)!).ToList();
		Assert.Equal(
			[ElevatedRunStatus.Pending, ElevatedRunStatus.Launching, ElevatedRunStatus.DeclinedAtPrompt],
			events.Select(e => e.Status));
		Assert.All(events, e => Assert.Equal((run.Id, _env.Lead.NodeId), (e.RequestId, e.NodeId)));
		Assert.All(received.Where(n => n.Method == "elevated.changed"), n => Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(n)));
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var until = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < until)
			await Task.Delay(20);
	}

	// ── TC.ER.49: attention.list ──────────────────────────────────────────────

	[Fact]
	public async Task AttentionList_LocalVariantCarriesTheActionWithPaths_TC_ER_49()
	{
		var run = await RequestAsync(command: "echo `ticks` and \"quotes\"");

		var result = await CallAsync("attention.list", $$"""{"nodeId":{{Q(_env.Lead.NodeId)}}}""");

		AssertGolden("attention.list.elevated.local", result["result"]!, run);
		var item = result["result"]!["items"]![0]!;
		Assert.Equal("ask", item["kind"]!.GetValue<string>());
		Assert.Equal("elevatedRun", item["action"]!["type"]!.GetValue<string>());
		Assert.Equal(run.Command, item["action"]!["run"]!["command"]!.GetValue<string>());
		Assert.Equal(_env.Alpha, item["action"]!["run"]!["workingDir"]!.GetValue<string>());
		Assert.NotNull(item["action"]!["run"]!["logPath"]);
		Assert.Null(item["action"]!["run"]!["workingDirName"]);
	}

	[Fact]
	public async Task AttentionList_RelayVariantHasTheFolderNameAndNoPaths_TC_ER_49()
	{
		var run = await RequestAsync(command: "echo `ticks` and \"quotes\"");

		var result = await CallAsync("attention.list", $$"""{"nodeId":{{Q(_env.Lead.NodeId)}}}""", TdcpTransport.Relay);

		AssertGolden("attention.list.elevated.relay", result["result"]!, run);
		var action = result["result"]!["items"]![0]!["action"]!["run"]!;
		Assert.Null(action["workingDir"]);
		Assert.Null(action["logPath"]);
		Assert.Equal("Alpha", action["workingDirName"]!.GetValue<string>());
		Assert.Equal(run.Command, action["command"]!.GetValue<string>());
		Assert.DoesNotContain(_env.Root, result["result"]!.ToJsonString().Replace("\\\\", "\\"));
	}

	[Fact]
	public async Task AttentionList_AnOrdinaryItemHasNoAction_TC_ER_49()
	{
		_env.Attention.Post(_env.Lead, [new AttentionPostRequestModel { Kind = AttentionKind.Todo, Title = "plain" }]);

		var result = await CallAsync("attention.list", $$"""{"nodeId":{{Q(_env.Lead.NodeId)}}}""");

		Assert.Null(result["result"]!["items"]![0]!["action"]);
	}

	// ── TC.ER.45: reply and dismiss are refused ───────────────────────────────

	[Fact]
	public async Task ReplyAndDismiss_OnTheCard_AreInvalidParams_TC_ER_45()
	{
		var run = await RequestAsync();
		var id = _env.Attention.GetItems(_env.Lead.NodeId).Single().Id;

		var reply   = await CallAsync("attention.reply", $$"""{"nodeId":{{Q(_env.Lead.NodeId)}},"answers":[{"itemId":{{Q(id.ToLowerInvariant())}},"text":"yes"}]}""");
		var dismiss = await CallAsync("attention.dismiss", $$"""{"nodeId":{{Q(_env.Lead.NodeId)}},"itemId":{{Q(id)}}}""");

		AssertError(reply, RemoteProtocol.ErrorCodes.InvalidParams, "This item is an elevated-run request: use Run as administrator or Decline.");
		AssertError(dismiss, RemoteProtocol.ErrorCodes.InvalidParams, "This item is an elevated-run request: use Run as administrator or Decline.");
		Assert.Equal(AttentionStatus.Open, _env.Attention.GetItems(_env.Lead.NodeId).Single().Status);
		Assert.Equal(ElevatedRunStatus.Pending, _env.Service.Find(run.Id)!.Status);
	}

	// ── TC.ER.50: loopback only ───────────────────────────────────────────────

	[Theory]
	[InlineData("elevated.list")]
	[InlineData("elevated.get")]
	[InlineData("elevated.approve")]
	[InlineData("elevated.launched")]
	[InlineData("elevated.launchFailed")]
	[InlineData("elevated.decline")]
	[InlineData("elevated.stop")]
	public async Task EveryElevatedMethod_IsMethodNotFoundOverTheRelay_TC_ER_50(string method)
	{
		var run = await RequestAsync();

		var response = await CallAsync(method, $$"""{"requestId":{{Q(run.Id)}},"declined":true}""", TdcpTransport.Relay);

		AssertError(response, RemoteProtocol.ErrorCodes.MethodNotFound);
		Assert.Equal(ElevatedRunStatus.Pending, _env.Service.Find(run.Id)!.Status);
	}

	[Fact]
	public async Task Capability_IsInTheLoopbackHelloOnly_TC_ER_50()
	{
		var loopback = await CallAsync("client.hello", null, TdcpTransport.Loopback);
		var relay    = await CallAsync("client.hello", null, TdcpTransport.Relay);

		Assert.Contains("elevatedRuns", loopback["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()));
		Assert.DoesNotContain("elevatedRuns", relay["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()));
	}

	[Fact]
	public async Task Capability_IsAbsentWhenTheServiceIsNotWired_TC_ER_50()
	{
		var dispatcher = new TdcpDispatcher([new ClientRpcModule(runInfo: new DaemonRunInfo(), eventLog: _log)]);

		var response = await dispatcher.HandleTextAsync("""{"jsonrpc":"2.0","id":1,"method":"client.hello"}""", TdcpTransport.Loopback, CancellationToken.None);

		Assert.DoesNotContain("elevatedRuns", response!["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()));
	}

	// ── golden files ──────────────────────────────────────────────────────────

	/// <summary>Replaces ids, paths, timestamps and the encoded launch line so the snapshot is the same on every run and machine.</summary>
	private string Normalize(JsonNode result, ElevatedRunModel run)
	{
		var json = result.ToJsonString();
		json = json.Replace(run.Id, "<run>");
		json = json.Replace(_env.Lead.NodeId, "<lead>").Replace(_env.Wrk.NodeId, "<wrk>").Replace(_env.Other.NodeId, "<other>");
		json = json.Replace(_env.Lead.NodeId[..8], "<lead8>").Replace(_env.Wrk.NodeId[..8], "<wrk8>").Replace(_env.Other.NodeId[..8], "<other8>");
		json = json.Replace(JsonSerializer.Serialize(_env.Root)[1..^1], "<root>");
		json = Regex.Replace(json, "<root>[^\"]*", m => m.Value.Replace("\\\\", "/"));
		json = Regex.Replace(json, "\"(\\d{4}-\\d\\d-\\d\\dT[^\"]*)\"", "\"<ts>\"");
		json = Regex.Replace(json, "(-EncodedCommand )[A-Za-z0-9+/=]+", "$1<base64>");
		return JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private void AssertGolden(string name, JsonNode result, ElevatedRunModel run)
	{
		var actual = Normalize(result, run).Replace("\r\n", "\n");
		var path   = GoldenPath(name);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd(), actual.TrimEnd());
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
