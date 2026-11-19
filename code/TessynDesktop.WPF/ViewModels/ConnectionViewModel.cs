using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Switch;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Finds the daemon, creates and connects the client, and exposes the connection banner. The client is created once
/// (reconnects and resume are its own job); this view model reports its state changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ConnectionViewModel : ObservableObject, IAsyncDisposable, IDaemonModeControl
{
	private readonly IDaemonLocator _locator;
	private readonly ITessynClientFactory _factory;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<ConnectionViewModel> _logger;
	private readonly TimeSpan _retryDelay;
	private readonly IDaemonHostLauncher _hostLauncher;
	private readonly DaemonRestarter? _restarter;
	private readonly IClientSettingsStore? _settings;
	private long _startingSince;
	private int _daemonPid;
	private int _daemonCheckRunning;

	private ITessynClient? _client;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(BannerText), nameof(IsBannerVisible), nameof(IsElevationBannerVisible))]
	private ConnectionBannerState _state = ConnectionBannerState.Connecting;

	/// <summary>FR.32.12: true when the connected daemon runs elevated, false when it runs normally; null while unknown (not connected, an older daemon).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsElevationBannerVisible))]
	private bool? _daemonElevated;

	/// <summary>FR.32.13: the notice after a declined or failed elevated start; null when there is none.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsElevationBannerVisible), nameof(ElevationBannerText))]
	private string? _elevationNote;

	[ObservableProperty]
	private string? _detail;

	public ConnectionViewModel(IDaemonLocator locator, ITessynClientFactory factory, IUiDispatcher ui, ILogger<ConnectionViewModel> logger, IDaemonHostLauncher? hostLauncher = null, DaemonRestarter? restarter = null, IClientSettingsStore? settings = null)
		: this(locator, factory, ui, logger, Constants.Timeouts.DaemonDiscoveryRetry, hostLauncher, restarter, settings)
	{
	}

	/// <param name="settings">The client settings: "Start daemon elevated" decides the elevation banner (FR.32.12); null = the setting is off.</param>
	public ConnectionViewModel(IDaemonLocator locator, ITessynClientFactory factory, IUiDispatcher ui, ILogger<ConnectionViewModel> logger, TimeSpan retryDelay, IDaemonHostLauncher? hostLauncher = null, DaemonRestarter? restarter = null, IClientSettingsStore? settings = null)
	{
		_restarter = restarter;
		_settings = settings;
		_hostLauncher = hostLauncher ?? new NoDaemonHostLauncher();
		_hostLauncher.ElevationRefused += OnElevationRefused;
		if (_settings != null)
			_settings.Changed += OnSettingsChanged;
		_locator = locator;
		_factory = factory;
		_ui = ui;
		_logger = logger;
		_retryDelay = retryDelay;
	}

	/// <summary>Raised on the UI thread once the client completed its first handshake.</summary>
	public event Action<ITessynClient>? ClientReady;

	/// <summary>The connected client; null until the first handshake completed (a late subscriber to <see cref="ClientReady"/> checks it).</summary>
	public ITessynClient? Client => _client;

	public bool IsBannerVisible => State != ConnectionBannerState.Connected;

	public string BannerText => State switch
	{
		ConnectionBannerState.DaemonNotFound => "Daemon not found. Start TessynDesktop to connect.",
		ConnectionBannerState.DaemonUnreachable => "Daemon found but not reachable. It may still be starting.",
		ConnectionBannerState.StartingDaemon => "Starting the Tessyn daemon in the background...",
		ConnectionBannerState.WaitingForTessyn => Constants.Switch.WaitingText,
		ConnectionBannerState.Connecting => "Connecting to the daemon...",
		ConnectionBannerState.Connected => "Connected",
		ConnectionBannerState.Reconnecting => "Connection lost. Reconnecting...",
		ConnectionBannerState.Incompatible => "Incompatible daemon version. Update Tessyn.",
		ConnectionBannerState.AuthFailed => "Authentication failed. The daemon rejected this client's token.",
		_ => string.Empty,
	};

	/// <summary>Runs the discovery and connect loop until connected or a terminal state is reached.</summary>
	public async Task RunAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				var found = await _locator.DiscoverAsync(cancellationToken);
				if (found.Status != DaemonDiscoveryStatus.Found || found.Daemon == null)
				{
					var launch = await StartHostIfNeededAsync(found.Status, cancellationToken);
					var waiting = launch is HostLaunchOutcome.Started or HostLaunchOutcome.AlreadyStarting or HostLaunchOutcome.HostPresent;
					if (waiting && _startingSince == 0)
						_startingSince = Stopwatch.GetTimestamp();
					else if (!waiting)
						_startingSince = 0;

					if (waiting && Stopwatch.GetElapsedTime(_startingSince) < StartingTimeout)
						Set(ConnectionBannerState.StartingDaemon, found.Status.ToString());
					else if (waiting)
						Set(ConnectionBannerState.DaemonNotFound, Constants.Switch.StartingTimedOutDetail); // never "Starting..." for ever
					else if (launch == HostLaunchOutcome.Disabled)
						Set(ConnectionBannerState.WaitingForTessyn, found.Status.ToString()); // side-by-side: the Avalonia app brings the daemon up, the loop connects
					else
						Set(ConnectionBannerState.DaemonNotFound, found.Status == DaemonDiscoveryStatus.TokenUnavailable ? Constants.Switch.TokenUnavailableDetail : found.Status.ToString());
					await Task.Delay(_retryDelay, cancellationToken);
					continue;
				}

				Set(ConnectionBannerState.Connecting, null);
				var client = _factory.Create(found.Daemon);
				client.StateChanged += OnClientStateChanged;
				try
				{
					await client.ConnectAsync(cancellationToken);
				}
				catch
				{
					client.StateChanged -= OnClientStateChanged;
					await client.DisposeAsync();
					throw;
				}

				_client = client;
				_startingSince = 0; // a later disconnect starts its own "Starting..." clock
				_daemonPid = found.Daemon.Pid;
				client.Resynced += OnClientResynced;
				var elevated = client.ServerInfo?.Elevated;
				_logger.LogInformation("Connected to daemon run {RunId} (protocol {Protocol}, elevated {Elevated})", client.ServerInfo?.RunId, client.ServerInfo?.ProtocolVersion, elevated?.ToString() ?? "unknown");
				_ui.Post(() =>
				{
					Apply(ConnectionBannerState.Connected, null);
					ApplyElevation(elevated);
					ClientReady?.Invoke(client);
				});
				_ = CheckDaemonBuildAsync(found.Daemon.Pid);
				return;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (TessynAuthException ex)
			{
				// The daemon may have restarted with a new token: show the banner, then re-read daemon.json and try again.
				_logger.LogWarning("Daemon rejected the token: {Message}", ex.Message);
				Set(ConnectionBannerState.AuthFailed, ex.Message);
				if (!await DelayAsync(cancellationToken))
					return;
			}
			catch (TessynIncompatibleException ex)
			{
				_logger.LogWarning(ex, "Daemon protocol incompatible");
				Set(ConnectionBannerState.Incompatible, ex.Message);
				return;
			}
			catch (Exception ex)
			{
				_logger.LogInformation("Connect attempt failed: {Message}", ex.Message);
				Set(ConnectionBannerState.DaemonUnreachable, ex.Message);
				if (!await DelayAsync(cancellationToken))
					return;
			}
		}
	}

	/// <summary>How long "Starting the daemon..." may last before the banner says what is wrong.</summary>
	public TimeSpan StartingTimeout { get; set; } = Constants.Switch.StartingTimeout;

	/// <summary>True when the connected daemon runs an older build than the newest host build (the banner offers "Restart daemon").</summary>
	[ObservableProperty]
	private bool _isDaemonOlder;

	/// <summary>Why the last restart attempt did not end the daemon; null when there is nothing to say.</summary>
	[ObservableProperty]
	private string? _restartNote;

	public string DaemonOlderText => Constants.Switch.DaemonOlderText;

	/// <summary>Evaluates the stale-daemon banner again (after a check for a newer build, FR.32.9); does nothing while not connected.</summary>
	public void RecheckDaemonBuild()
	{
		if (_client != null && _daemonPid != 0)
			_ = CheckDaemonBuildAsync(_daemonPid);
	}

	private async Task CheckDaemonBuildAsync(int pid)
	{
		if (_restarter == null || Interlocked.Exchange(ref _daemonCheckRunning, 1) == 1)
			return;

		try
		{
			var stale = await Task.Run(() => _restarter.Check(pid)); // off the UI thread: it reads assemblies and runs git
			_ui.Post(() => IsDaemonOlder = stale != null);
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "Could not compare the daemon's build with the newest build");
		}
		finally
		{
			Volatile.Write(ref _daemonCheckRunning, 0);
		}
	}

	/// <summary>
	/// The banner's "Restart daemon": asks, ends the daemon, starts the new host the way the daemon ran (FR.32.11) and restarts this client, which attaches to it.
	/// </summary>
	[RelayCommand]
	private async Task RestartDaemonAsync()
	{
		if (_restarter == null)
			return;

		var sessions = await RunningSessionsAsync();
		var keep = DaemonElevated == true ? HostStartMode.Elevated : HostStartMode.Normal;
		var outcome = await _restarter.RestartAsync(_daemonPid, sessions, CancellationToken.None, keep);
		_ui.Post(() => RestartNote = NoteFor(outcome));
	}

	/// <summary>FR.32.12: the banner while the daemon runs normally and "Start daemon elevated" is on, or after a declined or failed elevated start.</summary>
	public bool IsElevationBannerVisible => State == ConnectionBannerState.Connected && DaemonElevated == false && (ElevationNote != null || IsStartElevatedOn());

	public string ElevationBannerText => ElevationNote ?? Constants.Switch.ElevationExpectedText;

	/// <summary>True when the daemon can be restarted from here: the real launcher and a connected daemon.</summary>
	public bool CanRestartDaemon => _restarter != null && _client != null && _daemonPid != 0;

	/// <summary>Restart daemon elevated (FR.32.11), on the elevation banner and in Settings, Startup.</summary>
	[RelayCommand]
	private Task RestartDaemonElevatedAsync() => SwitchDaemonAsync(HostStartMode.Elevated);

	/// <summary>Restart daemon normal (FR.32.11), in Settings, Startup.</summary>
	[RelayCommand]
	private Task RestartDaemonNormalAsync() => SwitchDaemonAsync(HostStartMode.Normal);

	/// <summary>Asks first, ends the daemon and starts the host <paramref name="mode"/>'s way (FR.32.11); this client reconnects to the new run by itself.</summary>
	public async Task SwitchDaemonAsync(HostStartMode mode)
	{
		if (!CanRestartDaemon || _restarter == null)
			return;

		var sessions = await RunningSessionsAsync();
		var outcome = await _restarter.SwitchAsync(_daemonPid, sessions, mode, CancellationToken.None);
		_ui.Post(() =>
		{
			RestartNote = NoteFor(outcome);
			if (outcome != DaemonRestartOutcome.Restarted)
				return;

			DaemonElevated = null;   // unknown until the new run's hello
			if (mode == HostStartMode.Normal)
				ElevationNote = null;   // the User chose normal: no notice about a declined prompt
		});
	}

	private async Task<int> RunningSessionsAsync()
	{
		try
		{
			if (_client != null)
				return (await _client.DaemonHealthAsync()).SessionsRunning;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogInformation(ex, "Could not read the number of running sessions");
		}

		return 0;
	}

	private static string? NoteFor(DaemonRestartOutcome outcome) => outcome switch
	{
		DaemonRestartOutcome.NotSupported => Constants.Switch.RestartNotSupportedNote,
		DaemonRestartOutcome.DidNotExit => Constants.Switch.RestartDidNotExitNote,
		DaemonRestartOutcome.NotStarted => Constants.Switch.RestartNotStartedNote,
		_ => null,
	};

	private bool IsStartElevatedOn() => _settings?.Get<DaemonHostSettings>().StartDaemonElevated == true;

	private void ApplyElevation(bool? elevated)
	{
		DaemonElevated = elevated;
		if (elevated == true)
			ElevationNote = null;
		OnPropertyChanged(nameof(CanRestartDaemon));
	}

	private void OnElevationRefused(ElevationRefusal refusal) => _ui.Post(() => ElevationNote = refusal.NoticeText);

	private void OnSettingsChanged(object? sender, ClientSettingsChangedEventArgs e)
	{
		if (e.Section == Constants.SettingsSections.DaemonHost)
			_ui.Post(() => OnPropertyChanged(nameof(IsElevationBannerVisible)));
	}

	/// <summary>A new daemon run (a restart, FR.32.6): its pid, its way of running and its build are read again.</summary>
	private void OnClientResynced(object? sender, ResyncedEventArgs e)
	{
		if (e.Reason == ResyncedEventArgs.RunChangedReason)
			_ = FollowNewRunAsync();
	}

	private async Task FollowNewRunAsync()
	{
		try
		{
			var found = await _locator.DiscoverAsync(CancellationToken.None);
			if (found.Status == DaemonDiscoveryStatus.Found && found.Daemon != null)
				_daemonPid = found.Daemon.Pid;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogInformation(ex, "Could not read the new daemon run's discovery file");
		}

		var elevated = _client?.ServerInfo?.Elevated;
		_logger.LogInformation("Following the new daemon run {RunId} (pid {Pid}, elevated {Elevated})", _client?.ServerInfo?.RunId, _daemonPid, elevated?.ToString() ?? "unknown");
		_ui.Post(() => ApplyElevation(elevated));
		if (_daemonPid != 0)
			_ = CheckDaemonBuildAsync(_daemonPid);
	}

	/// <summary>Asks the launcher to start the background host; a failure of that never ends the connect loop.</summary>
	private async Task<HostLaunchOutcome> StartHostIfNeededAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken)
	{
		try
		{
			return await _hostLauncher.EnsureStartedAsync(status, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Starting the daemon host failed");
			return HostLaunchOutcome.Failed;
		}
	}

	private async Task<bool> DelayAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(_retryDelay, cancellationToken);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	/// <summary>Disconnects and disposes the client (at exit).</summary>
	public async ValueTask DisposeAsync()
	{
		var client = _client;
		_client = null;
		if (client == null)
			return;

		client.StateChanged -= OnClientStateChanged;
		client.Resynced -= OnClientResynced;
		_hostLauncher.ElevationRefused -= OnElevationRefused;
		if (_settings != null)
			_settings.Changed -= OnSettingsChanged;
		await client.DisposeAsync().ConfigureAwait(false);
	}

	/// <summary>Maps a client state to the banner state it implies once the client exists.</summary>
	public ConnectionBannerState MapClientState(TessynClientState state) => state switch
	{
		TessynClientState.Connected => ConnectionBannerState.Connected,
		TessynClientState.Connecting => ConnectionBannerState.Connecting,
		TessynClientState.Reconnecting => ConnectionBannerState.Reconnecting,
		TessynClientState.Incompatible => ConnectionBannerState.Incompatible,
		TessynClientState.AuthFailed => ConnectionBannerState.AuthFailed,
		_ => ConnectionBannerState.DaemonNotFound,
	};

	private void OnClientStateChanged(object? sender, TessynClientStateChangedEventArgs e)
	{
		// FR.32.12: nothing is said about the daemon's elevation while it is not connected; a reconnect to the same run says it again.
		var elevated = e.Current == TessynClientState.Connected ? _client?.ServerInfo?.Elevated : null;
		_ui.Post(() =>
		{
			Apply(MapClientState(e.Current), null);
			ApplyElevation(elevated);
		});
	}

	private void Set(ConnectionBannerState state, string? detail) => _ui.Post(() => Apply(state, detail));

	private void Apply(ConnectionBannerState state, string? detail)
	{
		State = state;
		Detail = detail;
	}
}
