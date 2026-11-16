using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The status bar's model line and usage meters (W10a, FR.18).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StatusBarViewModelTests : IDisposable
{
	private static readonly DateTimeOffset Reset = new(2026, 11, 16, 14, 5, 0, TimeSpan.Zero);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-statusbar-" + Guid.NewGuid().ToString("N"));
	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();

	public void Dispose() => TestDirectory.Delete(_root);

	private static UsageResult Ok(string profile, double five, double seven, bool stale = false) =>
		new(profile, profile, UsageState.Ok, new UsageWindowDto(five, Reset, stale), new UsageWindowDto(seven, Reset.AddDays(2), stale), Reset);

	private async Task<(MainViewModel Main, StatusBarViewModel Bar)> StartAsync()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "Top"))];
		_client.Sessions["s1"] = FakeSession.WithEntries(3, "Session s1");
		_client.Catalog = new CatalogResult(
			[new ModelOptionDto("default", "Default", null), new ModelOptionDto("claude-opus-5-5", "Opus 5.5", ModelProvider.Anthropic), new ModelOptionDto("qwen3", "Qwen 3", ModelProvider.Ollama)],
			["default"], [new ProfileOptionDto("default", "Default"), new ProfileOptionDto("p2", "Work")]);

		var store = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(10));
		await store.LoadAsync();
		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var main = new MainViewModel(
			connection,
			c => new TreeViewModel(c, ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			store,
			NullLogger<MainViewModel>.Instance);

		var bar = new StatusBarViewModel(connection, main, ui, NullLogger<StatusBarViewModel>.Instance);
		await main.StartAsync(CancellationToken.None);
		await main.Tree!.WhenIdleAsync();
		await main.Session!.InitializeAsync();
		return (main, bar);
	}

	[Fact]
	public async Task OnConnect_ReadsTheDefaultProfilesUsage_AndShowsBothMeters()
	{
		_client.Usage["default"] = Ok("default", 37, 12);

		var (_, bar) = await StartAsync();

		Assert.Contains("default", _client.UsageRequests);
		Assert.True(bar.HasUsage);
		Assert.Equal(37, bar.FiveHourPercent);
		Assert.Equal(12, bar.SevenDayPercent);
		Assert.StartsWith("5h: 37%  resets ", bar.FiveHourLabel);
		Assert.StartsWith("7d: 12%  resets ", bar.SevenDayLabel);
		Assert.Equal(string.Empty, bar.UsageNote);
	}

	[Fact]
	public async Task StaleReading_ShowsThePercentageOnly()
	{
		_client.Usage["default"] = Ok("default", 80, 55, stale: true);

		var (_, bar) = await StartAsync();

		Assert.Equal("5h: 80%", bar.FiveHourLabel);
		Assert.Equal("7d: 55%", bar.SevenDayLabel);
	}

	[Fact]
	public async Task UsageChanged_ForTheShownProfile_RefreshesTheMeters()
	{
		_client.Usage["default"] = Ok("default", 10, 5);
		var (_, bar) = await StartAsync();

		_client.Usage["default"] = Ok("default", 64, 21);
		_client.RaiseNotification(RemoteProtocol.Events.UsageChanged, new UsageChangedPayload("default"));

		Assert.Equal(64, bar.FiveHourPercent);
		Assert.Equal(21, bar.SevenDayPercent);
	}

	[Fact]
	public async Task UsageChanged_ForAnotherProfile_AsksNothing()
	{
		_client.Usage["default"] = Ok("default", 10, 5);
		var (_, bar) = await StartAsync();
		var before = _client.UsageRequests.Count;

		_client.RaiseNotification(RemoteProtocol.Events.UsageChanged, new UsageChangedPayload("p2"));

		Assert.Equal(before, _client.UsageRequests.Count);
		Assert.Equal(10, bar.FiveHourPercent);
	}

	[Fact]
	public async Task Resync_ReadsTheUsageAgain()
	{
		_client.Usage["default"] = Ok("default", 10, 5);
		var (_, bar) = await StartAsync();

		_client.Usage["default"] = Ok("default", 90, 40);
		_client.RaiseResync();

		Assert.Equal(90, bar.FiveHourPercent);
	}

	[Fact]
	public async Task OpeningASessionOfAnotherProfile_SwitchesTheMetersToThatProfile()
	{
		_client.Usage["default"] = Ok("default", 10, 5);
		_client.Usage["p2"] = Ok("p2", 71, 33);
		var (main, bar) = await StartAsync();

		await main.Session!.OpenAsync("s1", "Top");
		main.Session.SelectedProfile = main.Session.Profiles.Single(p => p.Id == "p2");

		Assert.Equal("p2", bar.ProfileId);
		Assert.Equal(71, bar.FiveHourPercent);
		Assert.Equal(33, bar.SevenDayPercent);
	}

	[Theory]
	[InlineData(UsageState.Unauthorized, "Usage: sign in again")]
	[InlineData(UsageState.Unavailable, "Usage unavailable")]
	public async Task WithoutAReading_TheMetersHide_AndANoteSaysWhy(UsageState state, string note)
	{
		_client.Usage["default"] = new UsageResult("default", "default", state, null, null, null);

		var (_, bar) = await StartAsync();

		Assert.False(bar.HasUsage);
		Assert.Equal(note, bar.UsageNote);
		Assert.True(bar.HasNote);
	}

	[Fact]
	public async Task Pending_HidesTheMeters_WithoutANote()
	{
		var (_, bar) = await StartAsync();

		Assert.False(bar.HasUsage);
		Assert.False(bar.HasNote);
	}

	[Fact]
	public async Task ModelLine_FollowsTheSelectedModel_AndIsEmptyWithoutASession()
	{
		var (main, bar) = await StartAsync();
		Assert.Equal(string.Empty, bar.ModelText);

		await main.Session!.OpenAsync("s1", "Top");
		main.Session.SelectedModel = main.Session.Models.Single(m => m.Id == "claude-opus-5-5");
		Assert.Equal("claude-opus-5-5", bar.ModelText);

		main.Session.SelectedModel = main.Session.Models.Single(m => m.Id == "qwen3");
		Assert.Equal("qwen3  ·  local", bar.ModelText);

		main.Session.SelectedModel = main.Session.Models.Single(m => m.Id == "default");
		Assert.Equal(string.Empty, bar.ModelText);
	}

	[Fact]
	public void ModelText_NamesThePriceWhenTheDaemonGaveOne()
	{
		Assert.Equal("claude-opus-5-5  ·  in $5.00 / out $25.00 per 1M", StatusBarText.Model("claude-opus-5-5", ModelProvider.Anthropic, 5, 25));
		Assert.Equal("claude-opus-5-5", StatusBarText.Model("claude-opus-5-5", ModelProvider.Anthropic, 0, 0));
		Assert.Equal("qwen3  ·  local", StatusBarText.Model("qwen3", ModelProvider.Ollama, 5, 25));
		Assert.Equal(string.Empty, StatusBarText.Model(null, null));
	}
}
