using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.33.10 in the status bar (TC.CX.23): the left section of a GPT model, the bars labelled from the ChatGPT plan's own windows, a missing window
/// hidden, and an exhausted window in the limits-error colour with a tooltip that says why.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexStatusBarTests : IDisposable
{
	private static readonly DateTimeOffset Reset = new(2026, 11, 16, 14, 5, 0, TimeSpan.Zero);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-codex-statusbar-" + Guid.NewGuid().ToString("N"));
	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();

	public void Dispose() => TestDirectory.Delete(_root);

	private static UsageResult Codex(UsageWindowDto? primary, UsageWindowDto? secondary, string? exhausted = null) =>
		new(CodexCatalogs.ChatGptProfile, CodexCatalogs.ChatGptName, UsageState.Ok, primary, secondary, Reset, RemoteProtocol.Engines.Codex, exhausted);

	private async Task<(MainViewModel Main, StatusBarViewModel Bar)> StartAsync()
	{
		_client.Directories = [_b.Directory("d1", "Alpha", [], _b.Session("s1", "G"))];
		_client.Sessions["s1"] = FakeSession.WithEntries(3, "G");
		_client.Catalog = CodexCatalogs.FixtureC();

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

	private static async Task SelectGptAsync(MainViewModel main, StatusBarViewModel bar)
	{
		await main.Session!.OpenAsync("s1", "G");
		main.Session.SelectedModel = main.Session.Models.Single(m => m.Id == "gpt-x-sol");
		await bar.RefreshAsync();
	}

	[Fact]
	public async Task AGptModel_ShowsItsNameAndTheChatGptPlan_AndTheMetersOfItsChatGptProfile()
	{
		_client.Usage[CodexCatalogs.ChatGptProfile] = Codex(new UsageWindowDto(12, Reset, false, "5h", 300), new UsageWindowDto(3, Reset.AddDays(3), false, "7d", 10080));
		var (main, bar) = await StartAsync();

		await SelectGptAsync(main, bar);

		Assert.Equal("GPT-X Sol  ·  ChatGPT plan", bar.ModelText);
		Assert.Equal(CodexCatalogs.ChatGptProfile, bar.ProfileId);
		Assert.Contains(CodexCatalogs.ChatGptProfile, _client.UsageRequests);
		Assert.True(bar.HasUsage);
		Assert.True(bar.HasFiveHour);
		Assert.True(bar.HasSevenDay);
		Assert.StartsWith("5h: 12%  resets ", bar.FiveHourLabel);
		Assert.StartsWith("7d: 3%  resets ", bar.SevenDayLabel);
		Assert.False(bar.IsFiveHourExhausted);
		Assert.Null(bar.FiveHourToolTip);
	}

	[Fact]
	public async Task APlanWithoutAShortWindow_HidesThatBar_AndShowsTheOther()
	{
		_client.Usage[CodexCatalogs.ChatGptProfile] = Codex(null, new UsageWindowDto(40, Reset.AddDays(3), false, "7d", 10080));
		var (main, bar) = await StartAsync();

		await SelectGptAsync(main, bar);

		Assert.True(bar.HasUsage);
		Assert.False(bar.HasFiveHour);
		Assert.True(bar.HasSevenDay);
		Assert.Equal(40, bar.SevenDayPercent);
	}

	[Fact]
	public async Task TheLabelsComeFromTheWindows_NotFromTheirSlot()
	{
		_client.Usage[CodexCatalogs.ChatGptProfile] = Codex(new UsageWindowDto(50, Reset, false, "3h", 180), new UsageWindowDto(10, Reset.AddDays(1), false, "2d", 2880));
		var (main, bar) = await StartAsync();

		await SelectGptAsync(main, bar);

		Assert.Equal(string.Format(CultureInfo.CurrentCulture, "3h: 50%  resets {0:HH:mm}", Reset.ToLocalTime()), bar.FiveHourLabel);
		Assert.Equal(string.Format(CultureInfo.CurrentCulture, "2d: 10%  resets {0:ddd}", Reset.AddDays(1).ToLocalTime()), bar.SevenDayLabel);
	}

	[Fact]
	public async Task AnExhaustedWindow_IsMarked_AndItsTooltipSaysWhy()
	{
		_client.Usage[CodexCatalogs.ChatGptProfile] = Codex(
			new UsageWindowDto(100, Reset, false, "5h", 300, IsExhausted: true), new UsageWindowDto(60, Reset.AddDays(3), false, "7d", 10080), exhausted: "usage_limit_reached");
		var (main, bar) = await StartAsync();

		await SelectGptAsync(main, bar);

		Assert.True(bar.IsFiveHourExhausted);
		Assert.False(bar.IsSevenDayExhausted);
		Assert.Contains("usage_limit_reached", bar.FiveHourToolTip);
		Assert.Null(bar.SevenDayToolTip);
	}

	[Fact]
	public void ClaudeLabels_AreUnchanged_WithAndWithoutTheNewMembers()
	{
		var old = new UsageWindowDto(37, Reset, false);
		var labelled = new UsageWindowDto(37, Reset, false, "5h", 300);

		Assert.Equal(string.Format(CultureInfo.CurrentCulture, "5h: 37%  resets {0:HH:mm}", Reset.ToLocalTime()), StatusBarText.FiveHour(old));
		Assert.Equal(StatusBarText.FiveHour(old), StatusBarText.FiveHour(labelled));
		Assert.Equal(string.Format(CultureInfo.CurrentCulture, "7d: 37%  resets {0:ddd}", Reset.ToLocalTime()), StatusBarText.SevenDay(old));
		Assert.Equal("7d: 37%", StatusBarText.SevenDay(old with { IsStale = true }));
	}
}
