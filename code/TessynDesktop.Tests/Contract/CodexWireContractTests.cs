using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// tdcp-spec 10.A14 (FR.33.2, FR.33.3, FR.33.5, FR.33.10, FR.33.17) on the wire: <c>client.hello</c>'s engines, <c>catalog.get</c> with GPT models,
/// a Codex <c>SessionStateDto</c>, Codex usage, the Codex members of <c>settings.general</c>, the ChatGPT profile methods (success, cancel, failure,
/// device code, sign-out, delete) and their loopback-only registration. Golden result shapes run through the in-process dispatcher and the real
/// loopback server. Everything that reaches Codex goes to the in-process fake app-server.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexWireContractTests : IAsyncLifetime
{
	private static readonly string[] LoopbackOnlyMethods = ["profiles.codexLogin", "profiles.codexLoginCancel", "profiles.codexLogout"];

	private readonly CodexWireEnv _env = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _env.DisposeAsync();

	// ── client.hello ─────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Hello_lists_the_engines_on_both_transports_and_the_ChatGPT_admin_capability_on_loopback_only()
	{
		var loopback = await _env.CallAsync("client.hello");
		var relay    = await _env.CallAsync("client.hello", transport: TdcpTransport.Relay);

		Assert.Equal(["claude", "codex"], Strings(loopback["engines"]!));
		Assert.Equal(["claude", "codex"], Strings(relay["engines"]!));
		Assert.Contains("engines", Strings(loopback["capabilities"]!));
		Assert.Contains("engines", Strings(relay["capabilities"]!));
		Assert.Contains("profiles.codex", Strings(loopback["capabilities"]!));
		Assert.DoesNotContain("profiles.codex", Strings(relay["capabilities"]!));
		Assert.Equal(1, loopback["protocolVersion"]!.GetValue<int>());
	}

	[Fact]
	public async Task Hello_lists_only_claude_when_Codex_was_not_found()
	{
		_env.Engines.Available = [Constants.SessionEngines.Claude];

		var result = await _env.CallAsync("client.hello");

		Assert.Equal(["claude"], Strings(result["engines"]!));
	}

	[Fact]
	public void The_real_availability_lists_Codex_only_when_the_locator_finds_it()
	{
		var engines = new ISessionEngine[] { new TessynDesktop.Tests.Services.Engines.FakeSessionEngine("claude"), new TessynDesktop.Tests.Services.Engines.FakeSessionEngine("codex") };
		var locator = new TessynDesktop.Codex.CodexExecutableLocator(_env.LocatorEnvironment);
		var found   = new SessionEngineAvailability(engines, _env.Settings, locator);

		var withCodex = found.GetAvailableEngines();
		_env.Settings.Settings.CodexPath = @"C:\Nowhere\codex.exe";
		var withoutCodex = found.GetAvailableEngines();

		Assert.Equal(["claude", "codex"], withCodex);
		Assert.Equal(["claude"], withoutCodex);
	}

	// ── catalog.get ──────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Catalog_with_GPT_models_matches_the_golden_file_on_the_relay_and_on_loopback_http()
	{
		await SignInCatalogProfileAsync();

		var relay = await _env.CallAsync("catalog.get", transport: TdcpTransport.Relay);
		var http  = await _env.Host.CallAsync("catalog.get");

		AssertGolden("catalog.get.gpt", relay);
		Assert.Null(http["error"]);
		AssertGolden("catalog.get.gpt", http["result"]!);
	}

	[Fact]
	public async Task Catalog_GPT_entries_carry_their_own_efforts_without_ultra_and_Claude_entries_keep_the_Claude_list()
	{
		await SignInCatalogProfileAsync();

		var models = (await _env.CallAsync("catalog.get"))["models"]!.AsArray();
		var luna   = models.Single(m => (string?)m!["id"] == "gpt-x-luna")!;
		var sol    = models.Single(m => (string?)m!["id"] == "gpt-x-sol")!;
		var opus   = models.Single(m => (string?)m!["id"] == FixedClaudeModelsOpus)!;

		Assert.Equal(["low", "medium"], luna["efforts"]!.AsArray().Select(e => (string)e!["id"]!));
		Assert.DoesNotContain("ultra", sol["efforts"]!.AsArray().Select(e => (string)e!["id"]!));
		Assert.Equal("low reasoning", (string?)luna["efforts"]![0]!["description"]);
		Assert.Equal(("codex", "openAI", true), ((string?)sol["engine"], (string?)sol["provider"], (bool?)sol["isDefault"]));
		Assert.False(luna.AsObject().ContainsKey("isDefault"));
		Assert.Equal(["max", "high", "medium", "low"], opus["efforts"]!.AsArray().Select(e => (string)e!["id"]!));
		Assert.False(opus["efforts"]![0]!.AsObject().ContainsKey("description"));
		Assert.Equal("claude", (string?)opus["engine"]);
	}

	[Fact]
	public async Task Catalog_without_GPT_models_sends_the_sign_in_hint_but_not_when_Codex_is_missing()
	{
		var signedOut = await _env.CallAsync("catalog.get");
		_env.Engines.Available = [Constants.SessionEngines.Claude];
		var noCodex = await _env.CallAsync("catalog.get");

		Assert.Equal("GPT models: sign in to ChatGPT in Settings > Profiles", (string?)signedOut["gptHint"]);
		Assert.DoesNotContain(signedOut["models"]!.AsArray(), m => (string?)m!["engine"] == "codex");
		Assert.False(noCodex.AsObject().ContainsKey("gptHint"));
		Assert.DoesNotContain(signedOut["profiles"]!.AsArray(), p => (string?)p!["engine"] == "codex"); // nothing signed in: no ChatGPT profile to pick
	}

	// ── SessionStateDto ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task A_Codex_session_state_matches_the_golden_file_with_its_engine_and_ChatGPT_pin()
	{
		_env.Codex.AddStoredProfile("codex_1", "u@example.com");
		var dir = _env.Registry.AddDirectory(Path.Combine(_env.Codex.Root, "proj"));
		var gpt = _env.Registry.AddSession(dir, "GPT session");
		var claude = _env.Registry.AddSession(dir, "Claude session");
		lock (_env.Settings.SyncRoot)
		{
			gpt.EngineId = Constants.SessionEngines.Codex;
			gpt.ProfileId = "codex_1";
			gpt.CodexThreadId = "thread-1";
		}

		var relay = await _env.CallAsync("tree.snapshot", transport: TdcpTransport.Relay);
		var sessions = relay["directories"]![0]!["sessions"]!.AsArray();
		var gptState = sessions.Single(s => (string?)s!["name"] == "GPT session")!["state"]!;
		var claudeState = sessions.Single(s => (string?)s!["name"] == "Claude session")!["state"]!;

		AssertGolden("sessionState.codex", Normalized(gptState, gpt.NodeId));
		Assert.Equal("claude", (string?)claudeState["engine"]);
		Assert.Equal("codex", _env.States.Build(gpt).Engine);
	}

	[Fact]
	public async Task A_deleted_ChatGPT_pin_is_named_missing_and_a_queued_switch_names_its_target()
	{
		_env.Codex.AddStoredProfile("codex_2", "b@example.com");
		var dir = _env.Registry.AddDirectory(Path.Combine(_env.Codex.Root, "proj"));
		var gpt = _env.Registry.AddSession(dir, "GPT session");
		lock (_env.Settings.SyncRoot)
		{
			gpt.EngineId = Constants.SessionEngines.Codex;
			gpt.ProfileId = "codex_9";
			gpt.PendingProfileId = "codex_2";
		}

		var state = _env.States.Build(gpt);

		Assert.Equal(("codex_9", "codex_9 (missing)", "ChatGPT · b@example.com"), (state.PinnedProfileId, state.PinnedProfileName, state.SwitchingToProfileName));
		await Task.CompletedTask;
	}

	// ── usage.get ────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Codex_usage_matches_the_golden_file_on_the_relay()
	{
		_env.Codex.AddStoredProfile("codex_1", "u@example.com");

		var relay = await _env.CallAsync("usage.get", """{"profileId":"codex_1"}""", TdcpTransport.Relay);

		AssertGolden("usage.get.codex", relay);
	}

	[Fact]
	public async Task An_exhausted_ChatGPT_plan_reports_its_reason_and_marks_the_exhausted_window()
	{
		_env.Codex.AddStoredProfile("codex_1", "u@example.com");
		_env.Codex.Launcher.Configure = server => server.On("account/rateLimits/read", call =>
		{
			var snapshot = server.Scripts.RateLimitSnapshot(primaryUsed: 100, secondaryUsed: 40, reached: "primary");
			return call.ReplyAsync(new JsonObject { ["rateLimits"] = snapshot.DeepClone(), ["rateLimitsByLimitId"] = new JsonObject { ["codex"] = snapshot } });
		});

		var result = await _env.CallAsync("usage.get", """{"profileId":"codex_1"}""");

		Assert.Equal(("codex", "primary"), ((string?)result["engine"], (string?)result["exhausted"]));
		Assert.True((bool?)result["fiveHour"]!["isExhausted"]);
		Assert.False(result["sevenDay"]!.AsObject().ContainsKey("isExhausted"));
	}

	[Fact]
	public async Task Claude_usage_windows_gain_their_labels_and_lengths_but_no_engine()
	{
		var resets = _env.Codex.Clock.GetUtcNow();
		_env.UsageFetcher.Result = new UsageFetchResultModel(UsageState.Ok,
			new ClaudeUsageData(20, resets.AddHours(2), "normal", 50, resets.AddDays(3), "normal"));

		var result = await _env.CallAsync("usage.get", "{}", TdcpTransport.Relay);

		Assert.Equal(("5h", 300), ((string?)result["fiveHour"]!["label"], (int?)result["fiveHour"]!["durationMinutes"]));
		Assert.Equal(("7d", 10080), ((string?)result["sevenDay"]!["label"], (int?)result["sevenDay"]!["durationMinutes"]));
		Assert.Equal(20, (double?)result["fiveHour"]!["utilization"]);
		Assert.False(result.AsObject().ContainsKey("engine"));
		Assert.False(result.AsObject().ContainsKey("exhausted"));
	}

	// ── settings.general ─────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task General_settings_with_the_Codex_members_match_the_golden_file()
	{
		_env.Codex.AddStoredProfile("codex_1", "u@example.com");
		_env.Settings.Settings.CodexCatalogProfileId = "codex_1";

		var inProcess = await _env.CallAsync("settings.get", """{"section":"general"}""");
		var http      = await _env.Host.CallAsync("settings.get", JsonNode.Parse("""{"section":"general"}"""));

		AssertGolden("settings.get.general.codex", inProcess);
		AssertGolden("settings.get.general.codex", http["result"]!);
	}

	[Fact]
	public async Task The_Codex_status_reports_a_missing_configured_path_and_never_bumps_the_section_version()
	{
		var before = await _env.CallAsync("settings.get", """{"section":"general"}""");
		_env.LocatorEnvironment.VersionOutputs[CodexWireEnv.CodexExe] = "codex-cli 0.172.0";
		var newer = await _env.CallAsync("settings.get", """{"section":"general"}""");
		await _env.CallAsync("settings.update", """{"section":"general","baseVersion":1,"patch":{"codexPath":"C:\\Nowhere\\codex.exe"}}""");
		var missing = await _env.CallAsync("settings.get", """{"section":"general"}""");

		Assert.Equal(1, (long)before["version"]!);
		Assert.Equal(1, (long)newer["version"]!); // the status changed, the version did not
		Assert.StartsWith("Codex 0.172.0 is newer than the tested", (string?)newer["values"]!["codexStatus"]!["note"]);
		Assert.False(before["values"]!["codexStatus"]!.AsObject().ContainsKey("note")); // the tested version has none
		Assert.Equal(2, (long)missing["version"]!);
		Assert.False((bool)missing["values"]!["codexStatus"]!["found"]!);
		Assert.False(missing["values"]!["codexStatus"]!.AsObject().ContainsKey("path"));
		Assert.Contains(@"C:\Nowhere\codex.exe", (string?)missing["values"]!["codexStatus"]!["statusLine"]);
	}

	[Fact]
	public async Task Settings_update_writes_the_Codex_path_catalog_profile_and_tiers_and_Reset_to_defaults_drops_the_table()
	{
		_env.Codex.AddStoredProfile("codex_2", "b@example.com");
		using var sse = await _env.Host.OpenStreamAsync();

		var updated = await _env.CallAsync("settings.update",
			"""{"section":"general","baseVersion":1,"patch":{"codexPath":"  D:\\Codex\\codex.exe ","codexCatalogProfileId":"CODEX_2","codexModelTiers":[{"match":" nova ","tier":4},{"match":"gpt","tier":1}]}}""");
		var changed = await CodexWireEnv.NextAsync(sse, "settings.changed");
		var values = (await _env.CallAsync("settings.get", """{"section":"general"}"""))["values"]!;

		Assert.Equal(2, (long)updated["version"]!);
		Assert.Equal(("general", 2L), ((string)changed.Params!["section"]!, (long)changed.Params["version"]!));
		Assert.Equal((@"D:\Codex\codex.exe", "codex_2"), (_env.Settings.Settings.CodexPath, _env.Settings.Settings.CodexCatalogProfileId));
		Assert.Equal([("nova", 4), ("gpt", 1)], _env.Settings.Settings.CodexModelTiers!.Select(r => (r.Match, r.Tier)));
		Assert.False((bool)values["codexModelTiersAreDefaults"]!);

		await _env.CallAsync("settings.update", """{"section":"general","baseVersion":2,"patch":{"resetCodexModelTiers":true,"codexCatalogProfileId":"","codexPath":""}}""");
		var reset = (await _env.CallAsync("settings.get", """{"section":"general"}"""))["values"]!;

		Assert.Null(_env.Settings.Settings.CodexModelTiers);
		Assert.Null(_env.Settings.Settings.CodexCatalogProfileId);
		Assert.Equal(string.Empty, _env.Settings.Settings.CodexPath);
		Assert.True((bool)reset["codexModelTiersAreDefaults"]!);
		Assert.Equal(["astra", "sol", "terra", "luna", "gpt"], reset["codexModelTiers"]!.AsArray().Select(r => (string)r!["match"]!));
		Assert.False(reset.AsObject().ContainsKey("codexCatalogProfileId"));
	}

	[Theory]
	[InlineData("""{"codexPath":"codex.exe"}""")]                                        // not fully qualified
	[InlineData("""{"codexPath":"C:\\Tools\\codex.cmd"}""")]                             // a shim
	[InlineData("""{"codexPath":"\\\\server\\share\\codex.exe"}""")]                     // UNC
	[InlineData("""{"codexCatalogProfileId":"codex_7"}""")]                              // unknown profile
	[InlineData("""{"codexCatalogProfileId":"default"}""")]                              // a Claude profile
	[InlineData("""{"codexModelTiers":[{"match":"sol","tier":5}]}""")]                   // tier out of range
	[InlineData("""{"codexModelTiers":[{"match":"sol","tier":-1}]}""")]
	[InlineData("""{"codexModelTiers":[{"match":"  ","tier":2}]}""")]                    // empty match
	[InlineData("""{"codexModelTiers":[{"match":"sol","tier":2},{"match":"SOL","tier":1}]}""")] // duplicate
	[InlineData("""{"codexModelTiers":[{"match":"sol","tier":2}],"resetCodexModelTiers":true}""")]
	[InlineData("""{"codexStatus":{"found":true}}""")]                                   // read-only
	[InlineData("""{"codexModelTiersAreDefaults":false}""")]                             // read-only
	public async Task Invalid_Codex_settings_are_invalid_params_and_change_nothing(string patch)
	{
		var response = await _env.CallRawAsync("settings.update", $$"""{"section":"general","baseVersion":1,"patch":{{patch}}}""");

		Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
		Assert.Equal(CodexWireEnv.CodexExe, _env.Settings.Settings.CodexPath);
		Assert.Null(_env.Settings.Settings.CodexModelTiers);
		Assert.Null(_env.Settings.Settings.CodexCatalogProfileId);
	}

	// ── ChatGPT profile administration ───────────────────────────────────────────────────────

	[Fact]
	public async Task A_browser_sign_in_answers_at_once_and_the_completion_event_follows_with_the_new_profile()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		var started = await _env.Host.CallAsync("profiles.codexLogin", JsonNode.Parse("""{"mode":"browser"}"""));
		AssertGolden("profiles.codexLogin.browser", started["result"]!);
		var listedBefore = (await _env.CallAsync("profile.list"))["codexProfiles"]!.AsArray();

		await _env.CompleteLoginAsync("codex_1", "login-1", success: true);
		var completed = await CodexWireEnv.NextAsync(sse, "profiles.codexLoginCompleted");
		await CodexWireEnv.NextAsync(sse, "profiles.changed");
		var listedAfter = (await _env.CallAsync("profile.list"))["codexProfiles"]!.AsArray();

		Assert.DoesNotContain(listedBefore, p => (string?)p!["id"] == "codex_1");
		AssertGolden("profiles.codexLoginCompleted.success", completed.Params!);
		var added = listedAfter.Single(p => (string?)p!["id"] == "codex_1")!;
		Assert.Equal(("ChatGPT · u@example.com", "plus", true), ((string?)added["displayName"], (string?)added["plan"], (bool?)added["signedIn"]));
	}

	[Fact]
	public async Task A_device_code_sign_in_answers_the_code_and_the_verification_URL()
	{
		var started = await _env.CallAsync("profiles.codexLogin", """{"mode":"deviceCode"}""");

		AssertGolden("profiles.codexLogin.deviceCode", started);
	}

	[Fact]
	public async Task A_cancelled_sign_in_ends_with_a_failed_completion_and_adds_nothing()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		var started = await _env.CallAsync("profiles.codexLogin", """{"mode":"browser"}""");

		var cancelled = await _env.CallAsync("profiles.codexLoginCancel", $$"""{"loginId":"{{(string)started["loginId"]!}}"}""");
		var completed = await CodexWireEnv.NextAsync(sse, "profiles.codexLoginCompleted");

		Assert.True((bool)cancelled["cancelled"]!);
		AssertGolden("profiles.codexLoginCompleted.cancelled", completed.Params!);
		Assert.Empty(_env.Settings.Settings.CodexProfiles);
		var again = await _env.CallRawAsync("profiles.codexLoginCancel", """{"loginId":"login-1"}""");
		Assert.Equal(-32602, again["error"]!["code"]!.GetValue<int>()); // no sign-in in progress any more
	}

	[Fact]
	public async Task A_failed_sign_in_reports_Codex_s_error_and_adds_nothing()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		await _env.CallAsync("profiles.codexLogin", """{"mode":"browser"}""");

		await _env.CompleteLoginAsync("codex_1", "login-1", success: false, error: "access denied");
		var completed = await CodexWireEnv.NextAsync(sse, "profiles.codexLoginCompleted");

		Assert.False((bool)completed.Params!["success"]!);
		Assert.Equal("ChatGPT sign-in failed: access denied", (string?)completed.Params["error"]);
		Assert.Empty(_env.Settings.Settings.CodexProfiles);
	}

	[Fact]
	public async Task A_sign_in_that_Codex_refuses_to_start_is_a_daemon_error_with_Codex_s_text()
	{
		_env.Codex.Launcher.Configure = server => server.On("account/login/start", call => call.ErrorAsync(-32600, "chatgpt login is disabled"));

		var response = await _env.CallRawAsync("profiles.codexLogin", """{"mode":"browser"}""");

		Assert.Equal(-32000, response["error"]!["code"]!.GetValue<int>());
		Assert.Contains("chatgpt login is disabled", (string?)response["error"]!["message"]);
	}

	[Fact]
	public async Task Sign_in_again_signs_an_existing_profile_in()
	{
		_env.Codex.AddStoredProfile("codex_1", "old@example.com");

		var started = await _env.CallAsync("profiles.codexLogin", """{"mode":"browser","profileId":"codex_1"}""");
		await _env.CompleteLoginAsync("codex_1", (string)started["loginId"]!, success: true);
		// The catalog refresh that follows a sign-in may also record Codex Default, so look at codex_1 only (under the settings lock: it is written meanwhile).
		await _env.Codex.Eventually.TrueAsync(() =>
		{
			lock (_env.Settings.SyncRoot)
				return _env.Settings.Settings.CodexProfiles.Any(p => p.ProfileId == "codex_1" && p.Email == "u@example.com");
		}, "the profile signed in again");

		Assert.Equal("codex_1", (string?)started["profileId"]);
	}

	[Theory]
	[InlineData("profiles.codexLogin", """{}""")]
	[InlineData("profiles.codexLogin", """{"mode":"password"}""")]
	[InlineData("profiles.codexLogin", """{"mode":"browser","profileId":"profile_1"}""")] // a Claude profile
	[InlineData("profiles.codexLogin", """{"mode":"browser","profileId":"codex_9"}""")]   // unknown
	[InlineData("profiles.codexLoginCancel", """{}""")]
	[InlineData("profiles.codexLoginCancel", """{"loginId":"nope"}""")]
	[InlineData("profiles.codexLogout", """{"profileId":"default"}""")]
	[InlineData("profiles.codexLogout", """{"profileId":"codex_9"}""")]
	[InlineData("profile.remove", """{"profileId":"codex-default"}""")]
	[InlineData("profile.remove", """{"profileId":"codex_9"}""")]
	public async Task Bad_ChatGPT_profile_params_are_invalid_params(string method, string paramsJson)
	{
		var response = await _env.CallRawAsync(method, paramsJson);

		Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task Sign_out_keeps_the_profile_listed_signed_out_and_publishes_profiles_changed()
	{
		_env.Codex.AddStoredProfile("codex_1", "u@example.com");
		using var sse = await _env.Host.OpenStreamAsync();

		var result = await _env.CallAsync("profiles.codexLogout", """{"profileId":"codex_1"}""");
		await CodexWireEnv.NextAsync(sse, "profiles.changed");

		// Codex follows account/logout with account/updated { authMode: null }, which sets the profile's auth error (FR.33.13) at an arbitrary
		// moment around the reply, so authError is not part of the golden shape.
		result.AsObject().Remove("authError");
		AssertGolden("profiles.codexLogout", result);
		Assert.Single(_env.Codex.Requests("account/logout"));
		await _env.Codex.Eventually.TrueAsync(() => _env.Codex.Profiles.HasAuthError("codex_1"), "Codex's account/updated marks the profile as needing a sign-in");
		Assert.Contains(_env.Settings.Settings.CodexProfiles, p => p.ProfileId == "codex_1");
	}

	[Fact]
	public async Task Profile_remove_deletes_a_ChatGPT_profile_renaming_its_folder_and_counting_its_pins()
	{
		_env.Codex.AddStoredProfile("codex_2", "b@example.com");
		File.WriteAllText(Path.Combine(_env.StoredHome("codex_2"), "auth.json"), "{}");
		var dir = _env.Registry.AddDirectory(Path.Combine(_env.Codex.Root, "proj"));
		var pinned = _env.Registry.AddSession(dir, "Pinned");
		lock (_env.Settings.SyncRoot)
			pinned.ProfileId = "codex_2";
		using var sse = await _env.Host.OpenStreamAsync();

		var result = await _env.CallAsync("profile.remove", """{"profileId":"codex_2"}""");
		await CodexWireEnv.NextAsync(sse, "profiles.changed");

		AssertGolden("profile.remove.codex", result);
		Assert.False(Directory.Exists(_env.StoredHome("codex_2")));
		Assert.True(File.Exists(Path.Combine(_env.StoredHome("codex_2") + "_rem", "auth.json")));
		Assert.Empty(_env.Settings.Settings.CodexProfiles);
	}

	[Fact]
	public async Task Profile_list_adds_the_ChatGPT_profiles_without_any_folder_and_keeps_the_Claude_list_as_it_was()
	{
		_env.Codex.AddStoredProfile("codex_1", "u@example.com");
		_env.Settings.Settings.CodexCatalogProfileId = "codex_1";

		var result = await _env.CallAsync("profile.list");

		AssertGolden("profile.list.codex", result);
		Assert.DoesNotContain(_env.Codex.StoredRoot.Replace("\\", "\\\\"), result.ToJsonString());
		Assert.DoesNotContain(".codex", result.ToJsonString());
	}

	[Fact]
	public async Task An_auth_error_of_a_ChatGPT_profile_publishes_profiles_changed()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		_env.Codex.Profiles.SetAuthError("codex_1", true);
		await CodexWireEnv.NextAsync(sse, "profiles.changed");
	}

	// ── Loopback only, relay variants ────────────────────────────────────────────────────────

	[Theory]
	[MemberData(nameof(LoopbackOnly))]
	public async Task The_ChatGPT_profile_methods_are_unknown_to_the_relay(string method)
	{
		var relay = await _env.CallRawAsync(method, """{"mode":"browser","loginId":"x","profileId":"codex_1"}""", TdcpTransport.Relay);

		Assert.Equal(-32601, relay["error"]!["code"]!.GetValue<int>());
	}

	public static TheoryData<string> LoopbackOnly => new(LoopbackOnlyMethods);

	[Fact]
	public void The_new_methods_are_registered_loopback_only_and_the_completion_event_never_reaches_the_relay()
	{
		var registry = new TdcpMethodRegistry();
		new ProfileAdminRpcModule(_env.ProfileAdmin, new ProfileLabelService(_env.ClaudeProfiles, _env.Settings), _env.Codex.Profiles, _env.CodexAdmin, _env.Settings)
			.Register(registry);

		Assert.All(registry.Entries, e => Assert.False(e.RemoteCallable, e.Name));
		Assert.Superset(LoopbackOnlyMethods.ToHashSet(), registry.Entries.Select(e => e.Name).ToHashSet());
		Assert.Contains("profiles.codexLoginCompleted", RemoteProtocol.AdminLocalOnlyEvents);
	}

	[Fact]
	public void Without_the_Codex_services_the_profile_module_registers_no_ChatGPT_method()
	{
		var registry = new TdcpMethodRegistry();
		new ProfileAdminRpcModule(_env.ProfileAdmin, new ProfileLabelService(_env.ClaudeProfiles, _env.Settings)).Register(registry);

		Assert.DoesNotContain(registry.Entries, e => LoopbackOnlyMethods.Contains(e.Name));
	}

	// ── Old clients ──────────────────────────────────────────────────────────────────────────

	[Fact]
	public void An_older_daemon_s_payload_reads_as_claude_in_this_client()
	{
		var state   = JsonSerializer.Deserialize<SessionStateDto>("""{"id":"n","running":false,"streamingAlive":false,"favorite":false,"hasDraft":false}""", TdcpJsonOptions.Default)!;
		var model   = JsonSerializer.Deserialize<ModelOptionDto>("""{"id":"claude-opus-5-5","displayName":"Opus","provider":"anthropic"}""", TdcpJsonOptions.Default)!;
		var profile = JsonSerializer.Deserialize<ProfileOptionDto>("""{"id":"p1","displayName":"Work"}""", TdcpJsonOptions.Default)!;
		var window  = JsonSerializer.Deserialize<UsageWindowDto>("""{"utilization":5,"resetsAtUtc":"2026-11-22T12:00:00+00:00","isStale":false}""", TdcpJsonOptions.Default)!;
		var hello   = JsonSerializer.Deserialize<HelloResult>("""{"protocolVersion":1,"daemonVersion":"1","machineName":"m","runId":"r","serverTimeUtc":"2026-11-22T12:00:00+00:00","capabilities":[],"minProtocolVersion":1,"eventSeq":0}""", TdcpJsonOptions.Default)!;

		Assert.Equal(("claude", "claude", "claude"), (state.Engine, model.Engine, profile.Engine));
		Assert.Null(model.Efforts);
		Assert.Null(window.Label);
		Assert.Null(hello.Engines);
	}

	[Fact]
	public async Task An_older_client_still_reads_every_member_it_knew_with_the_same_values()
	{
		await SignInCatalogProfileAsync();

		var catalog = await _env.CallAsync("catalog.get", transport: TdcpTransport.Relay);
		var old     = JsonSerializer.Deserialize<OldCatalogResult>(catalog.ToJsonString(), TdcpJsonOptions.Default)!;

		Assert.Equal(["default", "max", "high", "medium", "low"], old.Efforts);
		Assert.Equal("default", old.Models[0].Id);
		Assert.Null(old.Models[0].Provider);
		Assert.Equal("default", old.Profiles[0].Id);
		Assert.Contains(old.Models, m => m.Id == FixedClaudeModelsOpus && m.Provider == "anthropic");
	}

	/// <summary>The <c>catalog.get</c> shape of the 21-method web contract, as a client built before 10.A14 declares it.</summary>
	private sealed record OldCatalogResult(OldModel[] Models, string[] Efforts, OldProfile[] Profiles);

	private sealed record OldModel(string Id, string DisplayName, string? Provider);

	private sealed record OldProfile(string Id, string DisplayName);

	// ── helpers ──────────────────────────────────────────────────────────────────────────────

	private const string FixedClaudeModelsOpus = TessynDesktop.Tests.Codex.Engine.FixedClaudeModels.Opus;

	/// <summary>Signs <c>codex_1</c> in as the catalog profile and loads the fake's GPT list (gpt-x-sol default, astra, luna).</summary>
	private Task SignInCatalogProfileAsync() => _env.Codex.LoadCatalogAsync("codex_1");

	private static string[] Strings(JsonNode array) => [.. array.AsArray().Select(n => (string)n!)];

	/// <summary>The node id is a new GUID per run; the golden file holds a placeholder.</summary>
	private static JsonNode Normalized(JsonNode node, string nodeId) => JsonNode.Parse(node.ToJsonString().Replace(nodeId, "<nodeId>"))!;

	private void AssertGolden(string name, JsonNode result)
	{
		var actual = Eol(Normalize(result.ToJsonString()));
		var path = GoldenPath(name);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(Eol(File.ReadAllText(path)).TrimEnd(), actual.TrimEnd());
	}

	private static string Eol(string text) => text.Replace("\r\n", "\n");

	/// <summary>Replaces the temp root, so the snapshot is stable across runs and machines.</summary>
	private string Normalize(string json)
	{
		var root = JsonSerializer.Serialize(_env.Codex.Root)[1..^1];
		json = json.Replace(root, "<root>");
		json = Regex.Replace(json, "<root>[^\"]*", m => m.Value.Replace("\\\\", "/"));
		var node = JsonNode.Parse(json)!;
		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
