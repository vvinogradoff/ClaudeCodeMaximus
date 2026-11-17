using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.9.13 / TC.RD.01 to TC.RD.04: the closed Recent dropdown shows the open session's recency colour behind its name, switchable in Settings, Appearance ("Show recency colour in
/// the closed Recent dropdown", on by default) and applied live. The tests drive the real button with real layout, the settings tab, and the host that follows the open session.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentDropdownRecencyTests : IDisposable
{
	private static readonly Color Tier1 = Color.FromRgb(0x11, 0x99, 0x33);
	private static readonly Color Tier4 = Color.FromRgb(0x99, 0x77, 0x11);

	private readonly FakeTessynClient _client = new();
	private readonly ImmediateUiDispatcher _ui = new();
	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly MainViewModel _main;
	private readonly SessionListsHost _host;
	private readonly ListDisplayService _display;

	public RecentDropdownRecencyTests()
	{
		var now = DateTimeOffset.UtcNow;
		var b = new TreeBuilder();
		TreeSessionDto Session(string id, string name, TimeSpan? age) => b.SessionWith(
			new SessionStateDto(id, false, false, null, false, age == null ? null : now - age, age == null ? null : now - age, false, null), name);
		_client.Directories = [b.Directory("d1", "Repo", [], Session("fresh", "Fresh one", TimeSpan.FromMinutes(2)), Session("old", "Old one", TimeSpan.FromDays(3)), Session("quiet", "Quiet one", TimeSpan.FromHours(3)))];
		foreach (var id in new[] { "fresh", "old", "quiet" })
			_client.Sessions[id] = FakeSession.WithEntries(2, "Session " + id);

		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), _ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		_main = new MainViewModel(
			connection,
			c => new TreeViewModel(c, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), _ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			new ChromeMemoryState(),
			NullLogger<MainViewModel>.Instance);
		_display = new ListDisplayService(_store, _ui);
		_display.Start();
		_host = new SessionListsHost(connection, _main, _ui, NullLoggerFactory.Instance, display: _display);
		_ = _main.StartAsync(CancellationToken.None);
	}

	public void Dispose()
	{
		_host.Recent.Dispose();
		_host.Dispose();
	}

	private sealed class ChromeMemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private static ToggleButton Layout(RecentDropdown dropdown)
	{
		dropdown.Resources["Colour.RecentTier1"] = Frozen(Tier1);
		dropdown.Resources["Colour.RecentTier4"] = Frozen(Tier4);
		dropdown.Measure(new Size(700, 40));
		dropdown.Arrange(new Rect(0, 0, 700, 40));
		dropdown.UpdateLayout();
		return (ToggleButton)dropdown.FindName("Toggle")!;
	}

	private static SolidColorBrush Frozen(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	private static Color? BackgroundOf(ToggleButton toggle) => (toggle.Background as SolidColorBrush)?.Color;

	[Fact]
	public void TheSetting_IsOnByDefault_AndAnOlderFileWithoutTheKeyReadsAsOn()
	{
		Assert.True(new AppearanceSettings().ShowRecencyInRecentDropdown);
		Assert.True(new AppearanceSectionViewModel(_store).ShowRecencyInRecentDropdown);
		Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppearanceSettings>("{\"theme\":\"Light\",\"tagFontSize\":12}", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.ShowRecencyInRecentDropdown);
	}

	[Fact]
	public async Task TheSettingsCheckbox_Saves_IsAChange_AndComesBackOnReload()
	{
		var section = new AppearanceSectionViewModel(_store);
		Assert.False(section.HasChanges);

		section.ShowRecencyInRecentDropdown = false;
		Assert.True(section.HasChanges);
		Assert.True(await section.SaveAsync());
		Assert.False(_store.Get<AppearanceSettings>().ShowRecencyInRecentDropdown);
		Assert.False(section.HasChanges);

		var reopened = new AppearanceSectionViewModel(_store);
		Assert.False(reopened.ShowRecencyInRecentDropdown);
	}

	[Fact]
	public void TheClosedButton_TakesTheOpenSessionsRecencyTint_AndOnlyWhileTheSettingIsOn()
	{
		_main.Tree!.SelectNodeById("fresh");
		Assert.Equal(4, _host.Recent.RecencyLevel);

		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var toggle = Layout(dropdown);
			Assert.Equal(Tier1, BackgroundOf(toggle));   // level 4 = the newest tier (Colour.RecentTier1)

			_store.Update<AppearanceSettings>(a => a.ShowRecencyInRecentDropdown = false);   // live: no reopening, no restart
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RecencyLevel);
			Assert.NotEqual(Tier1, BackgroundOf(toggle));

			_store.Update<AppearanceSettings>(a => a.ShowRecencyInRecentDropdown = true);
			dropdown.UpdateLayout();
			Assert.Equal(Tier1, BackgroundOf(toggle));
		});
	}

	[Fact]
	public void TheTint_FollowsTheOpenSession_AndIsPlainForASessionWithoutRecentActivity()
	{
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var toggle = Layout(dropdown);

			_main.Tree!.SelectNodeById("quiet");   // 3 hours ago: tier 2 of the row colours, which this test does not define
			Assert.Equal(2, _host.Recent.RecencyLevel);

			_main.Tree.SelectNodeById("old");   // three days ago
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RecencyLevel);
			Assert.NotEqual(Tier1, BackgroundOf(toggle));

			_main.Tree.SelectNodeById("fresh");
			dropdown.UpdateLayout();
			Assert.Equal(Tier1, BackgroundOf(toggle));

			_main.Session!.Close();
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RecencyLevel);
		});
	}

	[Fact]
	public void TheScreensaverPicture_FillsTheScreen_KeepingTheAspectRatioAndCropping()
	{
		StaTestRunner.Run(() =>
		{
			var overlay = new ScreensaverOverlay();
			var picture = (Image)overlay.FindName("Picture")!;
			Assert.Equal(Stretch.UniformToFill, picture.Stretch);
		});
	}
}
