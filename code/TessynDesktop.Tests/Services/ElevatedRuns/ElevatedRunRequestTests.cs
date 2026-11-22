using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>TC.ER.01 to TC.ER.16 and TC.ER.38: <c>request_elevated_run</c>, its checks, its limits and the fixed record (FR.15.16, FR.15.18).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunRequestTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private Task<JsonNode> Tool(SessionNodeModel caller, JsonObject args) =>
		_env.Server.HandleToolCallAsync(new JsonObject { ["name"] = "request_elevated_run", ["arguments"] = args }, caller);

	// ── TC.ER.01 ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task Request_HappyPath_StoresTheRecordAndSaysPending_TC_ER_01()
	{
		var result = await Tool(_env.Lead, new JsonObject { ["command"] = "echo hi", ["workingDir"] = _env.Alpha, ["reason"] = "r" });

		var text = TextOf(result);
		var match = Regex.Match(text, @"^Elevated run (?<id>[0-9a-f]{8}) requested \(pending\)\. ");
		Assert.True(match.Success, text);
		Assert.EndsWith("end your turn instead of waiting.", text);

		var id = match.Groups["id"].Value;
		var stored = JsonNode.Parse(File.ReadAllText(Path.Combine(_env.RunDir(id), "request.json")))!;
		Assert.Equal("echo hi", stored["Command"]!.GetValue<string>());
		Assert.Equal(_env.Alpha, stored["WorkingDir"]!.GetValue<string>());
		Assert.Equal("r", stored["Reason"]!.GetValue<string>());
		Assert.Equal(60, stored["TimeoutMinutes"]!.GetValue<int>());
		Assert.Equal("Pending", stored["Status"]!.GetValue<string>());
		Assert.Equal(stored["CreatedUtc"]!.GetValue<DateTimeOffset>() + TimeSpan.FromHours(24), stored["ExpiresUtc"]!.GetValue<DateTimeOffset>());
	}

	// ── TC.ER.02, 03, 04 ──────────────────────────────────────────────────────

	[Theory]
	[InlineData("")]
	[InlineData("     ")]
	[InlineData("a\nb")]
	[InlineData("a\rb")]
	[InlineData("a\0b")]
	public async Task Request_BadCommand_IsRefusedAndNothingIsCreated_TC_ER_02(string command)
	{
		var error = await _env.RefusedAsync(_env.Lead, _env.Req(command));

		Assert.Equal("The command must be one non-empty line of at most 4,000 characters.", error);
		Assert.Empty(_env.RunFolders());
	}

	[Fact]
	public async Task Request_CommandLength_4000IsAccepted_4001IsNot_TC_ER_02()
	{
		var error = await _env.RefusedAsync(_env.Lead, _env.Req(new string('x', 4001)));
		var ok    = await _env.RequestAsync(command: new string('x', 4000));

		Assert.Equal(Constants.ElevatedRuns.CommandInvalidText, error);
		Assert.Equal(4000, ok.Command.Length);
		Assert.Single(_env.RunFolders());
	}

	[Fact]
	public async Task Request_Reason_IsOneLineCutTo300_AndTheCommandIsKeptExactly_TC_ER_03()
	{
		var blank = await _env.RefusedAsync(_env.Lead, _env.Req(reason: "  \r\n "));
		var multi = await _env.RequestAsync(command: "  echo  hi  ", reason: "line one\r\nline two\nline three");
		var cut   = await _env.RequestAsync(command: "echo 2", reason: new string('r', 301));

		Assert.Equal(Constants.ElevatedRuns.ReasonRequiredText, blank);
		Assert.Equal("line one line two line three", multi.Reason);
		Assert.Equal("  echo  hi  ", multi.Command);
		Assert.Equal(300, cut.Reason.Length);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(240)]
	[InlineData(90)]
	public async Task Request_TimeoutInRange_IsStoredAsGiven_TC_ER_04(int minutes)
	{
		var outcome = await _env.Service.RequestAsync(_env.Lead, _env.Req(timeout: JsonValue.Create(minutes)));

		Assert.Equal(minutes, outcome.Run.TimeoutMinutes);
	}

	[Fact]
	public async Task Request_TimeoutOmitted_Is60_TC_ER_04() =>
		Assert.Equal(60, (await _env.RequestAsync()).TimeoutMinutes);

	[Theory]
	[InlineData("0")]
	[InlineData("241")]
	[InlineData("-5")]
	[InlineData("1.5")]
	[InlineData("\"abc\"")]
	public async Task Request_TimeoutOutOfRangeOrNotWhole_IsAnErrorNeverClamped_TC_ER_04(string json)
	{
		var error = await _env.RefusedAsync(_env.Lead, _env.Req(timeout: JsonNode.Parse(json)));

		Assert.Equal("timeoutMinutes must be a whole number from 1 to 240.", error);
		Assert.Empty(_env.RunFolders());
	}

	// ── TC.ER.05 to 10: the working directory ─────────────────────────────────

	[Fact]
	public async Task WorkingDir_OwnDirectoryAndBelow_AreAccepted_TC_ER_05()
	{
		var own   = await _env.RequestAsync(workingDir: _env.Alpha);
		var below = await _env.RequestAsync(command: "echo 2", workingDir: Path.Combine(_env.Alpha, "src"));

		Assert.Equal(_env.Alpha, own.WorkingDir);
		Assert.Equal(Path.Combine(_env.Alpha, "src"), below.WorkingDir);
	}

	[Fact]
	public async Task WorkingDir_RegisteredProject_IsSeparatorAwareAndCaseInsensitive_TC_ER_06()
	{
		var beta    = await _env.RequestAsync(workingDir: _env.Beta);
		var betaSub = await _env.RequestAsync(command: "echo 2", workingDir: Path.Combine(_env.Beta, "sub"));
		var sloppy  = await _env.RequestAsync(command: "echo 3", workingDir: _env.Alpha.ToLowerInvariant() + @"\");
		var error   = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: Path.Combine(_env.Proj, "Alpha2")));

		Assert.Equal(_env.Beta, beta.WorkingDir);
		Assert.Equal(Path.Combine(_env.Beta, "sub"), betaSub.WorkingDir);
		Assert.Equal(_env.Alpha.ToLowerInvariant(), sloppy.WorkingDir);
		Assert.StartsWith($"'{Path.Combine(_env.Proj, "Alpha2")}' is not allowed for an elevated run.", error);
	}

	[Fact]
	public async Task WorkingDir_SiblingWorktree_IsAcceptedForLeadAndWrk_TC_ER_07()
	{
		var worktree = Path.Combine(_env.Proj, "Alpha-wt-fix");
		var lead     = await _env.RequestAsync(_env.Lead, workingDir: worktree);
		var leadSub  = await _env.RequestAsync(_env.Lead, "echo 2", Path.Combine(worktree, "src"));
		var wrk      = await _env.RequestAsync(_env.Wrk, "echo 3", worktree);
		var upper    = await _env.RequestAsync(_env.Lead, "echo 4", Path.Combine(_env.Proj, "ALPHA-WT-FIX"));

		Assert.All(new[] { lead, leadSub, wrk, upper }, run => Assert.True(Directory.Exists(run.WorkingDir)));
	}

	[Theory]
	[InlineData(@"Other")]
	[InlineData(@"Alpha-wt")]
	[InlineData(@"Alphawt-x")]
	[InlineData(@"Elsewhere\Alpha-wt-x")]
	public async Task WorkingDir_NotAllowed_ListsTheRegisteredProjects_TC_ER_08(string relativeToProj)
	{
		var dir   = Path.Combine(_env.Proj, relativeToProj);
		var error = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: dir));

		Assert.StartsWith($"'{dir}' is not allowed for an elevated run. Use the session's working directory, a folder in a project of the Tessyn tree, or a sibling worktree named '<project>-wt-*'. Registered: ", error);
		Assert.Contains(_env.Alpha, error);
		Assert.Contains(_env.Beta, error);
	}

	[Fact]
	public async Task WorkingDir_WorktreeOfAProjectThatIsNotRegistered_IsRefused_TC_ER_08()
	{
		var stranger = Path.Combine(_env.Proj, "Gamma-wt-x");
		Directory.CreateDirectory(stranger);

		var error = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: stranger));

		Assert.Contains("is not allowed for an elevated run", error);
	}

	[Fact]
	public async Task WorkingDir_ListOfRegisteredProjects_ShowsTheFirst12AndTheRest_TC_ER_08()
	{
		for (var i = 0; i < 13; i++)
			_env.Registry.AddDirectory(Path.Combine(_env.Root, "extra" + i));

		var error = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: Path.Combine(_env.Proj, "Other")));

		Assert.EndsWith("(+3 more).", error);
	}

	[Fact]
	public async Task WorkingDir_Relative_IsResolvedAgainstTheCaller_AndMissingOrFile_Refused_TC_ER_09()
	{
		var relative = await _env.RequestAsync(workingDir: "src");
		var missing  = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: "nope"));
		var file     = Path.Combine(_env.Alpha, "a.txt");
		File.WriteAllText(file, "x");
		var isFile = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: file));

		Assert.Equal(Path.Combine(_env.Alpha, "src"), relative.WorkingDir);
		Assert.Equal($"'{Path.Combine(_env.Alpha, "nope")}' does not exist.", missing);
		Assert.Equal($"'{file}' does not exist.", isFile);
	}

	[Fact]
	public async Task WorkingDir_JunctionOutsideAndDotDotEscape_AreRefused_TC_ER_10()
	{
		var viaLink = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: Path.Combine(_env.Alpha, "link")));
		var escape  = await _env.RefusedAsync(_env.Lead, _env.Req(workingDir: Path.Combine(_env.Alpha, "src", "..", "..", "Other")));

		Assert.Contains("is not allowed for an elevated run", viaLink);
		Assert.Contains("is not allowed for an elevated run", escape);
	}

	// ── TC.ER.11 dedupe ───────────────────────────────────────────────────────

	[Fact]
	public async Task Request_Dedupe_SamePendingRequestOfTheSameSession_ReturnsTheSameId_TC_ER_11()
	{
		var first = await Tool(_env.Lead, new JsonObject { ["command"] = "echo hi", ["workingDir"] = _env.Alpha, ["reason"] = "r" });
		var id    = Regex.Match(TextOf(first), "Elevated run ([0-9a-f]{8})").Groups[1].Value;

		var again = TextOf(await Tool(_env.Lead, new JsonObject { ["command"] = "echo hi", ["workingDir"] = _env.Alpha, ["reason"] = "other words" }));

		Assert.Equal($"Elevated run {id} is already pending with this command and working directory - not requested again.", again);
		Assert.Single(_env.RunFolders());
	}

	[Fact]
	public async Task Request_Dedupe_DifferentCommandOtherSessionOrFinished_CreateNewRequests_TC_ER_11()
	{
		var first     = await _env.RequestAsync();
		var different = await _env.RequestAsync(command: "echo different");
		var other     = await _env.RequestAsync(_env.Other, workingDir: _env.Beta);
		var sameForOther = await _env.RequestAsync(_env.Other, workingDir: _env.Beta);

		_env.Service.Decline(first.Id, null);
		var afterFinal = await _env.RequestAsync();

		Assert.NotEqual(first.Id, different.Id);
		Assert.Equal(other.Id, sameForOther.Id);
		Assert.NotEqual(first.Id, afterFinal.Id);
	}

	[Fact]
	public async Task Request_Dedupe_SameCommandFromAnotherSession_IsANewRequest_TC_ER_11()
	{
		var lead = await _env.RequestAsync(_env.Lead, workingDir: _env.Alpha);
		var wrk  = await _env.RequestAsync(_env.Wrk, workingDir: _env.Alpha);

		Assert.NotEqual(lead.Id, wrk.Id);
	}

	// ── TC.ER.12, 13 limits ───────────────────────────────────────────────────

	[Fact]
	public async Task Limit_ThreeOpenPerSession_FourthRefusedAndNotCounted_TC_ER_12()
	{
		var first = await _env.RequestAsync(command: "echo 1");
		await _env.RequestAsync(command: "echo 2");
		await _env.RequestAsync(command: "echo 3");

		var error = await _env.RefusedAsync(_env.Lead, _env.Req("echo 4"));
		Assert.Equal("This session already has 3 elevated runs waiting for the user. Wait for them or withdraw one with cancel_elevated_run.", error);
		Assert.Equal(3, _env.RunFolders().Count());

		_env.Service.Decline(first.Id, null);
		var accepted = await _env.RequestAsync(command: "echo 4");
		Assert.Equal("echo 4", accepted.Command);
	}

	[Fact]
	public async Task Limit_ARunningRequestDoesNotCountAgainstTheThree_TC_ER_12()
	{
		var first = await _env.RequestAsync(command: "echo 1");
		await _env.RequestAsync(command: "echo 2");
		await _env.RequestAsync(command: "echo 3");

		_env.MakeRunning(first.Id);

		var fourth = await _env.RequestAsync(command: "echo 4");
		Assert.Equal("echo 4", fourth.Command);
	}

	[Fact]
	public async Task Limit_TenOpenAcrossTheApp_EleventhRefused_TC_ER_13()
	{
		var sessions = new[] { _env.Lead, _env.Other, _env.Wrk, _env.Registry.AddSession(_env.Registry.AddDirectory(Path.Combine(_env.Root, "d4")), "S4") };
		sessions[3].WorkingDirectory = _env.Beta;
		var made = 0;
		foreach (var session in sessions)
		{
			var dir = session == _env.Other || session == sessions[3] ? _env.Beta : _env.Alpha;
			for (var i = 0; i < 3 && made < 10; i++, made++)
				await _env.RequestAsync(session, $"echo {session.Name}{i}", dir);
		}

		var error = await _env.RefusedAsync(sessions[3], _env.Req("echo late", _env.Beta));

		Assert.Equal(10, made);
		Assert.Equal("Too many elevated runs are waiting for the user (10). Try again later.", error);
	}

	// ── TC.ER.14, 15, 16, 38 ──────────────────────────────────────────────────

	[Fact]
	public async Task Request_WhenTheDaemonRunsElevated_IsRefusedWithNoCard_TC_ER_14()
	{
		using var env = new ElevatedRunTestEnv(isElevated: true);

		var result = await Assert.ThrowsAsync<McpToolException>(() => env.Server.HandleToolCallAsync(
			new JsonObject { ["name"] = "request_elevated_run", ["arguments"] = new JsonObject { ["command"] = "echo hi", ["workingDir"] = env.Alpha, ["reason"] = "r" } }, env.Lead));

		Assert.Equal("The daemon already runs as administrator, so this session's commands do too. Run the command directly.", result.Message);
		Assert.Empty(env.RunFolders());
		Assert.Empty(env.Attention.GetItems(env.Lead.NodeId));
	}

	[Fact]
	public async Task Request_FromAWorker_IsAllowed_TopLevelIsTheEndOfTheChain_TC_ER_15()
	{
		var deep = _env.Registry.AddSession(_env.Registry.AddDirectory(Path.Combine(_env.Root, "deepdir")), "Deep");
		deep.WorkingDirectory    = Path.Combine(_env.Proj, "Alpha-wt-fix");
		deep.SupervisorNodeId    = _env.Wrk.NodeId;
		deep.OrchestrationDepth  = 2;

		var wrk      = await _env.RequestAsync(_env.Wrk, workingDir: _env.Wrk.WorkingDirectory);
		var deepRun  = await _env.RequestAsync(deep, workingDir: deep.WorkingDirectory);
		var leadRun  = await _env.RequestAsync(_env.Lead);

		Assert.Equal(_env.Wrk.NodeId, wrk.RequesterNodeId);
		Assert.Equal(_env.Lead.NodeId, wrk.TopLevelNodeId);
		Assert.Equal(_env.Lead.NodeId, deepRun.TopLevelNodeId);
		Assert.Null(leadRun.TopLevelNodeId);
	}

	[Fact]
	public async Task PostAttention_ByAWorker_IsStillRefused_TC_ER_15()
	{
		var result = await _env.Server.HandleToolCallAsync(new JsonObject
		{
			["name"] = "post_attention",
			["arguments"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["kind"] = "ask", ["title"] = "t" }) },
		}, _env.Wrk);

		Assert.Equal(Constants.Attention.WorkerRefusalText, TextOf(result));
	}

	[Fact]
	public async Task LaunchLine_For4000CharsOfAwkwardText_StaysUnder30000_TC_ER_16()
	{
		var awkward = string.Concat(Enumerable.Repeat("a'b\"c%d^e&fü中é ", 400))[..4000];
		var run     = await _env.RequestAsync(command: awkward);

		var launch = _env.Service.Approve(run.Id);

		Assert.True(launch.FileName.Length + 1 + launch.Arguments.Length < 30_000, launch.Arguments.Length.ToString());
	}

	[Fact]
	public async Task Launch_IsBuiltFromTheStoredRecordByteForByte_AndLaterChangesDoNotChangeIt_TC_ER_38()
	{
		var command = "echo \"q\" & echo %PATH% ^ ü";
		var run     = await _env.RequestAsync(command: command, workingDir: Path.Combine(_env.Alpha, "src"));

		// request.json is rewritten behind the daemon's back, and the agent calls again: neither can change what runs.
		File.WriteAllText(Path.Combine(_env.RunDir(run.Id), "request.json"), File.ReadAllText(Path.Combine(_env.RunDir(run.Id), "request.json")).Replace("echo", "evil"));
		var again = await _env.RequestAsync(command: "echo different");
		var launch = _env.Service.Approve(run.Id);

		var script = Encoding.Unicode.GetString(Convert.FromBase64String(launch.Arguments[Constants.ElevatedRuns.PowerShellArgumentsPrefix.Length..]));
		Assert.Equal(command, Embedded(script, "cmd"));
		Assert.Equal(Path.Combine(_env.Alpha, "src"), Embedded(script, "dir"));
		Assert.Equal(_env.RunDir(run.Id), Embedded(script, "run"));
		Assert.Contains("$limit = 3600\n", script.Replace("\r\n", "\n"));
		Assert.Equal("powershell.exe", launch.FileName);
		Assert.Equal("runas", launch.Verb);
		Assert.Equal(Path.Combine(_env.Alpha, "src"), launch.WorkingDirectory);
		Assert.Equal(60, launch.StartTimeoutSeconds);
		Assert.NotEqual(run.Id, again.Id);
		Assert.DoesNotContain("evil", script);
	}

	/// <summary>The value of a <c>$name = @'</c> here-string of the wrapper.</summary>
	private static string Embedded(string script, string name)
	{
		var match = Regex.Match(script.Replace("\r\n", "\n"), @"\$" + name + @" = @'\n(?<v>.*?)\n'@", RegexOptions.Singleline);
		Assert.True(match.Success, name);
		return match.Groups["v"].Value;
	}
}
