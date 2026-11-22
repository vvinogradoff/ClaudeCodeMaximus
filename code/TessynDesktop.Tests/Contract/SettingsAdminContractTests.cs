using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J3 on the wire (settings sections, profile administration, sounds): golden result shapes through the in-process dispatcher and a real
/// loopback server, loopback-only registration (the relay answers -32601), the capability flags, the notifications on the local stream, the error
/// codes, and the secrecy rules (no secret, no token-shaped value, no account e-mail outside <c>profile.list</c> in any result).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SettingsAdminContractTests : IDisposable
{
	private static readonly string[] NewMethods =
	[
		"settings.sections", "settings.get", "settings.update",
		"profile.list", "profile.add", "profile.rename", "profile.remove", "profile.authenticate",
		"sound.list", "sound.test",
		// Part 2: the MCP registry, Telegram, relay and model refresh.
		"mcp.list", "mcp.upsert", "mcp.remove", "mcp.setEnabled", "mcp.discover", "mcp.authorize",
		"telegram.status", "telegram.setToken", "telegram.clearToken",
		"relay.status", "relay.configure",
		"models.refresh",
	];

	private readonly SettingsAdminEnv _env = new();

	public SettingsAdminContractTests()
	{
		_env.AddStoredProfile("p1", "first@example.com");
		_env.AddStoredProfile("p2", "second@example.com", loggedIn: false);
		File.WriteAllText(Path.Combine(_env.DataRoot, "ding.wav"), "RIFF");
	}

	public void Dispose() => _env.Dispose();

	public static TheoryData<string, string, string> Snapshots => new()
	{
		{ "settings.sections", "settings.sections", "{}" },
		{ "settings.get.general", "settings.get", """{"section":"general"}""" },
		{ "settings.get.notifications", "settings.get", """{"section":"notifications"}""" },
		{ "settings.get.telegram", "settings.get", """{"section":"telegram"}""" },
		{ "settings.update", "settings.update", """{"section":"telegram","baseVersion":1,"patch":{"telegramChatId":-1001,"telegramMirrorMode":"off"}}""" },
		{ "profile.list", "profile.list", "{}" },
		{ "profile.add", "profile.add", """{"displayName":"Work"}""" },
		{ "profile.rename", "profile.rename", """{"profileId":"p1","displayName":"Renamed"}""" },
		{ "profile.remove", "profile.remove", """{"profileId":"p2"}""" },
		{ "profile.authenticate", "profile.authenticate", """{"profileId":"p1"}""" },
		{ "sound.list", "sound.list", "{}" },
		{ "sound.test", "sound.test", """{"file":"$DING"}""" },

		// Part 2. Run in this order by the secrecy scan on one environment, so each call must stay valid after the ones before it. The requests carry
		// secrets on purpose: none may come back.
		{ "mcp.list", "mcp.list", "{}" },
		{ "mcp.upsert.http", "mcp.upsert", """{"id":"mcp4","displayName":"Search","url":"https://search.example.com/mcp?token=q-secret-key-777","authType":"staticHeader","staticHeaderName":"X-Api-Key","staticHeaderValue":"hunter2-static-header-value"}""" },
		{ "mcp.upsert.update", "mcp.upsert", """{"id":"mcp2","displayName":"Tracker 2","oauth":{"scopes":"read","clientSecret":"oauth-client-secret-9f8e7d"}}""" },
		{ "mcp.upsert.stdio", "mcp.upsert", """{"id":"mcp5","displayName":"Files","transport":"stdio","command":"npx","arguments":"-y server --token=stdio-arg-secret-555"}""" },
		{ "mcp.remove", "mcp.remove", """{"id":"mcp3"}""" },
		{ "mcp.setEnabled", "mcp.setEnabled", """{"id":"mcp1","enabled":false}""" },
		{ "mcp.discover", "mcp.discover", """{"url":"https://tracker.example.com/mcp"}""" },
		{ "mcp.authorize", "mcp.authorize", """{"id":"mcp2"}""" },
		{ "telegram.status", "telegram.status", "{}" },
		{ "telegram.setToken", "telegram.setToken", """{"token":"987654321:AAFsecretBotTokenForTestsOnly_0123456789ab"}""" },
		{ "telegram.clearToken", "telegram.clearToken", "{}" },
		{ "relay.status", "relay.status", "{}" },
		{ "relay.configure", "relay.configure", """{"enabled":true,"url":"https://web2.example.com","linkToken":"relay-link-token-secret-xyz-0001"}""" },
		{ "models.refresh", "models.refresh", "{}" },
	};

	// ── Registration, capabilities ───────────────────────────────────────────────────────────

	[Fact]
	public void TheNewMethods_AreExactlyTheTwentyTwoOfPackageJ3_AllLoopbackOnly()
	{
		var methods = _env.Methods();

		Assert.Equal(NewMethods.Order(), methods.Select(m => m.Name).Order());
		Assert.All(methods, m => Assert.False(m.RemoteCallable, m.Name));
	}

	[Theory]
	[MemberData(nameof(MethodNames))]
	public async Task EveryNewMethod_IsUnknownToTheRelay_ButKnownOnLoopback(string method)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = new JsonObject() }.ToJsonString();

		var relay = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Relay, CancellationToken.None);
		var loopback = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Loopback, CancellationToken.None);

		Assert.Equal(-32601, relay!["error"]!["code"]!.GetValue<int>());
		Assert.NotEqual(-32601, loopback!["error"]?["code"]?.GetValue<int>() ?? 0);
	}

	public static TheoryData<string> MethodNames => new(NewMethods);

	[Fact]
	public async Task Hello_AnnouncesTheCapabilitiesOfJ3_WhenTheirServicesAreWired()
	{
		var result = (await _env.Host.CallAsync("client.hello"))["result"]!;

		var capabilities = result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.Equal(["events.local", "events.resume", "settings", "profiles.admin", "sound", "mcp.registry", "telegram.settings", "relay.settings", "models.admin"], capabilities);
	}

	[Fact]
	public void TheAdminNotifications_AreLocalOnly()
	{
		Assert.Equal(["settings.changed", "profiles.changed", "mcp.changed", "catalog.changed", "profiles.codexLoginCompleted"], RemoteProtocol.AdminLocalOnlyEvents);
	}

	// ── Result shapes ────────────────────────────────────────────────────────────────────────

	[Theory]
	[MemberData(nameof(Snapshots))]
	public async Task InProcess_result_matches_the_golden_file(string golden, string method, string paramsJson)
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{Seeded(paramsJson)}}}""";

		var response = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Loopback, CancellationToken.None);

		Assert.Null(response!["error"]);
		AssertGolden(golden, response["result"]!);
	}

	[Theory]
	[MemberData(nameof(Snapshots))]
	public async Task Loopback_http_result_matches_the_same_golden_file(string golden, string method, string paramsJson)
	{
		var response = await _env.Host.CallAsync(method, JsonNode.Parse(Seeded(paramsJson)));

		Assert.Null(response["error"]);
		AssertGolden(golden, response["result"]!);
	}

	// ── Errors ───────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task AStaleBaseVersion_IsADaemonError_Settings_changed()
	{
		await _env.Host.CallAsync("settings.update", JsonNode.Parse("""{"section":"notifications","baseVersion":1,"patch":{"notificationsEnabled":false}}"""));

		var stale = await _env.Host.CallAsync("settings.update", JsonNode.Parse("""{"section":"notifications","baseVersion":1,"patch":{"notificationsEnabled":true}}"""));

		Assert.Equal(-32000, stale["error"]!["code"]!.GetValue<int>());
		Assert.Equal("Settings changed", stale["error"]!["message"]!.GetValue<string>());
	}

	[Theory]
	[InlineData("settings.get", """{}""")]
	[InlineData("settings.get", """{"section":"nope"}""")]
	[InlineData("settings.update", """{"section":"nope","baseVersion":1,"patch":{}}""")]                      // unknown section
	[InlineData("settings.update", """{"section":"general","patch":{}}""")]                                   // baseVersion missing
	[InlineData("settings.update", """{"section":"general","baseVersion":1}""")]                             // patch missing
	[InlineData("settings.update", """{"section":"general","baseVersion":1,"patch":{"tree":[]}}""")]         // outside the whitelist
	[InlineData("settings.update", """{"section":"general","baseVersion":1,"patch":{"claudePath":""}}""")]   // invalid value
	[InlineData("profile.add", """{"displayName":"  "}""")]
	[InlineData("profile.rename", """{"profileId":"nope","displayName":"x"}""")]
	[InlineData("profile.remove", """{"profileId":"nope"}""")]
	[InlineData("profile.remove", """{"profileId":"default"}""")]
	[InlineData("profile.authenticate", """{"profileId":"nope"}""")]
	[InlineData("sound.test", """{"file":"C:\\a.mp3"}""")]
	[InlineData("sound.test", """{"file":"\\\\10.0.0.5\\s\\a.wav"}""")]    // UNC: refused before any file access
	[InlineData("sound.test", """{"file":"\\foo.wav"}""")]
	[InlineData("sound.test", """{"file":"C:foo.wav"}""")]
	[InlineData("sound.test", """{"file":"\\\\?\\C:\\x.wav"}""")]
	[InlineData("settings.update", """{"section":"general","baseVersion":1,"patch":{"claudePath":"C:\\tmp\\evil.exe"}}""")]
	[InlineData("sound.test", """{}""")]
	public async Task BadParams_AreInvalidParams(string method, string paramsJson)
	{
		var response = await _env.Host.CallAsync(method, JsonNode.Parse(paramsJson));

		Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
	}

	// ── Secrecy ──────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task NoResult_CarriesASecret_ATokenShapedValue_OrAnAccountEmailOutsideProfileList()
	{
		var results = new System.Collections.Generic.Dictionary<string, JsonNode>();
		foreach (var (golden, method, paramsJson) in Snapshots.Select(d => ((string)d[0], (string)d[1], (string)d[2])))
		{
			var response = await _env.Host.CallAsync(method, JsonNode.Parse(Seeded(paramsJson)));
			Assert.Null(response["error"]);
			results[golden] = response["result"]!;
		}

		// The whole model with every secret in it is behind these: scan every string of every result.
		foreach (var (name, result) in results)
			AssertCarriesNoSecret(name, result, accountEmailAllowed: name == "profile.list");

		Assert.Contains(SettingsAdminEnv.DefaultAccountEmail, results["profile.list"].ToJsonString()); // the one place it is meant to be
	}

	[Fact]
	public async Task ProfileList_NeverCarriesAConfigDirectory()
	{
		var response = await _env.Host.CallAsync("profile.list");

		var text = response["result"]!.ToJsonString();
		Assert.DoesNotContain(_env.ProfileService.ProfilesRootDirectory.Replace("\\", "\\\\"), text);
		Assert.DoesNotContain("configDir", text, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The secrecy rule for one result: none of the seeded secrets, no string that has the shape of a token (a bot token, a 32+ character opaque run, a bearer
	/// value, an API key, a JWT) and, unless allowed, not the Default account's e-mail. Shared with the part 2 methods, whose secrets are write-only too.
	/// </summary>
	internal static void AssertCarriesNoSecret(string name, JsonNode result, bool accountEmailAllowed)
	{
		var text = result.ToJsonString();
		foreach (var secret in SettingsAdminEnv.Secrets)
			Assert.DoesNotContain(secret, text);

		foreach (var value in StringsOf(result))
		{
			Assert.False(TokenShape().IsMatch(value), $"{name}: '{value}' looks like a token");
			if (!accountEmailAllowed)
				Assert.DoesNotContain(SettingsAdminEnv.DefaultAccountEmail, value);
		}
	}

	[GeneratedRegex(@"(\d{6,12}:[A-Za-z0-9_-]{30,})|([A-Za-z0-9+/_=-]{32,})|(^Bearer\s)|(^sk-)|(^eyJ)")]
	private static partial Regex TokenShape();

	private static System.Collections.Generic.IEnumerable<string> StringsOf(JsonNode node)
	{
		switch (node)
		{
			case JsonValue value when value.TryGetValue<string>(out var text):
				yield return text;
				break;
			case JsonObject obj:
				foreach (var property in obj)
				foreach (var inner in property.Value == null ? [] : StringsOf(property.Value))
					yield return inner;
				break;
			case JsonArray array:
				foreach (var item in array)
				foreach (var inner in item == null ? [] : StringsOf(item))
					yield return inner;
				break;
		}
	}

	// ── Notifications on the local stream ────────────────────────────────────────────────────

	[Fact]
	public async Task SettingsUpdate_PublishesSettingsChanged_OnTheLocalStream()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		await _env.Host.CallAsync("settings.update", JsonNode.Parse("""{"section":"notifications","baseVersion":1,"patch":{"sessionCompletionSoundThrottleSeconds":9}}"""));

		var frame = await NextAsync(sse, "settings.changed");
		Assert.Equal(("notifications", 2L), (frame.Params!["section"]!.GetValue<string>(), frame.Params["version"]!.GetValue<long>()));
		Assert.True(frame.Id > 0);
	}

	[Fact]
	public async Task AnAvaloniaSettingsSave_AlsoPublishesSettingsChanged_ForTheSectionsThatChanged()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		_env.Settings.Settings.TelegramMentionHandle = "@boss"; // what the Avalonia Settings window does
		_env.Settings.Save();

		var frame = await NextAsync(sse, "settings.changed");
		Assert.Equal(("telegram", 2L), (frame.Params!["section"]!.GetValue<string>(), frame.Params["version"]!.GetValue<long>()));
	}

	[Fact]
	public async Task ProfileAdd_PublishesProfilesChanged()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		await _env.Host.CallAsync("profile.add", JsonNode.Parse("""{"displayName":"Work"}"""));

		var frame = await NextAsync(sse, "profiles.changed");
		Assert.Empty(frame.Params!.AsObject());
	}

	[Fact]
	public async Task ProfileAuthenticate_AnswersAtOnce_AndProfilesChangedFollowsTheLogin_ASecondStartIsRefusedWhileItRuns()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.ProfileService.OnLogin = _ => gate.Task;
		_env.ProfileService.SetEmail(Path.Combine(_env.ProfileService.ProfilesRootDirectory, "p1"), "fresh@example.com");

		var first = await _env.Host.CallAsync("profile.authenticate", JsonNode.Parse("""{"profileId":"p1"}"""));
		var second = await _env.Host.CallAsync("profile.authenticate", JsonNode.Parse("""{"profileId":"p1"}"""));
		gate.SetResult();
		await NextAsync(sse, "profiles.changed");

		Assert.True(first["result"]!["started"]!.GetValue<bool>());
		Assert.False(second["result"]!["started"]!.GetValue<bool>());
		var list = (await _env.Host.CallAsync("profile.list"))["result"]!["profiles"]!.AsArray();
		Assert.Equal("fresh@example.com", list.Single(p => p!["id"]!.GetValue<string>() == "p1")!["displayName"]!.GetValue<string>());
	}

	[Fact]
	public async Task SoundTest_PlaysAnExistingWav_AndReportsAMissingOneAsNotPlayed()
	{
		var ding = Path.Combine(_env.DataRoot, "ding.wav");
		var played = await _env.Host.CallAsync("sound.test", new JsonObject { ["file"] = ding });
		var missing = await _env.Host.CallAsync("sound.test", new JsonObject { ["file"] = Path.Combine(_env.DataRoot, "none.wav") });

		Assert.True(played["result"]!["played"]!.GetValue<bool>());
		Assert.False(missing["result"]!["played"]!.GetValue<bool>());
		Assert.Equal([ding], _env.Sound.TestPlays);
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────

	private static async Task<SseTestFrame> NextAsync(SseTestClient sse, string method)
	{
		while (true)
		{
			var frame = await sse.NextAsync(TimeSpan.FromSeconds(10));
			if (frame.Method == method)
				return frame;
		}
	}

	private string Seeded(string paramsJson) =>
		paramsJson.Replace("$DING", JsonSerializer.Serialize(Path.Combine(_env.DataRoot, "ding.wav"))[1..^1]);

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
		var root = JsonSerializer.Serialize(_env.DataRoot)[1..^1];
		json = json.Replace(root, "<root>");
		json = Regex.Replace(json, "<root>[^\"]*", m => m.Value.Replace("\\\\", "/"));
		var node = JsonNode.Parse(json)!;
		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
