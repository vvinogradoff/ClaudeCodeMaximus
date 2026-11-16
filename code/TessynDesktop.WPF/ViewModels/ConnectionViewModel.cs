using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Switch;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Finds the daemon, creates and connects the client, and exposes the connection banner. The client is created once
/// (reconnects and resume are its own job); this view model reports its state changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ConnectionViewModel : ObservableObject, IAsyncDisposable
{
	private readonly IDaemonLocator _locator;
	private readonly ITessynClientFactory _factory;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<ConnectionViewModel> _logger;
	private readonly TimeSpan _retryDelay;
	private readonly IDaemonHostLauncher _hostLauncher;
	private readonly DaemonRestarter? _restarter;
	private long _startingSince;
	private int _daemonPid;

	private ITessynClient? _client;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(BannerText), nameof(IsBannerVisible))]
	private ConnectionBannerState _state = ConnectionBannerState.Connecting;

	[ObservableProperty]
	private string? _detail;

	public ConnectionViewModel(IDaemonLocator locator, ITessynClientFactory factory, IUiDispatcher ui, ILogger<ConnectionViewModel> logger, IDaemonHostLauncher? hostLauncher = null, DaemonRestarter? restarter = null)
		: this(locator, factory, ui, logger, Constants.Timeouts.DaemonDiscoveryRetry, hostLauncher, restarter)
	{
	}

	public ConnectionViewModel(IDaemonLocator locator, ITessynClientFactory factory, IUiDispatcher ui, ILogger<ConnectionViewModel> logger, TimeSpan retryDelay, IDaemonHostLauncher? hostLauncher = null, DaemonRestarter? restarter = null)
	{
		_restarter = restarter;
		_hostLauncher = hostLauncher ?? new NoDaemonHostLauncher();
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
				_logger.LogInformation("Connected to daemon run {RunId} (protocol {Protocol})", client.ServerInfo?.RunId, client.ServerInfo?.ProtocolVersion);
				_ui.Post(() =>
				{
					Apply(ConnectionBannerState.Connected, null);
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

	private async Task CheckDaemonBuildAsync(int pid)
	{
		if (_restarter == null)
			return;

		try
		{
			var stale = await Task.Run(() => _restarter.Check(pid));
			_ui.Post(() => IsDaemonOlder = stale != null);
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "Could not compare the daemon's build with the newest build");
		}
	}

	/// <summary>The banner's "Restart daemon": asks, ends the daemon, restarts this client (the new start brings the host back on the newest build).</summary>
	[RelayCommand]
	private async Task RestartDaemonAsync()
	{
		if (_restarter == null)
			return;

		var sessions = 0;
		try
		{
			if (_client != null)
				sessions = (await _client.DaemonHealthAsync()).SessionsRunning;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogInformation(ex, "Could not read the number of running sessions");
		}

		var outcome = await _restarter.RestartAsync(_daemonPid, sessions, CancellationToken.None);
		var detail = outcome switch
		{
			DaemonRestartOutcome.NotSupported => "this daemon cannot be restarted from here; exit it from its tray icon or window, then start the client",
			DaemonRestartOutcome.DidNotExit => "the daemon did not exit in time; exit it from its tray icon",
			_ => null,
		};
		_ui.Post(() => RestartNote = detail);
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
		_ui.Post(() => Apply(MapClientState(e.Current), null));
	}

	private void Set(ConnectionBannerState state, string? detail) => _ui.Post(() => Apply(state, detail));

	private void Apply(ConnectionBannerState state, string? detail)
	{
		State = state;
		Detail = detail;
	}
}
