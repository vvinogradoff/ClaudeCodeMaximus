using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Tests.Codex.Fakes;

namespace TessynDesktop.Tests.Codex;

/// <summary><see cref="CodexAppServerHost"/> against the in-process fake app-server (FR.33.6–FR.33.8, FR.33.19).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexAppServerHostTests
{
	private readonly Eventually _eventually = new();

	// TC.CX.07
	[Fact]
	public async Task StoredProfile_LaunchesAppServer_WithCodexHome_AndTheFileCredentialStore()
	{
		await using var f = new CodexHostFixture();
		await f.Host.EnsureStartedAsync();

		var spec = Assert.Single(f.Launcher.Launches);
		Assert.Equal(@"C:\Codex\codex-x86_64-pc-windows-msvc.exe", spec.ExecutablePath);
		Assert.Equal(["app-server", "-c", "cli_auth_credentials_store=\"file\""], spec.Arguments);
		Assert.Equal(CodexHostFixture.StoredHome, spec.CodexHome);
		Assert.Equal(CodexHostFixture.StoredHome, spec.WorkingDirectory);
	}

	// TC.CX.07
	[Fact]
	public async Task CodexDefault_LaunchesAppServer_WithoutCodexHome_OrOverrides()
	{
		await using var f = new CodexHostFixture("codex-default", codexHome: null);
		await f.Host.EnsureStartedAsync();

		var spec = Assert.Single(f.Launcher.Launches);
		Assert.Equal(["app-server"], spec.Arguments);
		Assert.Null(spec.CodexHome);
	}

	// TC.CX.07
	[Fact]
	public async Task Handshake_SendsInitialize_AsTessynDesktop_WithoutExperimentalApi_ThenInitialized()
	{
		await using var f = new CodexHostFixture();
		await f.Host.EnsureStartedAsync();
		await f.Server.WaitForAsync("initialized");

		var messages = f.Server.Received.ToList();
		Assert.Equal("initialize", messages[0].Method);
		var clientInfo = messages[0].Params.GetProperty("clientInfo");
		Assert.Equal("tessyn_desktop", clientInfo.GetProperty("name").GetString());
		Assert.Equal("Tessyn", clientInfo.GetProperty("title").GetString());
		Assert.Equal("1.2.3", clientInfo.GetProperty("version").GetString());
		Assert.False(messages[0].Params.GetProperty("capabilities").GetProperty("experimentalApi").GetBoolean());

		Assert.Equal("initialized", messages[1].Method);
		Assert.Null(messages[1].Id);
		Assert.Equal(CodexHostState.Running, f.Host.State);
		Assert.Equal(1, f.Host.Generation);
		Assert.Equal(CodexHostFixture.StoredHome, f.Host.ServerInfo!.CodexHome);
	}

	// TC.CX.08
	[Fact]
	public async Task OneHost_SharesOneProcess_ForEveryCall()
	{
		await using var f = new CodexHostFixture();
		await f.Host.ReadAccountAsync();
		var first  = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });
		var second = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Beta" });

		Assert.Single(f.Launcher.Servers);
		Assert.NotEqual(first.Thread.Id, second.Thread.Id);
		Assert.True(f.Host.IsThreadLoaded(first.Thread.Id));
		Assert.True(f.Host.IsThreadLoaded(second.Thread.Id));
	}

	// TC.CX.08
	[Fact]
	public async Task IdleShutdown_StopsTheHost_After15Minutes_WithNoLoadedThread()
	{
		await using var f = new CodexHostFixture();
		await f.Host.ReadAccountAsync();
		Assert.True(f.Host.IsIdleShutdownArmed);

		f.Clock.Advance(TimeSpan.FromMinutes(14));
		Assert.Equal(CodexHostState.Running, f.Host.State);

		f.Clock.Advance(TimeSpan.FromMinutes(1));
		await _eventually.TrueAsync(() => f.Host.State == CodexHostState.Stopped, "the idle host stopped");
		Assert.True(f.Server.Process.StdinClosedByHost);
		Assert.Empty(f.Crashes);

		await f.Host.ReadAccountAsync();
		Assert.Equal(2, f.Launcher.Servers.Count);
		Assert.Equal(2, f.Host.Generation);
	}

	// TC.CX.08
	[Fact]
	public async Task IdleShutdown_WaitsWhileAThreadIsLoaded_AndStartsAfterItIsUnsubscribed()
	{
		await using var f = new CodexHostFixture();
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });
		Assert.False(f.Host.IsIdleShutdownArmed);

		f.Clock.Advance(TimeSpan.FromHours(2));
		Assert.Equal(CodexHostState.Running, f.Host.State);

		Assert.Equal("unsubscribed", await f.Host.UnsubscribeThreadAsync(thread.Thread.Id));
		Assert.False(f.Host.IsThreadLoaded(thread.Thread.Id));
		Assert.True(f.Host.IsIdleShutdownArmed);

		f.Clock.Advance(TimeSpan.FromMinutes(15));
		await _eventually.TrueAsync(() => f.Host.State == CodexHostState.Stopped, "the idle host stopped");
	}

	// TC.CX.08
	[Fact]
	public async Task IdleShutdown_WaitsWhileATurnIsInProgress()
	{
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => s.Scripts.StreamingThenHang(t.ThreadId, t.TurnId, "Working"));
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });
		var turn   = await f.Host.StartTurnAsync(new CodexTurnStartParams { ThreadId = thread.Thread.Id, Input = [CodexUserInput.FromText("hi")] });
		await f.WaitForNotificationAsync(n => n.Method == "turn/started");

		// The thread is loaded and the turn active: unloading the thread alone does not make the host idle.
		await f.Host.UnsubscribeThreadAsync(thread.Thread.Id);
		Assert.False(f.Host.IsIdleShutdownArmed);

		await f.Host.InterruptTurnAsync(thread.Thread.Id, turn.Turn.Id);
		await f.WaitForNotificationAsync(n => n.Method == "turn/completed");
		await _eventually.TrueAsync(() => f.Host.IsIdleShutdownArmed, "the idle timer armed after the turn ended");
	}

	// TC.CX.09
	[Fact]
	public async Task ResumeResult_OfFiftyMegabytes_IsReadWithoutError()
	{
		var huge = new string('y', 50 * 1024 * 1024);
		await using var f = new CodexHostFixture(configure: s => s.DecorateResumeResult = r => r["thread"]!["preview"] = huge);

		var resumed = await f.Host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = "01a20229-aaaa", ExcludeTurns = true });

		Assert.Equal(huge.Length, resumed.Thread.Preview!.Length);
		Assert.StartsWith(@"\\?\", resumed.Thread.Path);
	}

	// TC.CX.10 (the wire shape the host sends; the values come from the engine in phase 2b)
	[Fact]
	public async Task StartThread_SendsTheThreadParams_AndConfig_AsGiven()
	{
		await using var f = new CodexHostFixture();
		var config = new JsonObject
		{
			["mcp_servers.cmx.url"]                         = "http://127.0.0.1:5555/mcp",
			["mcp_servers.cmx.http_headers"]                = new JsonObject { ["X-CMX-Token"] = "token-A" },
			["mcp_servers.cmx.default_tools_approval_mode"] = "approve",
			["project_doc_fallback_filenames"]              = new JsonArray("CLAUDE.md"),
			["features.apps"]                               = false,
		};

		await f.Host.StartThreadAsync(new CodexThreadStartParams
		{
			Cwd            = @"C:\Proj\Alpha",
			Model          = "gpt-x-sol",
			ApprovalPolicy = Constants.Codex.Values.ApprovalPolicyNever,
			Sandbox        = Constants.Codex.Values.SandboxDangerFullAccess,
			ServiceName    = Constants.Codex.Values.ServiceName,
			ThreadSource   = Constants.Codex.Values.ThreadSource,
			Config         = config,
		});

		var p = Assert.Single(f.Server.RequestsOf("thread/start")).Params;
		Assert.Equal(@"C:\Proj\Alpha", p.GetProperty("cwd").GetString());
		Assert.Equal("gpt-x-sol", p.GetProperty("model").GetString());
		Assert.Equal("never", p.GetProperty("approvalPolicy").GetString());
		Assert.Equal("danger-full-access", p.GetProperty("sandbox").GetString());
		Assert.Equal("tessyn", p.GetProperty("serviceName").GetString());
		Assert.Equal("tessyn", p.GetProperty("threadSource").GetString());
		Assert.False(p.TryGetProperty("developerInstructions", out _));
		var sentConfig = p.GetProperty("config");
		Assert.Equal("token-A", sentConfig.GetProperty("mcp_servers.cmx.http_headers").GetProperty("X-CMX-Token").GetString());
		Assert.False(sentConfig.GetProperty("features.apps").GetBoolean());
		Assert.Equal("CLAUDE.md", sentConfig.GetProperty("project_doc_fallback_filenames")[0].GetString());
	}

	// TC.CX.12
	[Fact]
	public async Task StartTurn_SendsModelEffortSummaryAndClientMessageId_AndOmitsADefaultEffort()
	{
		await using var f = new CodexHostFixture();
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });

		await f.Host.StartTurnAsync(new CodexTurnStartParams
		{
			ThreadId = thread.Thread.Id, Input = [CodexUserInput.FromText("one"), CodexUserInput.FromLocalImage(@"C:\img\a.png")],
			Model = "gpt-x-sol", Effort = "high", Summary = Constants.Codex.Values.SummaryConcise, ClientUserMessageId = "entry-1",
		});
		await f.Host.StartTurnAsync(new CodexTurnStartParams { ThreadId = thread.Thread.Id, Input = [CodexUserInput.FromText("two")], Model = "gpt-x-sol" });

		var turns = f.Server.RequestsOf("turn/start");
		var first = turns[0].Params;
		Assert.Equal("gpt-x-sol", first.GetProperty("model").GetString());
		Assert.Equal("high", first.GetProperty("effort").GetString());
		Assert.Equal("concise", first.GetProperty("summary").GetString());
		Assert.Equal("entry-1", first.GetProperty("clientUserMessageId").GetString());
		Assert.Equal("text", first.GetProperty("input")[0].GetProperty("type").GetString());
		Assert.Equal("one", first.GetProperty("input")[0].GetProperty("text").GetString());
		Assert.Equal("localImage", first.GetProperty("input")[1].GetProperty("type").GetString());
		Assert.Equal(@"C:\img\a.png", first.GetProperty("input")[1].GetProperty("path").GetString());
		Assert.False(turns[1].Params.TryGetProperty("effort", out _));
	}

	// TC.CX.12
	[Fact]
	public async Task ResumeThread_SendsExcludeTurns_AndMarksTheThreadLoaded()
	{
		await using var f = new CodexHostFixture();
		await f.Host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = "01a20229-bbbb", ExcludeTurns = true, Config = new JsonObject { ["features.apps"] = false } });

		var p = Assert.Single(f.Server.RequestsOf("thread/resume")).Params;
		Assert.True(p.GetProperty("excludeTurns").GetBoolean());
		Assert.True(f.Host.IsThreadLoaded("01a20229-bbbb"));
	}

	// TC.CX.13
	[Fact]
	public async Task ResumeOfAMissingThread_IsThreadGone()
	{
		await using var f = new CodexHostFixture(configure: s => s.GoneThreads["01a20000-0000"] = 0);
		var error = await Assert.ThrowsAsync<CodexThreadException>(() =>
			f.Host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = "01a20000-0000", ExcludeTurns = true }));

		Assert.Equal(CodexThreadErrorKind.ThreadGone, error.Kind);
		Assert.Equal("01a20000-0000", error.ThreadId);
		Assert.False(f.Host.IsThreadLoaded("01a20000-0000"));
	}

	// TC.CX.13
	[Fact]
	public async Task ResumeOfAThreadOpenElsewhere_IsActiveWriter()
	{
		await using var f = new CodexHostFixture(configure: s => s.ActiveWriterThreads["01a20229-cccc"] = 0);
		var error = await Assert.ThrowsAsync<CodexThreadException>(() =>
			f.Host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = "01a20229-cccc", ExcludeTurns = true }));

		Assert.Equal(CodexThreadErrorKind.ActiveWriter, error.Kind);
	}

	// TC.CX.13
	[Fact]
	public async Task TurnOnAThreadNotLoaded_IsNotLoaded_AndWorksAfterAResume()
	{
		await using var f = new CodexHostFixture();
		var request = new CodexTurnStartParams { ThreadId = "01a20229-dddd", Input = [CodexUserInput.FromText("hi")] };

		var error = await Assert.ThrowsAsync<CodexThreadException>(() => f.Host.StartTurnAsync(request));
		Assert.Equal(CodexThreadErrorKind.NotLoaded, error.Kind);

		await f.Host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = "01a20229-dddd", ExcludeTurns = true });
		var turn = await f.Host.StartTurnAsync(request);
		Assert.Equal("inProgress", turn.Turn.Status);
	}

	[Fact]
	public async Task OtherRpcErrors_StayCodexRpcExceptions()
	{
		await using var f = new CodexHostFixture(configure: s => s.On("thread/start", c => c.ErrorAsync(-32600, "failed to load configuration: unknown variant `bogus`")));
		var error = await Assert.ThrowsAsync<CodexRpcException>(() => f.Host.StartThreadAsync(new CodexThreadStartParams()));

		Assert.IsNotType<CodexThreadException>(error);
		Assert.Equal(-32600, error.Code);
	}

	// TC.CX.18
	[Fact]
	public async Task ServerRequests_AreAllAnswered_AndTheTurnCompletes()
	{
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => s.Scripts.ServerRequestTurn(t.ThreadId, t.TurnId));
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });
		await f.Host.StartTurnAsync(new CodexTurnStartParams { ThreadId = thread.Thread.Id, Input = [CodexUserInput.FromText("go")] });

		await f.WaitForNotificationAsync(n => n.Method == "turn/completed");
		var answers = f.Server.ServerRequestAnswers.ToList();
		Assert.Equal(6, answers.Count);

		Assert.Equal("accept", answers[0].GetProperty("result").GetProperty("decision").GetString());
		Assert.Equal("accept", answers[1].GetProperty("result").GetProperty("decision").GetString());
		var permissions = answers[2].GetProperty("result");
		Assert.Equal("turn", permissions.GetProperty("scope").GetString());
		Assert.True(permissions.GetProperty("permissions").GetProperty("network").GetProperty("enabled").GetBoolean());
		Assert.Equal("decline", answers[3].GetProperty("result").GetProperty("action").GetString());
		Assert.Equal(-32601, answers[4].GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal("Tessyn does not support item/tool/requestUserInput", answers[4].GetProperty("error").GetProperty("message").GetString());
		Assert.Equal(-32601, answers[5].GetProperty("error").GetProperty("code").GetInt32());
	}

	// TC.CX.19
	[Fact]
	public async Task CrashMidTurn_RaisesCrashed_WithExitCodeAndStderrTail_AndTheNextCallStartsANewProcess()
	{
		var stderr = Enumerable.Range(1, 60).Select(i => $"stderr line {i}").ToArray();
		await using var f = new CodexHostFixture(configure: s => s.TurnScript = t => s.Scripts.CrashMidTurn(t.ThreadId, t.TurnId, 3, stderr));
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });
		await f.Host.StartTurnAsync(new CodexTurnStartParams { ThreadId = thread.Thread.Id, Input = [CodexUserInput.FromText("go")] });

		await _eventually.TrueAsync(() => !f.Crashes.IsEmpty, "the crash was reported");
		var crash = Assert.Single(f.Crashes);
		Assert.Equal(3, crash.ExitCode);
		Assert.Equal("codex_1", crash.ProfileId);
		Assert.Equal(50, crash.StderrTail.Count);
		Assert.Equal("stderr line 60", crash.StderrTail[^1]);
		Assert.Equal("stderr line 11", crash.StderrTail[0]);
		Assert.Equal(CodexHostState.Stopped, f.Host.State);
		Assert.False(f.Host.IsThreadLoaded(thread.Thread.Id));
		Assert.Same(crash, f.Host.LastCrash);

		await f.Host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = thread.Thread.Id, ExcludeTurns = true });
		Assert.Equal(2, f.Launcher.Servers.Count);
		Assert.Equal(2, f.Host.Generation);
	}

	// TC.CX.19
	[Fact]
	public async Task Crash_FailsAWaitingCall_WithCodexHostCrashedException()
	{
		await using var f = new CodexHostFixture(configure: s => s.On("account/read", _ => Task.Run(() => s.Crash(7, "fatal: boom"))));

		var error = await Assert.ThrowsAsync<CodexHostCrashedException>(() => f.Host.ReadAccountAsync());

		Assert.Equal(7, error.Crash.ExitCode);
		Assert.Equal(["fatal: boom"], error.Crash.StderrTail);
	}

	[Fact]
	public async Task HungRequest_FailsWithTimeout_AfterTheRequestTimeout()
	{
		await using var f = new CodexHostFixture(configure: s => s.On("account/read", _ => Task.CompletedTask));
		var call = f.Host.ReadAccountAsync();
		await f.Server.WaitForAsync("account/read");

		f.Clock.Advance(TimeSpan.FromSeconds(59));
		Assert.False(call.IsCompleted);

		f.Clock.Advance(TimeSpan.FromSeconds(1));
		await Assert.ThrowsAsync<TimeoutException>(() => call);
		Assert.Equal(CodexHostState.Running, f.Host.State);
	}

	[Fact]
	public async Task StopAsync_ClosesStdin_AndIsNotACrash()
	{
		await using var f = new CodexHostFixture();
		await f.Host.EnsureStartedAsync();

		await f.Host.StopAsync();

		Assert.Equal(CodexHostState.Stopped, f.Host.State);
		Assert.True(f.Server.Process.StdinClosedByHost);
		Assert.False(f.Server.Process.Killed);
		await Task.Delay(50);
		Assert.Empty(f.Crashes);
	}

	[Fact]
	public async Task TerminateAsync_KillsTheProcess()
	{
		await using var f = new CodexHostFixture();
		await f.Host.EnsureStartedAsync();

		await f.Host.TerminateAsync();

		Assert.True(f.Server.Process.Killed);
		Assert.Equal(CodexHostState.Stopped, f.Host.State);
		await Task.Delay(50);
		Assert.Empty(f.Crashes);
	}

	[Fact]
	public async Task ReadAccount_SignedIn_And_SignedOut()
	{
		await using var f = new CodexHostFixture();
		var account = await f.Host.ReadAccountAsync();
		Assert.True(account.IsSignedIn);
		Assert.Equal("u@example.com", account.Account!.Email);
		Assert.Equal("plus", account.Account.PlanType);

		f.Server.SignedIn = false;
		var signedOut = await f.Host.ReadAccountAsync();
		Assert.False(signedOut.IsSignedIn);
		Assert.True(signedOut.RequiresOpenaiAuth);
	}

	[Fact]
	public async Task ReadRateLimits_ReturnsTheCodexBucket_WithWindowDurations()
	{
		await using var f = new CodexHostFixture();
		var limits = await f.Host.ReadRateLimitsAsync();

		var codex = limits.RateLimitsByLimitId!["codex"];
		Assert.Equal(300, codex.Primary!.WindowDurationMins);
		Assert.Equal(10080, codex.Secondary!.WindowDurationMins);
		Assert.Equal(19, codex.Secondary.UsedPercent);
		Assert.Equal(1795609786, codex.Secondary.ResetsAt);
		Assert.False(Assert.Single(f.Server.RequestsOf("account/rateLimits/read")).Raw.TryGetProperty("params", out _));
	}

	[Fact]
	public async Task ListModels_ReadsEveryPage_WithoutHiddenOnes()
	{
		await using var f = new CodexHostFixture(configure: s =>
			s.ModelPages = [
				new JsonArray(s.Scripts.Model("gpt-x-sol", "Sol", true, "low", "low", "high")),
				new JsonArray(s.Scripts.Model("gpt-x-luna", "Luna", false, "medium", "low", "medium")),
			]);

		var models = await f.Host.ListModelsAsync();

		Assert.Equal(["gpt-x-sol", "gpt-x-luna"], models.Select(m => m.Id));
		Assert.True(models[0].IsDefault);
		Assert.Equal(["low", "high"], models[0].SupportedReasoningEfforts!.Select(e => e.ReasoningEffort));
		var requests = f.Server.RequestsOf("model/list");
		Assert.Equal(2, requests.Count);
		Assert.False(requests[0].Params.GetProperty("includeHidden").GetBoolean());
		Assert.Equal("page-1", requests[1].Params.GetProperty("cursor").GetString());
	}

	[Fact]
	public async Task Login_ChatGpt_ReturnsAnAuthUrl_AndDeviceCode_ReturnsTheCode()
	{
		await using var f = new CodexHostFixture();
		var browser = await f.Host.LoginStartAsync();
		Assert.Equal("chatgpt", browser.Type);
		Assert.StartsWith("https://auth.openai.com/", browser.AuthUrl);

		var device = await f.Host.LoginStartAsync(useDeviceCode: true);
		Assert.Equal("ABCD-1234", device.UserCode);
		Assert.Equal("https://auth.openai.com/codex/device", device.VerificationUrl);

		Assert.Equal("canceled", await f.Host.CancelLoginAsync(device.LoginId!));
	}

	[Fact]
	public async Task Logout_IsFollowedByASignOutNotification()
	{
		await using var f = new CodexHostFixture();
		await f.Host.LogoutAsync();

		var updated = await f.WaitForNotificationAsync(n => n.Method == "account/updated");
		var payload = updated.Params.Deserialize<CodexAccountUpdatedNotification>(Constants.Codex.SerializerOptions)!;
		Assert.True(payload.IsSignedOut);
	}

	[Fact]
	public async Task SetThreadName_AndCompact_AreSent()
	{
		await using var f = new CodexHostFixture();
		var thread = await f.Host.StartThreadAsync(new CodexThreadStartParams { Cwd = @"C:\Proj\Alpha" });

		await f.Host.SetThreadNameAsync(thread.Thread.Id, "My session");
		var renamed = await f.WaitForNotificationAsync(n => n.Method == "thread/name/updated");
		Assert.Equal("My session", renamed.Params.GetProperty("threadName").GetString());

		await f.Host.CompactThreadAsync(thread.Thread.Id);
		await f.WaitForNotificationAsync(n => n.Method == "turn/completed");
		Assert.Single(f.Server.RequestsOf("thread/compact/start"));
	}
}
