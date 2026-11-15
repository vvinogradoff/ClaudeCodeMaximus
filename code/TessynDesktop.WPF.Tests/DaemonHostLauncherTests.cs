using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Starting the Avalonia background host when no daemon answers, and finding it again (A23 Q6, work package W13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostLauncherTests : IDisposable
{
	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class FakeProcess : IHostProcess
	{
		public bool HasExited { get; set; }

		public void Dispose()
		{
		}
	}

	private sealed class FakeStarter : IHostProcessStarter
	{
		public List<(string Exe, string[] Args, string WorkingDirectory)> Starts { get; } = [];

		public List<FakeProcess> Processes { get; } = [];

		public bool ReturnNull { get; set; }

		public Exception? Throw { get; set; }

		public IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
		{
			if (Throw != null)
				throw Throw;

			Starts.Add((executable, [.. arguments], workingDirectory));
			if (ReturnNull)
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

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-switch-host-" + Guid.NewGuid().ToString("N"));
	private readonly FakeStarter _starter = new();
	private readonly FakePresence _presence = new();
	private readonly ManualTime _time = new();

	public DaemonHostLauncherTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	private string RunFolder => Path.Combine(_root, "[Publish-WPF]");

	private void InstallHost()
	{
		Directory.CreateDirectory(Path.Combine(RunFolder, "host"));
		File.WriteAllText(Path.Combine(RunFolder, "host", "TessynDesktop.exe"), "exe");
	}

	private DaemonHostLauncher Create(string? source = null) =>
		new(RunFolder, source, _starter, _presence, _time, TimeSpan.FromSeconds(60), NullLogger.Instance);

	[Theory]
	[InlineData(DaemonDiscoveryStatus.NoDaemonFile)]
	[InlineData(DaemonDiscoveryStatus.StaleFile)]
	[InlineData(DaemonDiscoveryStatus.InvalidFile)]
	public async Task NoDaemon_StartsTheHostWithTheBackgroundFlagInItsOwnFolder(DaemonDiscoveryStatus status)
	{
		InstallHost();

		var outcome = await Create().EnsureStartedAsync(status, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.Started, outcome);
		var start = Assert.Single(_starter.Starts);
		Assert.Equal(Path.Combine(RunFolder, "host", "TessynDesktop.exe"), start.Exe);
		Assert.Equal(["--background"], start.Args);
		Assert.Equal(Path.Combine(RunFolder, "host"), start.WorkingDirectory);
	}

	[Theory]
	[InlineData(DaemonDiscoveryStatus.Found)]
	[InlineData(DaemonDiscoveryStatus.TokenUnavailable)]
	public async Task ARunningDaemon_IsAttachedToAndNeverStartedAgain(DaemonDiscoveryStatus status)
	{
		InstallHost();

		var outcome = await Create().EnsureStartedAsync(status, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.NotNeeded, outcome);
		Assert.Empty(_starter.Starts);
	}

	[Fact]
	public async Task AHostThatIsStillStarting_IsNotStartedTwice()
	{
		InstallHost();
		var launcher = Create();

		var first = await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);
		var second = await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);
		_time.Now += TimeSpan.FromMinutes(10); // even long after the cool-down, a live host is not replaced
		var third = await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.Started, first);
		Assert.Equal(HostLaunchOutcome.AlreadyStarting, second);
		Assert.Equal(HostLaunchOutcome.AlreadyStarting, third);
		Assert.Single(_starter.Starts);
	}

	[Fact]
	public async Task AHostThatExitedAtOnce_IsRetriedOnlyAfterTheCooldown()
	{
		InstallHost();
		var launcher = Create();
		await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);
		_starter.Processes[0].HasExited = true;

		_time.Now += TimeSpan.FromSeconds(10);
		var tooSoon = await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.StaleFile, CancellationToken.None);
		_time.Now += TimeSpan.FromSeconds(61);
		var later = await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.StaleFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.AlreadyStarting, tooSoon);
		Assert.Equal(HostLaunchOutcome.Started, later);
		Assert.Equal(2, _starter.Starts.Count);
	}

	[Fact]
	public async Task AnotherHostHoldingTheGuard_IsWaitedForNotReplaced()
	{
		InstallHost();
		_presence.Running = true;

		var outcome = await Create().EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.HostPresent, outcome);
		Assert.Empty(_starter.Starts);
	}

	[Fact]
	public async Task NoHostExecutable_ReportsItInsteadOfFailing()
	{
		var outcome = await Create().EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.HostMissing, outcome);
		Assert.Empty(_starter.Starts);
	}

	[Fact]
	public async Task AProcessThatCannotBeStarted_IsAFailureNotACrash()
	{
		InstallHost();
		_starter.Throw = new System.ComponentModel.Win32Exception("blocked");
		Assert.Equal(HostLaunchOutcome.Failed, await Create().EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));

		_starter.Throw = null;
		_starter.ReturnNull = true;
		_time.Now += TimeSpan.FromMinutes(5);
		Assert.Equal(HostLaunchOutcome.Failed, await Create().EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None));
	}

	[Fact]
	public async Task TheHostFolderIsBroughtUpToDateFromTheNewestAvaloniaBuildBeforeItStarts()
	{
		var build = Path.Combine(_root, "src", "code", "TessynDesktop", "bin", "Debug", "net10.0-windows10.0.17763.0");
		Directory.CreateDirectory(Path.Combine(build, "runtimes"));
		File.WriteAllText(Path.Combine(build, "TessynDesktop.dll"), "dll");
		File.WriteAllText(Path.Combine(build, "TessynDesktop.exe"), "new exe");
		File.WriteAllText(Path.Combine(build, "runtimes", "native.dll"), "native");
		InstallHost(); // an old host exe

		var outcome = await Create(Path.Combine(_root, "src")).EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal(HostLaunchOutcome.Started, outcome);
		Assert.Equal("new exe", File.ReadAllText(Path.Combine(RunFolder, "host", "TessynDesktop.exe")));
		Assert.Equal("native", File.ReadAllText(Path.Combine(RunFolder, "host", "runtimes", "native.dll")));
	}

	[Fact]
	public async Task TheHostFolderIsNotTouchedWhileAHostRuns()
	{
		var build = Path.Combine(_root, "src", "code", "TessynDesktop", "bin", "Debug", "net10.0");
		Directory.CreateDirectory(build);
		File.WriteAllText(Path.Combine(build, "TessynDesktop.dll"), "dll");
		File.WriteAllText(Path.Combine(build, "TessynDesktop.exe"), "new exe");
		InstallHost();
		_presence.Running = true;

		await Create(Path.Combine(_root, "src")).EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);

		Assert.Equal("exe", File.ReadAllText(Path.Combine(RunFolder, "host", "TessynDesktop.exe")));
	}

	[Fact]
	public async Task TheHostFolderCopy_RunsOffTheCallersThread_AndNeverUnderTheLock()
	{
		// M4: the copy is blocking disk I/O; the call must return a pending task at once, and a second call must not wait for the copy.
		InstallHost();
		var build = Path.Combine(_root, "src", "code", "TessynDesktop", "bin", "Debug", "net10.0");
		Directory.CreateDirectory(build);
		File.WriteAllText(Path.Combine(build, "TessynDesktop.dll"), "dll");
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var copyThread = -1;
		var launcher = new DaemonHostLauncher(RunFolder, Path.Combine(_root, "src"), _starter, _presence, _time, TimeSpan.FromSeconds(60), NullLogger.Instance, (_, _) =>
		{
			copyThread = Environment.CurrentManagedThreadId;
			entered.Set();
			release.Wait(TimeSpan.FromSeconds(20));
			return new BuildCopyResult(0, 0, 0);
		});

		var first = launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None);
		Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the copy never started");
		Assert.False(first.IsCompleted); // still copying, and the caller was not blocked
		Assert.NotEqual(Environment.CurrentManagedThreadId, copyThread);

		var second = await launcher.EnsureStartedAsync(DaemonDiscoveryStatus.NoDaemonFile, CancellationToken.None); // would hang if the lock were held during the copy
		Assert.Equal(HostLaunchOutcome.AlreadyStarting, second);
		Assert.Empty(_starter.Starts);

		release.Set();
		Assert.Equal(HostLaunchOutcome.Started, await first);
		Assert.Single(_starter.Starts);
	}

	[Fact]
	public void MutexHostPresence_SeesAHostThatHoldsTheNamedMutex()
	{
		var name = @"Local\TessynDesktop.Test." + Guid.NewGuid().ToString("N");
		var presence = new MutexHostPresence(name);
		Assert.False(presence.IsHostRunning());

		using (new Mutex(initiallyOwned: true, name))
			Assert.True(presence.IsHostRunning());

		Assert.False(presence.IsHostRunning());
	}

	[Fact]
	public void MutexHostPresence_DefaultsToTheProtocolsHostMutexName()
	{
		Assert.Equal(@"Local\TessynDesktop.DaemonHost", RemoteProtocol.Discovery.HostMutexName);
	}

	[Fact]
	public async Task ProcessHostStarter_StartsARealProcessWithItsArgumentsAndWorkingDirectory()
	{
		var marker = Path.Combine(_root, "marker.txt");
		var workDir = Path.Combine(_root, "work");
		Directory.CreateDirectory(workDir);
		var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

		using var process = new ProcessHostStarter().Start(cmd, ["/c", "cd > marker.txt"], workDir);

		Assert.NotNull(process);
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!process!.HasExited && DateTime.UtcNow < deadline)
			await Task.Delay(50);

		Assert.True(process.HasExited);
		Assert.Equal(workDir, File.ReadAllText(Path.Combine(workDir, "marker.txt")).Trim());
		Assert.False(File.Exists(marker));
	}

	private sealed class RecordingLauncher : IDaemonHostLauncher
	{
		public List<DaemonDiscoveryStatus> Calls { get; } = [];

		public HostLaunchOutcome Outcome { get; set; } = HostLaunchOutcome.Started;

		public Exception? Throw { get; set; }

		public Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken)
		{
			Calls.Add(status);
			if (Throw != null)
				throw Throw;

			return Task.FromResult(Outcome);
		}
	}

	private static readonly DaemonDiscoveryResult FoundDaemon = new(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 1));

	[Fact]
	public async Task ConnectionLoop_StartsTheHostWhenNoDaemonRunsAndConnectsOnceItIsUp()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		locator.Results.Enqueue(FoundDaemon);
		var launcher = new RecordingLauncher();
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), launcher);
		var states = new List<ConnectionBannerState>();
		connection.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConnectionViewModel.State)) states.Add(connection.State); };

		await connection.RunAsync(CancellationToken.None);

		Assert.Equal([DaemonDiscoveryStatus.NoDaemonFile], launcher.Calls);
		Assert.Contains(ConnectionBannerState.StartingDaemon, states);
		Assert.Equal(ConnectionBannerState.Connected, connection.State);
	}

	[Fact]
	public async Task ConnectionLoop_NeverAsksForAHostWhenTheDaemonIsFound()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(FoundDaemon);
		var launcher = new RecordingLauncher();
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), launcher);

		await connection.RunAsync(CancellationToken.None);

		Assert.Empty(launcher.Calls);
	}

	[Fact]
	public async Task ConnectionLoop_ALauncherThatThrows_KeepsTheLoopAndTheBannerHonest()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var launcher = new RecordingLauncher { Throw = new IOException("disk") };
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), launcher);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

		await connection.RunAsync(cts.Token);

		Assert.True(launcher.Calls.Count >= 2);
		Assert.Equal(ConnectionBannerState.DaemonNotFound, connection.State);
	}

	[Fact]
	public async Task ConnectionLoop_AHostThatCouldNotBeFound_ShowsTheStartTessynDesktopBanner()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var launcher = new RecordingLauncher { Outcome = HostLaunchOutcome.HostMissing };
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), launcher);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

		await connection.RunAsync(cts.Token);

		Assert.Equal(ConnectionBannerState.DaemonNotFound, connection.State);
		Assert.Contains("Start TessynDesktop", connection.BannerText);
	}
}
