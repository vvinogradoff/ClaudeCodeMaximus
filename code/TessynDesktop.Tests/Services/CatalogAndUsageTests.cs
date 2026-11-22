using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>catalog.get (contract section 3.20).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CatalogRpcModuleTests : IDisposable
{
	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "cm_test_catalog_" + Path.GetRandomFileName());
	private readonly AppSettingsService _settings;
	private readonly FakeProfileServiceForTurnService _profiles = new();
	private readonly FakeModelServiceForTurnService _models = new();
	private readonly CatalogRpcModule _module;

	public CatalogRpcModuleTests()
	{
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_module   = new CatalogRpcModule(_models, _settings, new ProfileLabelService(_profiles, _settings));
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private async Task<JsonNode> Get() => (await RemoteTestHelpers.CallAsync(_module, "catalog.get", "{}"))!;

	[Fact]
	public async Task Models_StartWithDefault_ThenTheCachedModels()
	{
		_models.Models =
		[
			new ClaudeModelInfo("claude-opus-5-5", "opus", "Opus 5.5"),
			new ClaudeModelInfo("llama3", "llama3", "Llama 3", ModelProvider.Ollama, SupportsTools: false),
		];

		var json = await Get();

		var models = json["models"]!.AsArray();
		Assert.Equal(3, models.Count);
		Assert.Equal("default", (string?)models[0]!["id"]);
		Assert.Equal("Default", (string?)models[0]!["displayName"]);
		Assert.False(models[0]!.AsObject().ContainsKey("provider"));
		Assert.Equal("claude-opus-5-5", (string?)models[1]!["id"]);
		Assert.Equal("Opus 5.5", (string?)models[1]!["displayName"]);
		Assert.Equal("anthropic", (string?)models[1]!["provider"]);
		Assert.Equal("ollama", (string?)models[2]!["provider"]);
	}

	[Fact]
	public async Task MediaPreviewHosts_AreTheSettingsListWithTheDefaultsAndAreNormalised()
	{
		var defaults = await Get();
		_settings.Settings.MediaPreviewHosts = ["  CDN.Example.test ", "", "*.images.test"];
		var custom = await Get();

		Assert.Equal(["annielartens.online", "*.githubusercontent.com"], defaults["mediaPreviewHosts"]!.AsArray().Select(h => (string)h!));
		Assert.Equal(["cdn.example.test", "*.images.test"], custom["mediaPreviewHosts"]!.AsArray().Select(h => (string)h!));
	}

	[Fact]
	public async Task Efforts_AreTheSharedList()
	{
		var json = await Get();

		Assert.Equal(["default", "max", "high", "medium", "low"], json["efforts"]!.AsArray().Select(e => (string)e!));
	}

	[Fact]
	public async Task Profiles_StartWithTheDefaultLabel_ThenTheStoredProfiles_WithoutDirectories()
	{
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p2", DisplayName = "Home" });

		var json = await Get();

		var profiles = json["profiles"]!.AsArray();
		Assert.Equal(["default", "p1", "p2"], profiles.Select(p => (string)p!["id"]!));
		Assert.Equal(["[Sys] me@example.com", "Work", "Home"], profiles.Select(p => (string)p!["displayName"]!));
		Assert.All(profiles, p => Assert.Equal(3, p!.AsObject().Count)); // id, displayName and (tdcp-spec 10.A14) engine
		Assert.All(profiles, p => Assert.Equal("claude", (string?)p!["engine"]));
		Assert.DoesNotContain("profiles\\", json.ToJsonString());
	}

	[Fact]
	public async Task DefaultProfileLabel_FallsBackWhenTheEmailIsUnknown()
	{
		_profiles.Email = null;

		var json = await Get();

		Assert.Equal("[Sys] Default", (string?)json["profiles"]![0]!["displayName"]);
	}

	[Fact]
	public async Task NoModelsAndNoProfiles_StillOffersTheDefaults()
	{
		var json = await Get();

		Assert.Single(json["models"]!.AsArray());
		Assert.Single(json["profiles"]!.AsArray());
	}
}

/// <summary>A usage fetcher that returns scripted results and counts its calls.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StubUsageFetcher : IClaudeUsageFetcher
{
	private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public int Calls { get; private set; }
	public List<string> Paths { get; } = new();
	public UsageFetchResultModel Result { get; set; } = new(UsageState.Ok, null);
	public bool Hold { get; set; }

	public void Release() => _gate.TrySetResult();

	public async Task<UsageFetchResultModel> FetchAsync(string credentialsPath, CancellationToken cancellationToken)
	{
		Calls++;
		Paths.Add(credentialsPath);
		if (Hold)
			await _gate.Task;

		return Result;
	}
}

/// <summary>The desktop poller as the remote usage service sees it; polling control is off limits.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StubDesktopUsage : IClaudeUsageService
{
	public ClaudeUsageData? CachedUsage => null;
	public string? ActiveCredentialsPath { get; set; }
	public event EventHandler? UsageUpdated;

	public void SetActiveProfile(string? credentialsFilePath) =>
		throw new InvalidOperationException("The remote usage service must not touch the desktop's active profile.");

	public void RaiseUpdated() => UsageUpdated?.Invoke(this, EventArgs.Empty);
}

