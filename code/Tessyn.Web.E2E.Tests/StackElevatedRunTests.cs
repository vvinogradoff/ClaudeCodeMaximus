using System.Text.Json.Nodes;
using Tessyn.Web.E2E.Tests.Harness;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests;

/// <summary>
/// TC.ER.62 through the real web server, relay connector and Core: what the browser can see of an elevated-run request (FR.23.31). The Web client's Focus tab renders exactly
/// this read-only card, so the test fixes the data it is given: the whole command, the folder NAME only and no path, the requester's list_sessions short id, the status and
/// the outcome after the run ends, and that no <c>elevated.*</c> method (the Run and Decline of the PC) is reachable from the browser.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class StackElevatedRunTests
{
	private static JsonNode P(string json) => JsonNode.Parse(json)!;

	private static string LongCommand => "powershell -NoProfile -Command \"Get-ChildItem 'C:\\Windows' | Where-Object { $_.Name -like '*<b>x</b>*' }\"  `code` & echo %PATH% " + new string('x', 400);

	private static async Task<JsonNode> CardAsync(WebStack stack, HttpClient browser, string nodeId, string runId)
	{
		var list = await stack.RpcAsync(browser, "attention.list", P($$"""{"nodeId":"{{nodeId}}"}"""));
		return list["items"]!.AsArray().Single(i => i!["action"]?["run"]?["id"]?.GetValue<string>() == runId)!;
	}

	[Fact]
	public async Task ER62_TheCardIsReadOnlyOnTheWeb_WholeCommand_FolderNameOnly_AndTheOutcomeAtTheEnd()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;
		var service = stack.Daemon.Get<IElevatedRunService>();
		var lead = stack.Daemon.Registry.FindModelByNodeId(seed.SupervisorId)!;

		var outcome = await service.RequestAsync(lead, new ElevatedRunRequestModel { Command = LongCommand, WorkingDir = seed.AlphaPath, Reason = "Run the suite as admin" });
		var runId = outcome.Run.Id;

		// Pending: the facts, the whole command, the folder name; no path anywhere in the answer.
		var item = await CardAsync(stack, browser, seed.SupervisorId, runId);
		var run = item["action"]!["run"]!;
		Assert.Equal("elevatedRun", item["action"]!["type"]!.GetValue<string>());
		Assert.Equal("ask", item["kind"]!.GetValue<string>());
		Assert.Equal("open", item["status"]!.GetValue<string>());
		Assert.Equal("pending", run["status"]!.GetValue<string>());
		Assert.Equal("Run the suite as admin", run["reason"]!.GetValue<string>());
		Assert.Equal(LongCommand, run["command"]!.GetValue<string>());
		Assert.Equal(SeedModel.AlphaLabel, run["workingDirName"]!.GetValue<string>());
		Assert.Null(run["workingDir"]);
		Assert.Null(run["logPath"]);
		Assert.DoesNotContain(seed.AlphaPath, item.ToJsonString(), StringComparison.OrdinalIgnoreCase);
		Assert.Contains(SeedModel.AlphaLabel, item["body"]!.GetValue<string>());

		// The requester is named by the Short Node Id that list_sessions shows.
		var expectedShortId = new SessionListIndex(stack.Daemon.Registry.SnapshotSessionsForListing()).GetShortId(seed.SupervisorId);
		Assert.Equal(SeedModel.SupervisorName, run["requester"]!["name"]!.GetValue<string>());
		Assert.Equal(expectedShortId, run["requester"]!["shortId"]!.GetValue<string>());
		Assert.Equal(seed.SupervisorId, run["requester"]!["nodeId"]!.GetValue<string>());

		// Nothing that approves, declines or stops is reachable from the browser, and the item cannot be answered or dismissed there.
		foreach (var method in new[] { "elevated.approve", "elevated.decline", "elevated.stop", "elevated.get", "elevated.list", "elevated.launched", "elevated.launchFailed" })
			Assert.Equal(-32601, WebStack.ErrorCode(await stack.RpcRawAsync(browser, method, P($$"""{"requestId":"{{runId}}"}"""))));

		var itemId = item["id"]!.GetValue<string>();
		var reply = await stack.RpcRawAsync(browser, "attention.reply", P($$"""{"nodeId":"{{seed.SupervisorId}}","answers":[{"itemId":"{{itemId}}","text":"yes"}]}"""));
		var dismiss = await stack.RpcRawAsync(browser, "attention.dismiss", P($$"""{"nodeId":"{{seed.SupervisorId}}","itemId":"{{itemId}}"}"""));
		Assert.Equal(-32602, WebStack.ErrorCode(reply));
		Assert.Equal(-32602, WebStack.ErrorCode(dismiss));
		Assert.Equal("pending", (await CardAsync(stack, browser, seed.SupervisorId, runId))["action"]!["run"]!["status"]!.GetValue<string>());

		// The PC's side moves the run on; the browser sees the status and then the outcome (the card is closed by the daemon).
		service.Approve(runId);
		Assert.Equal("launching", (await CardAsync(stack, browser, seed.SupervisorId, runId))["action"]!["run"]!["status"]!.GetValue<string>());
		service.ReportLaunchFailed(runId, declined: true, reason: null);

		var closed = await CardAsync(stack, browser, seed.SupervisorId, runId);
		Assert.Equal("resolved", closed["status"]!.GetValue<string>());
		Assert.Equal("declinedAtPrompt", closed["action"]!["run"]!["status"]!.GetValue<string>());
		Assert.StartsWith($"Elevated run {runId}: ", closed["resolutionNote"]!.GetValue<string>());
		Assert.Equal(LongCommand, closed["action"]!["run"]!["command"]!.GetValue<string>());
		Assert.Null(closed["action"]!["run"]!["workingDir"]);
	}

	[Fact]
	public async Task ER62_ADeclinedRequestShowsItsOutcome_AndAWorkersRequestNamesTheWorkerOnTheSupervisorCard()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;
		var service = stack.Daemon.Get<IElevatedRunService>();
		var worker = stack.Daemon.Registry.FindModelByNodeId(seed.WorkerOneId)!;
		lock (stack.Daemon.Settings.SyncRoot)
			worker.OrchestrationDepth = 1;

		var run = (await service.RequestAsync(worker, new ElevatedRunRequestModel { Command = "cmd /c echo hi", WorkingDir = seed.AlphaPath, Reason = "worker needs admin" })).Run;

		var onSupervisor = await CardAsync(stack, browser, seed.SupervisorId, run.Id);
		var onWorker = await CardAsync(stack, browser, seed.WorkerOneId, run.Id);
		Assert.Equal(seed.WorkerOneId, onSupervisor["action"]!["run"]!["requester"]!["nodeId"]!.GetValue<string>());   // the web card compares it to the shown session to write "Requested by worker"
		Assert.Equal(seed.WorkerOneId, onWorker["action"]!["run"]!["requester"]!["nodeId"]!.GetValue<string>());

		service.Decline(run.Id, "not now");

		foreach (var nodeId in new[] { seed.SupervisorId, seed.WorkerOneId })
		{
			var closed = await CardAsync(stack, browser, nodeId, run.Id);
			Assert.Equal("resolved", closed["status"]!.GetValue<string>());
			Assert.Equal("declined", closed["action"]!["run"]!["status"]!.GetValue<string>());
			Assert.Equal("not now", closed["action"]!["run"]!["note"]!.GetValue<string>());
		}
	}
}
