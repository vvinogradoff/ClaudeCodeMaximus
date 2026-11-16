using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// WPF beside the Avalonia app: "Start the background daemon when none is running" is off by default (the client waits and connects when Avalonia comes up), a changed
/// setting counts at once, and an Avalonia-hosted daemon never shows the "older than the build" banner (A23 Q6 follow-up).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SideBySideTests : IDisposable
{
	private sealed class FakeProcess : IHostProcess
	{
		public bool HasExited => false;

		public void Dispose()
		{
		}
	}

	private sealed class CountingStarter : IHostProcessStarter
	{
		public int Starts { get; private set; }

		public IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
		{
			Starts++;
			return new FakeProcess();
		}
	}

	private sealed class Absent : IHostPresence
	{
		public bool IsHostRunning() => false;
	}

	private static readonly DaemonDiscoveryResult Found = new(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 1));

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-sbs-" + Guid.NewGuid().ToString("N"));
	private readonly CountingStarter _starter = new();
	private readonly ClientSettingsStore _store;

	public SideBySideTests()
	{
		var host = Path.Combine(_root, "run", "host");
		Directory.CreateDirectory(host);
		File.WriteAllText(Path.Combine(host, "TessynDesktop.exe"), "exe");
		_store = SettingsTestSupport.NewStore(new MemoryClientState());
	}

	public void Dispose() => TestDirectory.Delete(_root);

	private DaemonHostLauncher Launcher() =>
		new(Path.Combine(_root, "run"), null, _starter, new Absent(), TimeProvider.System, TimeSpan.FromSeconds(60), NullLogger.Instance, isEnabled: ServiceRegistration.IsHostStartEnabled(_store));

	private ConnectionViewModel Connection(ScriptedLocator locator, IDaemonHostLauncher launcher) =>
		new(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), launcher);

	[Fact]
	public void TheSettingIsOffByDefault()
	{
		Assert.False(_store.Get<DaemonHostSettings>().StartBackgroundDaemon);
		Assert.False(ServiceRegistration.IsHostStartEnabled(_store)());
	}

	[Theory]
	[InlineData(DaemonDiscoveryStatus.NoDaemonFile)]
	[InlineData(DaemonDiscoveryStatus.StaleFile)]
	[InlineData(DaemonDiscoveryStatus.InvalidFile)]
	public async Task WithTheSettingOff_NoHostIsStarted_AndTheLauncherSaysWhy(DaemonDiscoveryStatus status)
	{
		var outcome = await Launcher().EnsureStartedAsync(status, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.Disabled, outcome);
		Assert.Equal(0, _starter.Starts);
	}

	[Fact]
	public async Task TurningTheSettingOn_StartsTheHostAtOnce_AndOffStopsItAgain()
	{
		var launcher = Launcher();
		_store.Update<DaemonHostSettings>(s => s.StartBackgroundDaemon = true);

		Assert.Equal(HostLaunchOutcome.Started, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));
		Assert.Equal(1, _starter.Starts);

		_store.Update<DaemonHostSettings>(s => s.StartBackgroundDaemon = false);
		Assert.Equal(HostLaunchOutcome.Disabled, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));
		Assert.Equal(1, _starter.Starts);
	}

	[Fact]
	public async Task AFoundDaemon_IsNeverTouched_WhateverTheSetting()
	{
		_store.Update<DaemonHostSettings>(s => s.StartBackgroundDaemon = true);

		Assert.Equal(HostLaunchOutcome.NotNeeded, await Launcher().EnsureStartedAsync(DaemonDiscoveryStatus.Found, CancellationToken.None));
		Assert.Equal(HostLaunchOutcome.NotNeeded, await Launcher().EnsureStartedAsync(DaemonDiscoveryStatus.TokenUnavailable, CancellationToken.None));
		Assert.Equal(0, _starter.Starts);
	}

	[Fact]
	public async Task Connection_WaitsWithTheWaitingBanner_ThenConnectsWhenTheDaemonAppears()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		locator.Results.Enqueue(Found);
		var connection = Connection(locator, Launcher());
		var states = new List<ConnectionBannerState>();
		connection.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConnectionViewModel.State)) states.Add(connection.State); };

		await connection.RunAsync(CancellationToken.None);

		Assert.Contains(ConnectionBannerState.WaitingForTessyn, states);
		Assert.DoesNotContain(ConnectionBannerState.StartingDaemon, states);
		Assert.Equal(ConnectionBannerState.Connected, connection.State);
		Assert.Equal(0, _starter.Starts);
	}

	[Fact]
	public async Task TheWaitingBanner_SaysWaitingForTessynToStart()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var connection = Connection(locator, Launcher());
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

		await connection.RunAsync(cts.Token);

		Assert.Equal(ConnectionBannerState.WaitingForTessyn, connection.State);
		Assert.True(connection.IsBannerVisible);
		Assert.Equal("Waiting for Tessyn to start…", connection.BannerText);
	}

	[Fact]
	public async Task WaitingForTessyn_NeverTimesOutIntoAnError()
	{
		// The "Starting..." banner gives up after 45 s; waiting for Avalonia is open-ended and must keep its own banner.
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var connection = Connection(locator, Launcher());
		connection.StartingTimeout = TimeSpan.Zero;
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

		await connection.RunAsync(cts.Token);

		Assert.Equal(ConnectionBannerState.WaitingForTessyn, connection.State);
	}

	[Fact]
	public async Task TurningTheSettingOnWhileWaiting_StartsTheHostOnTheNextRound()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var connection = Connection(locator, Launcher());
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
		await connection.RunAsync(cts.Token);
		Assert.Equal(0, _starter.Starts);

		_store.Update<DaemonHostSettings>(s => s.StartBackgroundDaemon = true);
		using var again = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
		await connection.RunAsync(again.Token);

		Assert.Equal(1, _starter.Starts);
		Assert.Equal(ConnectionBannerState.StartingDaemon, connection.State);
	}

	[Fact]
	public async Task TheStartupSection_SavesTheSetting_AndTheLauncherFollows()
	{
		var section = new StartupSectionViewModel(_store);
		Assert.False(section.StartBackgroundDaemon);
		Assert.False(section.HasChanges);

		section.StartBackgroundDaemon = true;
		Assert.True(section.HasChanges);
		Assert.True(await section.SaveAsync());

		Assert.True(_store.Get<DaemonHostSettings>().StartBackgroundDaemon);
		Assert.False(section.HasChanges);
		Assert.Equal(HostLaunchOutcome.Started, await Launcher().EnsureStartedAsync(DaemonDiscoveryStatus.StaleFile, CancellationToken.None));
	}

	[Fact]
	public async Task TheStartupSection_ReloadDropsEdits()
	{
		var section = new StartupSectionViewModel(_store);
		section.StartBackgroundDaemon = true;

		await section.ReloadAsync();

		Assert.False(section.StartBackgroundDaemon);
	}

	// ── an Avalonia-hosted daemon is not ours to restart ─────────────────────────────────────

	private sealed class FolderProbe(string folder, DateTime build) : IDaemonBuildProbe
	{
		public RunningDaemonBuild? Inspect(int pid) => new(folder, build);
	}

	private sealed class NoDialogs : IDialogService
	{
		public void CancelPending()
		{
		}

		public Task<bool> ConfirmAsync(ConfirmDialogRequest request) => Task.FromResult(false);

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	private sealed class Idle : IHostExitSignal, IProcessExitWaiter, IClientRestarter
	{
		public bool Signal() => false;

		public Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

		public void RestartClient()
		{
		}
	}

	[Fact]
	public async Task AnAvaloniaHostedDaemon_NeverShowsTheStaleBanner_EvenWithANewerBuild()
	{
		// A newer build exists under the source location, the daemon runs from [Publish] (Avalonia's own run folder), not from <run folder>\host.
		var build = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
		var source = Path.Combine(_root, "src");
		var buildDir = Path.Combine(source, "code", "TessynDesktop", "bin", "Debug", "net10.0-windows10.0.17763.0");
		Directory.CreateDirectory(buildDir);
		File.WriteAllText(Path.Combine(buildDir, "TessynDesktop.dll"), "dll");
		File.SetLastWriteTimeUtc(Path.Combine(buildDir, "TessynDesktop.dll"), build);
		var idle = new Idle();
		var hostFolder = Path.Combine(_root, "run", "host");

		DaemonRestarter Restarter(string runningFolder) =>
			new(source, new FolderProbe(runningFolder, build.AddHours(-5)), new NoDialogs(), idle, idle, idle, TimeSpan.FromSeconds(1), NullLogger.Instance, hostFolder);

		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var avalonia = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), null, Restarter(Path.Combine(_root, "[Publish]")));
		await avalonia.RunAsync(CancellationToken.None);
		await Task.Delay(300);
		Assert.False(avalonia.IsDaemonOlder);

		// The same daemon in the WPF host folder is ours: the banner (and its Restart button) appears.
		var ours = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), null, Restarter(hostFolder));
		locator.Results.Enqueue(Found);
		await ours.RunAsync(CancellationToken.None);
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!ours.IsDaemonOlder && DateTime.UtcNow < deadline)
			await Task.Delay(20);

		Assert.True(ours.IsDaemonOlder);
	}
}