/// <summary>usage.get (contract section 3.21): per-profile fetch with a five-minute cache.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteUsageServiceTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "cm_test_usage_" + Path.GetRandomFileName());
	private readonly AppSettingsService _settings;
	private readonly StubUsageFetcher _fetcher = new();
	private readonly StubDesktopUsage _desktop = new();
	private readonly ManualTimeProvider _clock = new();
	private readonly RemoteEventBus _bus = new();
	private readonly List<RemoteNotification> _published = new();
	private readonly SemaphoreSlim _signal = new(0);
	private readonly RemoteUsageService _sut;
	private readonly UsageRpcModule _module;

	public RemoteUsageServiceTests()
	{
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });

		var profileService = new FakeProfileServiceForTurnService();
		_bus.Published += n =>
		{
			lock (_published)
				_published.Add(n);
			_signal.Release();
		};

		_sut    = new RemoteUsageService(_fetcher, _desktop, profileService, _settings, new ProfileLabelService(profileService, _settings), _bus,
			_clock, fetchWait: TimeSpan.FromMilliseconds(300));
		_module = new UsageRpcModule(_sut);
		_fetcher.Result = Ok();
	}

	public void Dispose()
	{
		_bus.Dispose();
		Directory.Delete(_tempDir, recursive: true);
	}

	private UsageFetchResultModel Ok(double fiveHour = 37, double sevenDay = 12) =>
		new(UsageState.Ok, new ClaudeUsageData(
			fiveHour, _clock.GetUtcNow().AddHours(2), "normal",
			sevenDay, _clock.GetUtcNow().AddDays(3), "normal"));

	private Task<UsageResult> Get(string profileId = "default") => _sut.GetAsync(profileId, CancellationToken.None);

	private List<RemoteNotification> Published()
	{
		lock (_published)
			return [.. _published];
	}

	[Fact]
	public async Task FirstGet_Fetches_AndReportsBothWindows()
	{
		var result = await Get();

		Assert.Equal(UsageState.Ok, result.State);
		Assert.Equal("default", result.ProfileId);
		Assert.Equal("[Sys] me@example.com", result.ProfileName);
		Assert.Equal(37, result.FiveHour!.Utilization);
		Assert.False(result.FiveHour.IsStale);
		Assert.Equal(12, result.SevenDay!.Utilization);
		Assert.Equal(_clock.GetUtcNow(), result.FetchedAtUtc);
		Assert.Equal(1, _fetcher.Calls);
		Assert.Equal(ClaudeCredentialsPaths.Resolve(null), _fetcher.Paths.Single());
	}

	[Fact]
	public async Task ASecondGetWithinFiveMinutes_UsesTheCache()
	{
		await Get();
		_clock.Advance(TimeSpan.FromMinutes(4));
		await Get();

		Assert.Equal(1, _fetcher.Calls);
	}

	[Fact]
	public async Task AfterFiveMinutes_TheApiIsAskedAgain()
	{
		await Get();
		_clock.Advance(Constants.Remote.Usage.CacheDuration + TimeSpan.FromSeconds(1));
		_fetcher.Result = Ok(fiveHour: 50);

		var result = await Get();

		Assert.Equal(2, _fetcher.Calls);
		Assert.Equal(50, result.FiveHour!.Utilization);
	}

	[Fact]
	public async Task EachProfile_HasItsOwnCache_AndItsOwnCredentials()
	{
		await Get("default");
		var work = await Get("p1");

		Assert.Equal(2, _fetcher.Calls);
		Assert.Equal("Work", work.ProfileName);
		Assert.Equal(ClaudeCredentialsPaths.Resolve(@"C:\profiles\p1"), _fetcher.Paths[1]);
	}

	[Fact]
	public async Task UnknownProfile_IsInvalidParams()
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => Get("nobody"));

		Assert.Equal(0, _fetcher.Calls);
	}

	[Fact]
	public async Task Unauthorized_IsReported_AndNotRetriedBeforeTheCacheExpires()
	{
		_fetcher.Result = new UsageFetchResultModel(UsageState.Unauthorized, null);

		var first = await Get();
		_clock.Advance(TimeSpan.FromMinutes(2));
		await Get();

		Assert.Equal(UsageState.Unauthorized, first.State);
		Assert.Null(first.FiveHour);
		Assert.Equal(1, _fetcher.Calls);
	}

	[Fact]
	public async Task Unavailable_IsRetriedAfterAMinute()
	{
		_fetcher.Result = new UsageFetchResultModel(UsageState.Unavailable, null);

		var first = await Get();
		_clock.Advance(TimeSpan.FromSeconds(30));
		await Get();
		_clock.Advance(TimeSpan.FromSeconds(40));
		_fetcher.Result = Ok();
		var third = await Get();

		Assert.Equal(UsageState.Unavailable, first.State);
		Assert.Equal(2, _fetcher.Calls);
		Assert.Equal(UsageState.Ok, third.State);
	}

	[Fact]
	public async Task AFailedRefresh_KeepsServingTheEarlierReading()
	{
		await Get();
		_clock.Advance(Constants.Remote.Usage.CacheDuration + TimeSpan.FromSeconds(1));
		_fetcher.Result = new UsageFetchResultModel(UsageState.Unavailable, null);

		var result = await Get();

		Assert.Equal(UsageState.Ok, result.State);
		Assert.Equal(37, result.FiveHour!.Utilization);
	}

	[Fact]
	public async Task AWindowThatRolledOver_IsZeroedAndMarkedStale()
	{
		await Get();
		_clock.Advance(TimeSpan.FromHours(3));   // past the 5h reset, before the 7d one

		var result = await Get();

		Assert.True(result.FiveHour!.IsStale);
		Assert.Equal(0, result.FiveHour.Utilization);
		Assert.False(result.SevenDay!.IsStale);
		Assert.Equal(12, result.SevenDay.Utilization);
	}

	[Fact]
	public async Task ASlowFirstFetch_AnswersPending_ThenTheDataFollows_AndConcurrentCallsShareOneFetch()
	{
		_fetcher.Hold = true;

		var first  = await Get();
		var second = await Get();

		Assert.Equal(UsageState.Pending, first.State);
		Assert.Equal(UsageState.Pending, second.State);
		Assert.Equal(1, _fetcher.Calls);

		_fetcher.Release();
		Assert.True(await _signal.WaitAsync(Wait));
		var third = await Get();
		Assert.Equal(UsageState.Ok, third.State);
		Assert.Equal(1, _fetcher.Calls);
	}

	[Fact]
	public async Task NewData_PublishesUsageChanged_UnchangedDataDoesNot()
	{
		await Get();
		Assert.True(await _signal.WaitAsync(Wait));
		var notification = Published().Single();
		Assert.Equal(Constants.Remote.Events.UsageChanged, notification.Method);
		Assert.Equal("default", (string?)notification.Params["profileId"]);

		_clock.Advance(Constants.Remote.Usage.CacheDuration + TimeSpan.FromSeconds(1));
		await Get();                       // same reading
		Assert.False(await _signal.WaitAsync(TimeSpan.FromMilliseconds(500)));

		_clock.Advance(Constants.Remote.Usage.CacheDuration + TimeSpan.FromSeconds(1));
		_fetcher.Result = Ok(fiveHour: 80);
		await Get();                       // new reading
		Assert.True(await _signal.WaitAsync(Wait));
		Assert.Equal(2, Published().Count);
	}

	[Fact]
	public async Task TheDesktopPollerUpdating_PublishesForTheActiveProfile_AndMakesTheNextGetFetchAgain()
	{
		await Get("p1");
		Assert.True(await _signal.WaitAsync(Wait));
		_desktop.ActiveCredentialsPath = ClaudeCredentialsPaths.Resolve(@"C:\profiles\p1");

		_desktop.RaiseUpdated();
		Assert.True(await _signal.WaitAsync(Wait));

		var notification = Published().Last();
		Assert.Equal(Constants.Remote.Events.UsageChanged, notification.Method);
		Assert.Equal("p1", (string?)notification.Params["profileId"]);

		await Get("p1");   // well inside the cache window, yet it asks the API again
		Assert.Equal(2, _fetcher.Calls);
	}

	[Fact]
	public async Task TheDesktopPollerUpdating_WithoutAnActiveProfileOrAnUnknownOne_PublishesNothing()
	{
		_desktop.ActiveCredentialsPath = null;
		_desktop.RaiseUpdated();
		_desktop.ActiveCredentialsPath = @"C:\somewhere\else\.credentials.json";
		_desktop.RaiseUpdated();

		Assert.False(await _signal.WaitAsync(TimeSpan.FromMilliseconds(600)));
	}

	[Fact]
	public async Task TheRpcMethod_DefaultsToTheDefaultProfile_AndSerializesTheWireShape()
	{
		var json = (await RemoteTestHelpers.CallAsync(_module, "usage.get", "{}"))!;

		Assert.Equal("default", (string?)json["profileId"]);
		Assert.Equal("ok", (string?)json["state"]);
		Assert.Equal(37, (double?)json["fiveHour"]!["utilization"]);
		Assert.False((bool?)json["fiveHour"]!["isStale"]);
		Assert.NotNull(json["sevenDay"]!["resetsAtUtc"]);
		Assert.NotNull(json["fetchedAtUtc"]);
	}

	[Fact]
	public async Task TheRpcMethod_RejectsAWronglyTypedProfileId()
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => RemoteTestHelpers.CallAsync(_module, "usage.get", "{\"profileId\":3}"));
	}

	[Fact]
	public async Task NoDataAndNoFetchYet_ForAnUnavailableProfile_OmitsTheWindows()
	{
		_fetcher.Result = new UsageFetchResultModel(UsageState.Unavailable, null);

		var json = (await RemoteTestHelpers.CallAsync(_module, "usage.get", "{}"))!;

		Assert.Equal("unavailable", (string?)json["state"]);
		Assert.False(json.AsObject().ContainsKey("fiveHour"));
		Assert.False(json.AsObject().ContainsKey("fetchedAtUtc"));
	}
}
