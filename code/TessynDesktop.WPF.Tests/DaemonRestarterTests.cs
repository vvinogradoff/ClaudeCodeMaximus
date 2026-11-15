using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The "daemon is older than the build" banner and its one-click restart, and the startup banner's limits (review 459: M2, M4, L1, L2, L3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonRestarterTests : IDisposable
{
	private sealed class FakeProbe : IDaemonBuildProbe
	{
		public DateTime? Running { get; set; }

		public DateTime? RunningBuildUtc(int pid) => Running;
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

		public List<TimeSpan> Waits { get; } = [];

		public Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken)
		{
			Waits.Add(timeout);
			return Task.FromResult(Exits);
		}
	}

	private sealed class FakeClientRestarter : IClientRestarter
	{
		public int Restarts { get; private set; }

		public void RestartClient() => Restarts++;
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

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-switch-restart-" + Guid.NewGuid().ToString("N"));
	private readonly FakeProbe _probe = new();
	private readonly FakeSignal _signal = new();
	private readonly FakeWaiter _waiter = new();
	private readonly FakeClientRestarter _client = new();
	private readonly Dialogs _dialogs = new();
	private static readonly DateTime Build = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

	public DaemonRestarterTests()
	{
		var dir = Path.Combine(_root, "src", "code", "TessynDesktop", "bin", "Debug", "net10.0-windows10.0.17763.0");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "TessynDesktop.dll"), "dll");
		File.SetLastWriteTimeUtc(Path.Combine(dir, "TessynDesktop.dll"), Build);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	private DaemonRestarter Create(string? source = "") =>
		new(source == "" ? Path.Combine(_root, "src") : source, _probe, _dialogs, _signal, _waiter, _client, TimeSpan.FromSeconds(7), NullLogger.Instance);

	[Fact]
	public void Check_ADaemonOlderThanTheNewestHostBuild_IsStale()
	{
		_probe.Running = Build.AddHours(-3);

		var stale = Create().Check(1);

		Assert.NotNull(stale);
		Assert.Equal(Build, stale!.NewestBuildUtc);
	}

	[Fact]
	public void Check_TheSameBuild_OrOneWithinTheTolerance_IsNotStale()
	{
		_probe.Running = Build;
		Assert.Null(Create().Check(1));

		_probe.Running = Build.AddSeconds(-1);
		Assert.Null(Create().Check(1));
	}

	[Fact]
	public void Check_AnUnknownBuildOrNoSource_NeverShowsTheBanner()
	{
		_probe.Running = null;
		Assert.Null(Create().Check(1));

		_probe.Running = Build.AddDays(-1);
		Assert.Null(Create(null).Check(1));
		Assert.Null(Create(Path.Combine(_root, "empty")).Check(1));
	}

	[Fact]
	public async Task Restart_Confirmed_SignalsTheHostWaitsForItAndRestartsTheClient()
	{
		var outcome = await Create().RestartAsync(77, 3, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.ClientRestarting, outcome);
		Assert.Equal(1, _signal.Signals);
		Assert.Equal([TimeSpan.FromSeconds(7)], _waiter.Waits);
		Assert.Equal(1, _client.Restarts);
		var confirm = Assert.Single(_dialogs.Confirms);
		Assert.Contains("3 active session(s) will be terminated", confirm.Message);
		Assert.True(confirm.IsDestructive);
	}

	[Fact]
	public async Task Restart_NoActiveSessions_AsksWithoutTheTerminationWarning()
	{
		await Create().RestartAsync(77, 0, CancellationToken.None);

		var confirm = Assert.Single(_dialogs.Confirms);
		Assert.DoesNotContain("terminated", confirm.Message);
		Assert.False(confirm.IsDestructive);
	}

	[Fact]
	public async Task Restart_Declined_ChangesNothing()
	{
		_dialogs.Answer = false;

		var outcome = await Create().RestartAsync(77, 2, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.Cancelled, outcome);
		Assert.Equal(0, _signal.Signals);
		Assert.Equal(0, _client.Restarts);
	}

	[Fact]
	public async Task Restart_AHostThatDoesNotListen_IsNotSupported_AndTheClientStays()
	{
		_signal.Listening = false;

		var outcome = await Create().RestartAsync(77, 0, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.NotSupported, outcome);
		Assert.Equal(0, _client.Restarts);
	}

	[Fact]
	public async Task Restart_AHostThatStaysUp_IsReported_AndTheClientStays()
	{
		_waiter.Exits = false;

		var outcome = await Create().RestartAsync(77, 0, CancellationToken.None);

		Assert.Equal(DaemonRestartOutcome.DidNotExit, outcome);
		Assert.Equal(0, _client.Restarts);
	}

	private static readonly DaemonDiscoveryResult Found = new(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 4321));

	private ConnectionViewModel Connection(ScriptedLocator locator, IDaemonHostLauncher? launcher = null, DaemonRestarter? restarter = null) =>
		new(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5), launcher, restarter);

	[Fact]
	public async Task Connection_AtConnectItComparesTheDaemonBuild_AndShowsTheRestartBanner()
	{
		_probe.Running = Build.AddHours(-2);
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var connection = Connection(locator, restarter: Create());

		await connection.RunAsync(CancellationToken.None);
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!connection.IsDaemonOlder && DateTime.UtcNow < deadline)
			await Task.Delay(20);

		Assert.True(connection.IsDaemonOlder);
		Assert.Equal("The daemon is older than the build.", connection.DaemonOlderText);
	}

	[Fact]
	public async Task Connection_ACurrentDaemon_ShowsNoRestartBanner()
	{
		_probe.Running = Build;
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var connection = Connection(locator, restarter: Create());

		await connection.RunAsync(CancellationToken.None);
		await Task.Delay(150);

		Assert.False(connection.IsDaemonOlder);
	}

	[Fact]
	public async Task Connection_RestartDaemon_RunsTheConfirmExitAndRestartWithTheDaemonsPid_AndNotesWhyItFailed()
	{
		_probe.Running = Build.AddHours(-2);
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var connection = Connection(locator, restarter: Create());
		await connection.RunAsync(CancellationToken.None);

		_signal.Listening = false;
		await connection.RestartDaemonCommand.ExecuteAsync(null);

		Assert.Equal(1, _signal.Signals);
		Assert.Contains("cannot be restarted from here", connection.RestartNote);

		_signal.Listening = true;
		await connection.RestartDaemonCommand.ExecuteAsync(null);

		Assert.Equal(1, _client.Restarts);
		Assert.Null(connection.RestartNote);
	}

	private sealed class AlwaysStartingLauncher : IDaemonHostLauncher
	{
		public Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken) => Task.FromResult(HostLaunchOutcome.AlreadyStarting);
	}

	[Fact]
	public async Task Connection_AHostThatNeverPublishesDaemonJson_StopsSayingStartingAfterTheTimeout()
	{
		// L3: "Starting the daemon..." must not last for ever.
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var connection = Connection(locator, new AlwaysStartingLauncher());
		connection.StartingTimeout = TimeSpan.FromMilliseconds(30);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

		await connection.RunAsync(cts.Token);

		Assert.Equal(ConnectionBannerState.DaemonNotFound, connection.State);
		Assert.Contains("did not publish daemon.json", connection.Detail);
	}

	[Fact]
	public async Task Connection_AnUnreadableToken_SaysSoInsteadOfJustDaemonNotFound()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.TokenUnavailable, null));
		var connection = Connection(locator);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

		await connection.RunAsync(cts.Token);

		Assert.Contains("token cannot be read", connection.Detail);
	}
}
