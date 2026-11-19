using System.ComponentModel;
using System.Text.Json;
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
/// The daemon elevated while this client runs normally (FR.32.10–FR.32.14; TC.HS.23–TC.HS.29): the setting, the elevated start and its fallback, the two
/// restart buttons, the stale banner keeping the daemon's way, the status and the banner, and reading an elevated host's folder.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonElevationTests : IDisposable
{
	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class FakeProcess : IHostProcess
	{
		public bool HasExited { get; set; }

		public void Dispose()
		{
		}
	}

	/// <summary>Records each start with its way; an elevated start can be made to throw (a declined prompt) or to return nothing.</summary>
	private sealed class ModeStarter : IHostProcessStarter
	{
		private readonly List<string> _events;

		public ModeStarter(List<string> events) => _events = events;

		public List<HostStartMode> Starts { get; } = [];

		public List<FakeProcess> Processes { get; } = [];

		public Exception? ElevatedThrows { get; set; }

		public bool ElevatedReturnsNoProcess { get; set; }

		public IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, HostStartMode mode)
		{
			Starts.Add(mode);
			_events.Add("start:" + mode);
			if (mode == HostStartMode.Elevated && ElevatedThrows != null)
				throw ElevatedThrows;
			if (mode == HostStartMode.Elevated && ElevatedReturnsNoProcess)
				return null;

			var process = new FakeProcess();
			Processes.Add(process);
			return process;
		}
	}

	private sealed class FakePresence : IHostPresence
	{
		public bool Running { get; set; }

		public bool IsHostRunning() => Running;
	}

	private sealed class FakeSignal : IHostExitSignal
	{
		public bool Listening { get; set; } = true;

		public int Signals { get; private set; }

		public bool Signal()
		{
			Signals++;
			return Listening;
		}
	}

	private sealed class FakeWaiter : IProcessExitWaiter
	{
		public bool Exits { get; set; } = true;

		public int Waits { get; private set; }

		public Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken)
		{
			Waits++;
			return Task.FromResult(Exits);
		}
	}

	private sealed class FakeClientRestarter : IClientRestarter
	{
		private readonly List<string> _events;

		public FakeClientRestarter(List<string> events) => _events = events;

		public int Restarts { get; private set; }

		public void RestartClient()
		{
			Restarts++;
			_events.Add("client-restart");
		}
	}

	private sealed class Dialogs : IDialogService
	{
		public List<ConfirmDialogRequest> Confirms { get; } = [];

		public bool Answer { get; set; } = true;

		public void CancelPending()
		{
		}

		public Task<bool> ConfirmAsync(ConfirmDialogRequest request)
		{
			Confirms.Add(request);
			return Task.FromResult(Answer);
		}

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	/// <summary>A launcher that records the restart starts and can report a refusal.</summary>
	private sealed class RecordingLauncher : IDaemonHostLauncher
	{
		private readonly List<string> _events;

		public RecordingLauncher(List<string> events) => _events = events;

		public event Action<ElevationRefusal>? ElevationRefused;

		public List<HostStartMode> StartNow { get; } = [];

		public HostLaunchOutcome Outcome { get; set; } = HostLaunchOutcome.Started;

		public Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken) => Task.FromResult(HostLaunchOutcome.NotNeeded);

		public Task<HostLaunchOutcome> StartNowAsync(HostStartMode mode, CancellationToken cancellationToken)
		{
			StartNow.Add(mode);
			_events.Add("start-now:" + mode);
			return Task.FromResult(Outcome);
		}

		public void Refuse(ElevationRefusal refusal) => ElevationRefused?.Invoke(refusal);
	}

	/// <summary>The connection's daemon control as the Startup tab sees it.</summary>
	private sealed class FakeDaemonControl : IDaemonModeControl
	{
		private bool? _elevated;

		public event PropertyChangedEventHandler? PropertyChanged;

		public bool? DaemonElevated
		{
			get => _elevated;
			set
			{
				_elevated = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DaemonElevated)));
			}
		}

		public bool CanRestartDaemon { get; set; } = true;

		public string? RestartNote { get; set; }

		public List<HostStartMode> Switches { get; } = [];

		public Task SwitchDaemonAsync(HostStartMode mode)
		{
			Switches.Add(mode);
			return Task.CompletedTask;
		}
	}

	private static readonly DaemonDiscoveryResult Found = new(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 4321));

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-elevation-" + Guid.NewGuid().ToString("N"));
	private readonly List<string> _events = [];
	private readonly ModeStarter _starter;
	private readonly FakePresence _presence = new();
	private readonly ManualTime _time = new();
	private readonly FakeSignal _signal = new();
	private readonly FakeWaiter _waiter = new();
	private readonly FakeClientRestarter _clientRestarter;
	private readonly Dialogs _dialogs = new();
	private readonly RecordingLauncher _launcher;

	public DaemonElevationTests()
	{
		_starter = new ModeStarter(_events);
		_clientRestarter = new FakeClientRestarter(_events);
		_launcher = new RecordingLauncher(_events);
		Directory.CreateDirectory(Path.Combine(RunFolder, "host"));
		File.WriteAllText(Path.Combine(RunFolder, "host", "TessynDesktop.exe"), "exe");
		var build = Path.Combine(Source, "code", "TessynDesktop", "bin", "Debug", "net10.0-windows10.0.17763.0");
		Directory.CreateDirectory(build);
		File.WriteAllText(Path.Combine(build, "TessynDesktop.dll"), "dll");
	}

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	private string RunFolder => Path.Combine(_root, "[Publish-WPF]");

	private string Source => Path.Combine(_root, "src");

	private BuildCopyResult RecordingCopier(string buildDir, string hostFolder)
	{
		_events.Add("copy");
		return new BuildCopyResult(1, 0, 0);
	}

	private DaemonHostLauncher Launcher(bool elevated = false, bool enabled = true, Func<string, string, BuildCopyResult>? copier = null) =>
		new(RunFolder, Source, _starter, _presence, _time, TimeSpan.FromSeconds(60), NullLogger.Instance, copier ?? RecordingCopier, () => enabled, () => elevated);

	private DaemonRestarter Restarter(IDaemonHostLauncher? launcher = null) =>
		new(Source, new ProcessDaemonBuildProbe(), _dialogs, _signal, _waiter, _clientRestarter, TimeSpan.FromSeconds(7), NullLogger.Instance, launcher: launcher ?? _launcher);

	private static ClientSettingsStore Store(bool startElevated)
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		store.Update<DaemonHostSettings>(s => s.StartDaemonElevated = startElevated);
		return store;
	}

	private static HelloResult Hello(bool? elevated, string run = "run") => new(1, "test", "machine", run, DateTimeOffset.UtcNow, [], 1, 0, elevated);

	private static async Task<ConnectionViewModel> ConnectedAsync(FakeTessynClient client, IClientSettingsStore store, IDaemonHostLauncher? launcher = null, DaemonRestarter? restarter = null)
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(client), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance,
			TimeSpan.FromMilliseconds(5), launcher, restarter, store);
		await connection.RunAsync(CancellationToken.None);
		return connection;
	}

	// ── TC.HS.23: the setting ─────────────────────────────────────────────────

	[Fact]
	public void Setting_IsOffByDefault_AndAnOlderSettingsFileWithoutItReadsAsOff()
	{
		Assert.False(new DaemonHostSettings().StartDaemonElevated);
		var older = JsonSerializer.Deserialize<DaemonHostSettings>("""{"startBackgroundDaemon":false}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		Assert.False(older.StartBackgroundDaemon);
		Assert.False(older.StartDaemonElevated);
		Assert.False(new StartupSectionViewModel(Store(startElevated: false)).StartDaemonElevated);
	}

	[Fact]
	public async Task Setting_IsSavedInTheDaemonHostSection_AndAnUnsavedChangeCountsForTheCloseGuard()
	{
		var store = Store(startElevated: false);
		var section = new StartupSectionViewModel(store);
		Assert.False(section.HasChanges);

		section.StartDaemonElevated = true;
		Assert.True(section.HasChanges);   // the Settings close guard (FR.4.7) asks about it

		Assert.True(await section.SaveAsync());
		Assert.True(store.Get<DaemonHostSettings>().StartDaemonElevated);
		Assert.True(store.Get<DaemonHostSettings>().StartBackgroundDaemon);   // the other setting is kept
		Assert.False(section.HasChanges);
	}

	// ── TC.HS.24: the elevated start ──────────────────────────────────────────

	[Fact]
	public async Task Launcher_WithTheSettingOn_CopiesTheNewestBuild_ThenStartsTheHostThroughTheAdministratorPrompt()
	{
		var outcome = await Launcher(elevated: true).EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.Started, outcome);
		Assert.Equal(["copy", "start:Elevated"], _events);
	}

	[Fact]
	public async Task Launcher_WithTheSettingOff_StartsTheHostNormally_AfterTheCopy()
	{
		var outcome = await Launcher(elevated: false).EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.Started, outcome);
		Assert.Equal(["copy", "start:Normal"], _events);
	}

	[Fact]
	public void ProcessHostStarter_Elevated_IsAShellStartWithTheRunAsVerbAndNoPipes_NormalIsADirectStartWithoutAWindow()
	{
		var starter = new ProcessHostStarter();

		var elevated = starter.CreateStartInfo(@"C:\run\host\TessynDesktop.exe", ["--background"], @"C:\run\host", HostStartMode.Elevated);
		Assert.True(elevated.UseShellExecute);
		Assert.Equal("runas", elevated.Verb);
		Assert.Equal("--background", elevated.Arguments);
		Assert.Equal(@"C:\run\host", elevated.WorkingDirectory);
		Assert.False(elevated.RedirectStandardInput || elevated.RedirectStandardOutput || elevated.RedirectStandardError);
		Assert.Equal("--a \"b c\" \"\"", starter.CreateStartInfo("x", ["--a", "b c", ""], "w", HostStartMode.Elevated).Arguments);

		var normal = starter.CreateStartInfo(@"C:\run\host\TessynDesktop.exe", ["--background"], @"C:\run\host", HostStartMode.Normal);
		Assert.False(normal.UseShellExecute);
		Assert.True(normal.CreateNoWindow);
		Assert.Equal(["--background"], normal.ArgumentList);
		Assert.True(string.IsNullOrEmpty(normal.Verb));
	}

	[Fact]
	public async Task StartNow_IgnoresTheStartSettingAndTheCooldown_ButNeverStartsBesideAHostThatHoldsTheGuard()
	{
		var launcher = Launcher(enabled: false);
		Assert.Equal(HostLaunchOutcome.Disabled, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));

		Assert.Equal(HostLaunchOutcome.Started, await launcher.StartNowAsync(HostStartMode.Elevated, CancellationToken.None));
		Assert.Equal(["copy", "start:Elevated"], _events);

		_presence.Running = true;
		Assert.Equal(HostLaunchOutcome.HostPresent, await launcher.StartNowAsync(HostStartMode.Normal, CancellationToken.None));
		Assert.Equal([HostStartMode.Elevated], _starter.Starts);
	}

	[Fact]
	public async Task StartNow_WhileItCopies_TheConnectLoopStartsNoHost()
	{
		using var copying = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var launcher = Launcher(copier: (_, _) =>
		{
			copying.Set();
			release.Wait(TimeSpan.FromSeconds(10));
			return new BuildCopyResult(1, 0, 0);
		});

		var restart = launcher.StartNowAsync(HostStartMode.Elevated, CancellationToken.None);
		Assert.True(copying.Wait(TimeSpan.FromSeconds(10)));
		Assert.Equal(HostLaunchOutcome.AlreadyStarting, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));
		release.Set();

		Assert.Equal(HostLaunchOutcome.Started, await restart);
		Assert.Equal([HostStartMode.Elevated], _starter.Starts);
	}

	// ── TC.HS.25: a declined or failed prompt ─────────────────────────────────

	[Fact]
	public async Task ADeclinedPrompt_StartsTheHostNormallyWithTheNotice_AndOnlyRestartDaemonElevatedAsksAgain()
	{
		_starter.ElevatedThrows = new Win32Exception(Constants.Switch.ElevationCancelledError);
		var launcher = Launcher(elevated: true);
		var refusals = new List<ElevationRefusal>();
		launcher.ElevationRefused += refusals.Add;

		Assert.Equal(HostLaunchOutcome.Started, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));
		Assert.Equal([HostStartMode.Elevated, HostStartMode.Normal], _starter.Starts);
		var refusal = Assert.Single(refusals);
		Assert.True(refusal.Declined);
		Assert.Equal("The daemon runs normally: the administrator prompt was declined.", refusal.NoticeText);

		// The host ends; after the cool-down the connect loop starts the next one normally: the prompt does not come back by itself.
		_starter.Processes[^1].HasExited = true;
		_time.Now += TimeSpan.FromSeconds(61);
		Assert.Equal(HostLaunchOutcome.Started, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));
		Assert.Equal([HostStartMode.Elevated, HostStartMode.Normal, HostStartMode.Normal], _starter.Starts);
		Assert.Single(refusals);

		// Restart daemon elevated asks again; approved, the next automatic start may ask again too.
		_starter.ElevatedThrows = null;
		Assert.Equal(HostLaunchOutcome.Started, await launcher.StartNowAsync(HostStartMode.Elevated, CancellationToken.None));
		_starter.Processes[^1].HasExited = true;
		_time.Now += TimeSpan.FromSeconds(61);
		await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);
		Assert.Equal([HostStartMode.Elevated, HostStartMode.Normal, HostStartMode.Normal, HostStartMode.Elevated, HostStartMode.Elevated], _starter.Starts);
	}

	[Fact]
	public async Task AnElevatedShellStartWithoutAProcessHandle_CountsAsStarted_AndNoNormalHostIsStartedBesideIt()
	{
		_starter.ElevatedReturnsNoProcess = true;
		var launcher = Launcher(elevated: true);
		var refusals = 0;
		launcher.ElevationRefused += _ => refusals++;

		Assert.Equal(HostLaunchOutcome.Started, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));

		Assert.Equal([HostStartMode.Elevated], _starter.Starts);
		Assert.Equal(0, refusals);
	}

	[Fact]
	public async Task AFailedElevatedStart_StartsTheHostNormally_AndTheNoticeNamesTheReason()
	{
		_starter.ElevatedThrows = new Win32Exception(5, "Access is denied");
		var launcher = Launcher(elevated: true);
		ElevationRefusal? refusal = null;
		launcher.ElevationRefused += r => refusal = r;

		Assert.Equal(HostLaunchOutcome.Started, await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));

		Assert.Equal([HostStartMode.Elevated, HostStartMode.Normal], _starter.Starts);
		Assert.NotNull(refusal);
		Assert.False(refusal!.Declined);
		Assert.Equal("The daemon runs normally: the elevated start failed (Access is denied).", refusal.NoticeText);
	}

	// ── TC.HS.26: Restart daemon elevated and Restart daemon normal ───────────

	[Fact]
	public async Task Switch_AsksFirstNamingTheRunningSessions_AndCancelChangesNothing()
	{
		_dialogs.Answer = false;

		var outcome = await Restarter().SwitchAsync(4321, 2, HostStartMode.Elevated, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.Cancelled, outcome);
		var confirm = Assert.Single(_dialogs.Confirms);
		Assert.Equal("Restart the daemon elevated", confirm.Title);
		Assert.Equal("Restart daemon elevated", confirm.ConfirmText);
		Assert.Contains("2 active session(s) will be terminated", confirm.Message);
		Assert.True(confirm.IsDestructive);
		Assert.Equal(0, _signal.Signals);
		Assert.Empty(_launcher.StartNow);
	}

	[Theory]
	[InlineData(HostStartMode.Elevated)]
	[InlineData(HostStartMode.Normal)]
	public async Task Switch_EndsTheDaemon_StartsTheHostTheChosenWay_AndTheClientDoesNotRestart(HostStartMode mode)
	{
		var outcome = await Restarter().SwitchAsync(4321, 0, mode, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.Restarted, outcome);
		Assert.Equal(1, _signal.Signals);
		Assert.Equal(1, _waiter.Waits);
		Assert.Equal([mode], _launcher.StartNow);
		Assert.Equal(0, _clientRestarter.Restarts);
		Assert.Equal(mode == HostStartMode.Elevated ? "Restart the daemon elevated" : "Restart the daemon normally", Assert.Single(_dialogs.Confirms).Title);
	}

	[Fact]
	public async Task Switch_ADaemonThatDoesNotExitOrDoesNotListen_StartsNothing()
	{
		_waiter.Exits = false;
		Assert.Equal(DaemonRestartOutcome.DidNotExit, await Restarter().SwitchAsync(4321, 0, HostStartMode.Elevated, CancellationToken.None));

		_signal.Listening = false;
		Assert.Equal(DaemonRestartOutcome.NotSupported, await Restarter().SwitchAsync(4321, 0, HostStartMode.Elevated, CancellationToken.None));
		Assert.Empty(_launcher.StartNow);
	}

	[Fact]
	public async Task Switch_WhenNoNewHostStarts_SaysSo()
	{
		_launcher.Outcome = HostLaunchOutcome.HostMissing;

		Assert.Equal(DaemonRestartOutcome.NotStarted, await Restarter().SwitchAsync(4321, 0, HostStartMode.Normal, CancellationToken.None));
	}

	[Fact]
	public async Task Switch_WorksWithTheStartSettingOff()
	{
		// The real launcher behind the restarter, with "Start the background daemon" off: the restart still brings a host up.
		var launcher = Launcher(enabled: false);

		var outcome = await Restarter(launcher).SwitchAsync(4321, 0, HostStartMode.Normal, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.Restarted, outcome);
		Assert.Equal([HostStartMode.Normal], _starter.Starts);
	}

	// ── TC.HS.27: the stale banner keeps the daemon's way ─────────────────────

	[Theory]
	[InlineData(HostStartMode.Elevated)]
	[InlineData(HostStartMode.Normal)]
	public async Task StaleRestart_StartsTheNewHostTheWayTheDaemonRan_ThenRestartsTheClient(HostStartMode keep)
	{
		var outcome = await Restarter().RestartAsync(4321, 0, CancellationToken.None, keep);

		Assert.Equal(DaemonRestartOutcome.ClientRestarting, outcome);
		Assert.Equal(["start-now:" + keep, "client-restart"], _events);
	}

	[Fact]
	public async Task StaleRestart_FromTheBanner_KeepsAnElevatedDaemonElevated()
	{
		var client = new FakeTessynClient { ServerInfo = Hello(elevated: true) };
		var connection = await ConnectedAsync(client, Store(startElevated: false), _launcher, Restarter());

		await connection.RestartDaemonCommand.ExecuteAsync(null);

		Assert.Equal([HostStartMode.Elevated], _launcher.StartNow);
		Assert.Equal(1, _clientRestarter.Restarts);
	}

	// ── TC.HS.28: the status and the banner ───────────────────────────────────

	[Theory]
	[InlineData(false, true, true)]    // normal daemon, setting on: the banner
	[InlineData(false, false, false)]  // normal daemon, setting off: nothing
	[InlineData(true, true, false)]    // elevated daemon: nothing
	[InlineData(null, true, false)]    // an older daemon that does not say: nothing
	public async Task Banner_ShowsOnlyForANormalDaemonWithTheSettingOn(bool? elevated, bool setting, bool visible)
	{
		var connection = await ConnectedAsync(new FakeTessynClient { ServerInfo = Hello(elevated) }, Store(setting));

		Assert.Equal(elevated, connection.DaemonElevated);
		Assert.Equal(visible, connection.IsElevationBannerVisible);
		Assert.Equal("The daemon runs normally, but Start daemon elevated is on.", connection.ElevationBannerText);
	}

	[Fact]
	public async Task Banner_ComesAndGoesWithTheSetting()
	{
		var store = Store(startElevated: false);
		var connection = await ConnectedAsync(new FakeTessynClient { ServerInfo = Hello(elevated: false) }, store);
		var changes = new List<string?>();
		connection.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

		store.Update<DaemonHostSettings>(s => s.StartDaemonElevated = true);
		Assert.True(connection.IsElevationBannerVisible);
		Assert.Contains(nameof(ConnectionViewModel.IsElevationBannerVisible), changes);

		store.Update<DaemonHostSettings>(s => s.StartDaemonElevated = false);
		Assert.False(connection.IsElevationBannerVisible);
	}

	[Fact]
	public async Task Banner_AfterADeclinedPrompt_ShowsTheNoticeEvenWithTheSettingOff_AndGoesWhenTheNewRunIsElevated()
	{
		var client = new FakeTessynClient { ServerInfo = Hello(elevated: false) };
		var connection = await ConnectedAsync(client, Store(startElevated: false), _launcher);

		_launcher.Refuse(new ElevationRefusal(Declined: true, Reason: null));
		Assert.True(connection.IsElevationBannerVisible);
		Assert.Equal("The daemon runs normally: the administrator prompt was declined.", connection.ElevationBannerText);

		client.ServerInfo = Hello(elevated: true, run: "run-2");
		client.RaiseResync(ResyncedEventArgs.RunChangedReason);
		Assert.True(connection.DaemonElevated);
		Assert.False(connection.IsElevationBannerVisible);
		Assert.Null(connection.ElevationNote);
	}

	[Fact]
	public async Task Status_IsUnknownWhileTheConnectionIsDown_AndBackAfterAReconnectToTheSameRun()
	{
		var client = new FakeTessynClient { ServerInfo = Hello(elevated: true) };
		var connection = await ConnectedAsync(client, Store(startElevated: false));
		Assert.True(connection.DaemonElevated);

		client.SetState(TessynClientState.Reconnecting);
		Assert.Null(connection.DaemonElevated);

		client.SetState(TessynClientState.Connected);
		Assert.True(connection.DaemonElevated);
	}

	[Fact]
	public async Task Restart_FromTheConnection_StartsTheChosenWay_AndTheStatusIsUnknownUntilTheNewRunSays()
	{
		var client = new FakeTessynClient { ServerInfo = Hello(elevated: false) };
		var connection = await ConnectedAsync(client, Store(startElevated: true), _launcher, Restarter());
		Assert.True(connection.CanRestartDaemon);

		await connection.RestartDaemonElevatedCommand.ExecuteAsync(null);

		Assert.Equal([HostStartMode.Elevated], _launcher.StartNow);
		Assert.Null(connection.DaemonElevated);
		Assert.Equal(0, _clientRestarter.Restarts);

		client.ServerInfo = Hello(elevated: true, run: "run-2");
		client.RaiseResync(ResyncedEventArgs.RunChangedReason);
		Assert.True(connection.DaemonElevated);
		Assert.False(connection.IsElevationBannerVisible);
	}

	[Fact]
	public async Task Restart_ThatDoesNotEndTheDaemon_LeavesANote()
	{
		_waiter.Exits = false;
		var connection = await ConnectedAsync(new FakeTessynClient { ServerInfo = Hello(elevated: false) }, Store(startElevated: true), _launcher, Restarter());

		await connection.RestartDaemonNormalCommand.ExecuteAsync(null);

		Assert.Equal(Constants.Switch.RestartDidNotExitNote, connection.RestartNote);
		Assert.Empty(_launcher.StartNow);
	}

	[Fact]
	public void Startup_ShowsWhichWayTheDaemonRuns_AndFollowsIt()
	{
		var control = new FakeDaemonControl { DaemonElevated = true };
		var section = new StartupSectionViewModel(Store(startElevated: false), control);
		Assert.True(section.HasDaemonControl);
		Assert.Equal("The daemon runs elevated (as administrator).", section.DaemonStatusText);

		control.DaemonElevated = false;
		Assert.Equal("The daemon runs normally.", section.DaemonStatusText);

		control.DaemonElevated = null;
		Assert.Null(section.DaemonStatusText);

		Assert.False(new StartupSectionViewModel(Store(startElevated: false)).HasDaemonControl);
	}

	[Fact]
	public async Task Startup_TheTwoButtonsRestartTheirWay_OnlyWhenTheDaemonCanBeRestarted()
	{
		var control = new FakeDaemonControl { DaemonElevated = false };
		var section = new StartupSectionViewModel(Store(startElevated: false), control);

		await section.RestartDaemonElevatedCommand.ExecuteAsync(null);
		await section.RestartDaemonNormalCommand.ExecuteAsync(null);
		Assert.Equal([HostStartMode.Elevated, HostStartMode.Normal], control.Switches);
		Assert.False(section.HasChanges);   // the buttons act at once; they are not part of Save

		control.CanRestartDaemon = false;
		control.DaemonElevated = true;   // any change of the control re-evaluates the buttons
		Assert.False(section.RestartDaemonElevatedCommand.CanExecute(null));
		Assert.False(section.RestartDaemonNormalCommand.CanExecute(null));
	}

	// ── TC.HS.31: an elevated WPF says so ─────────────────────────────────────

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ClientBanner_ShowsOnlyWhileThisWindowRunsElevated_ForTheWholeRun_AndTheStartLogsIt(bool elevated)
	{
		var logger = new CapturingLogger<ConnectionViewModel>();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient { ServerInfo = Hello(elevated: false) }), new ImmediateUiDispatcher(), logger,
			TimeSpan.FromMilliseconds(5), clientElevated: elevated);

		Assert.Equal(elevated, connection.IsClientElevated);
		Assert.Equal("This window runs as administrator. Files can't be dropped from Explorer, and every daemon it starts runs elevated.", connection.ClientElevatedText);
		Assert.Contains(elevated ? "This window runs elevated (as administrator)" : "This window runs normally", logger.Messages);

		await connection.RunAsync(CancellationToken.None);   // connecting, and whichever way the daemon runs, changes nothing
		Assert.Equal(elevated, connection.IsClientElevated);
	}

	// ── TC.HS.29: reading an elevated host's folder ───────────────────────────

	[Fact]
	public void ProcessImagePath_ReadsTheExecutableWithTheLimitedQueryRight_AndNothingForAMissingProcess()
	{
		var path = new ProcessImagePath();

		Assert.Equal(Environment.ProcessPath, path.Get(Environment.ProcessId), StringComparer.OrdinalIgnoreCase);
		Assert.Null(path.Get(-1));
	}
}
