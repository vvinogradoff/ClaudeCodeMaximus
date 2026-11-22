using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>Plan usage of a ChatGPT profile (FR.33.10, TC.CX.23): window labels, a missing window, sparse updates, staleness, the status-bar label.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexUsageTests : IAsyncLifetime
{
	private readonly CodexServicesFixture _f = new();

	public async Task InitializeAsync()
	{
		_f.AddStoredProfile("codex_1", "u@example.com");
		await Task.CompletedTask;
	}

	public async Task DisposeAsync() => await _f.DisposeAsync();

	private long InHours(double hours) => _f.Clock.GetUtcNow().AddHours(hours).ToUnixTimeSeconds();

	private JsonObject Snapshot(JsonNode? primary, JsonNode? secondary, string? reached = null) => new()
	{
		["limitId"] = "codex", ["primary"] = primary, ["secondary"] = secondary, ["planType"] = "plus", ["rateLimitReachedType"] = reached,
	};

	private JsonObject Window(double used, int minutes, long resets) => new() { ["usedPercent"] = used, ["windowDurationMins"] = minutes, ["resetsAt"] = resets };

	private void ServeLimits(JsonObject snapshot) =>
		_f.Launcher.Configure = server => server.On("account/rateLimits/read", call => call.ReplyAsync(new JsonObject
		{
			["rateLimits"] = new JsonObject { ["limitId"] = "other" }, ["rateLimitsByLimitId"] = new JsonObject { ["codex"] = snapshot.DeepClone() },
		}));

	[Fact]
	public async Task TC_CX_23_the_windows_are_labelled_from_their_durations_and_come_from_the_codex_bucket()
	{
		ServeLimits(Snapshot(Window(12, 300, InHours(3)), Window(40, 10080, InHours(100))));

		var reading = await _f.Usage.ReadAsync("codex_1");
		var result  = await _f.Usage.GetAsync("codex_1", CancellationToken.None);

		Assert.Equal(("5h", 12d), (reading!.Primary!.Label, reading.Primary.UsedPercent));
		Assert.Equal(("7d", 40d), (reading.Secondary!.Label, reading.Secondary.UsedPercent));
		Assert.Equal(UsageState.Ok, result.State);
		Assert.Equal("ChatGPT · u@example.com", result.ProfileName);
		Assert.Equal(12, result.FiveHour!.Utilization);
		Assert.Equal(40, result.SevenDay!.Utilization);
		Assert.Equal("90m", _f.Usage.Label(90));
	}

	[Fact]
	public async Task TC_CX_23_with_no_short_window_only_one_bar_is_shown()
	{
		ServeLimits(Snapshot(null, Window(55, 10080, InHours(20))));

		var result = await _f.Usage.GetAsync("codex_1", CancellationToken.None);

		Assert.Null(result.FiveHour);
		Assert.Equal(55, result.SevenDay!.Utilization);
	}

	[Fact]
	public async Task TC_CX_23_a_sparse_update_with_a_null_window_keeps_the_known_value()
	{
		ServeLimits(Snapshot(Window(10, 300, InHours(3)), Window(20, 10080, InHours(100))));
		await _f.Usage.ReadAsync("codex_1");
		string? changed = null;
		_f.Usage.Changed += id => changed = id;

		await _f.ServerOf(Path.Combine(_f.StoredRoot, "codex_1")).NotifyAsync("account/rateLimits/updated",
			new JsonObject { ["rateLimits"] = Snapshot(null, Window(25, 10080, InHours(100))) });
		await _f.Eventually.TrueAsync(() => changed == "codex_1", "the update was merged");

		var reading = _f.Usage.GetReading("codex_1")!;
		Assert.Equal(10, reading.Primary!.UsedPercent);
		Assert.Equal(25, reading.Secondary!.UsedPercent);
	}

	[Fact]
	public async Task A_window_whose_reset_has_passed_is_shown_as_zero_and_stale()
	{
		ServeLimits(Snapshot(Window(80, 300, InHours(1)), Window(20, 10080, InHours(100))));
		await _f.Usage.ReadAsync("codex_1");

		_f.Clock.Advance(TimeSpan.FromHours(2));

		var reading = _f.Usage.GetReading("codex_1")!;
		Assert.Equal((0d, true), (reading.Primary!.UsedPercent, reading.Primary.IsStale));
		Assert.False(reading.Secondary!.IsStale);
	}

	[Fact]
	public async Task A_read_is_cached_and_a_failure_keeps_the_last_reading()
	{
		ServeLimits(Snapshot(Window(10, 300, InHours(3)), Window(20, 10080, InHours(100))));
		await _f.Usage.ReadAsync("codex_1");
		await _f.Usage.ReadAsync("codex_1");
		Assert.Single(_f.Requests("account/rateLimits/read"));

		_f.Launcher.Latest.On("account/rateLimits/read", call => call.ErrorAsync(-32600, "codex account authentication required to read rate limits"));
		_f.Clock.Advance(Constants.Remote.Usage.CacheDuration + TimeSpan.FromSeconds(1));
		var reading = await _f.Usage.ReadAsync("codex_1");

		Assert.Equal(2, _f.Requests("account/rateLimits/read").Length);
		Assert.Equal(10, reading!.Primary!.UsedPercent);
	}

	[Fact]
	public async Task The_limit_reset_suffix_names_the_exhausted_window()
	{
		ServeLimits(Snapshot(Window(30, 300, InHours(3)), Window(100, 10080, InHours(30)), reached: "rateLimitReached"));
		await _f.Usage.ReadAsync("codex_1");

		var expected = _f.Clock.GetUtcNow().AddHours(30).ToLocalTime().ToString("ddd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
		Assert.Equal($" (resets {expected})", _f.Usage.LimitResetSuffix("codex_1"));
		Assert.True(_f.Usage.GetReading("codex_1")!.Secondary!.IsExhausted);
	}

	[Fact]
	public async Task TC_CX_23_the_left_label_of_a_GPT_session_is_its_name_and_the_ChatGPT_plan()
	{
		await _f.LoadCatalogAsync();

		Assert.Equal("GPT-X-Sol  ·  ChatGPT plan", _f.Models.StatusBarModelLabel("gpt-x-sol"));
		Assert.Null(_f.Models.StatusBarModelLabel(FixedClaudeModels.Opus));
	}

	[Fact]
	public void Usage_handles_only_ChatGPT_profile_ids()
	{
		Assert.True(_f.Usage.Handles("codex_1"));
		Assert.True(_f.Usage.Handles("codex-default"));
		Assert.False(_f.Usage.Handles("profile_1"));
		Assert.False(_f.Usage.Handles("default"));
	}

	[Fact]
	public async Task Usage_get_answers_a_ChatGPT_profile_from_the_Codex_source_and_leaves_Claude_profiles_to_the_Claude_reader()
	{
		ServeLimits(Snapshot(Window(33, 300, InHours(3)), Window(44, 10080, InHours(100))));
		var fetcher = new TessynDesktop.Tests.Services.StubUsageFetcher();
		var profiles = new TessynDesktop.Tests.Services.FakeProfileServiceForTurnService();
		var sut = new TessynDesktop.Services.RemoteUsageService(fetcher, new TessynDesktop.Tests.Services.StubDesktopUsage(), profiles, _f.Settings,
			new TessynDesktop.Services.ProfileLabelService(profiles, _f.Settings), new TessynDesktop.Services.RemoteEventBus(), engineSources: [_f.Usage]);

		var gpt = await sut.GetAsync("codex_1", CancellationToken.None);

		Assert.Equal(("codex_1", "ChatGPT · u@example.com", 33d, 44d), (gpt.ProfileId, gpt.ProfileName, gpt.FiveHour!.Utilization, gpt.SevenDay!.Utilization));
		Assert.Equal(0, fetcher.Calls);
	}
}
