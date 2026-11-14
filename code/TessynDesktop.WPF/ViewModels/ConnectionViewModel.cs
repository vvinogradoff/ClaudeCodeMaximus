using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Finds the daemon, creates and connects the client, and exposes the connection banner. The client is created once
/// (reconnects and resume are its own job); this view model reports its state changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ConnectionViewModel : ObservableObject
{
	private readonly IDaemonLocator _locator;
	private readonly ITessynClientFactory _factory;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<ConnectionViewModel> _logger;
	private readonly TimeSpan _retryDelay;

	private ITessynClient? _client;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(BannerText), nameof(IsBannerVisible))]
	private ConnectionBannerState _state = ConnectionBannerState.Connecting;

	[ObservableProperty]
	private string? _detail;

	public ConnectionViewModel(IDaemonLocator locator, ITessynClientFactory factory, IUiDispatcher ui, ILogger<ConnectionViewModel> logger)
		: this(locator, factory, ui, logger, Constants.Timeouts.DaemonDiscoveryRetry)
	{
	}

	public ConnectionViewModel(IDaemonLocator locator, ITessynClientFactory factory, IUiDispatcher ui, ILogger<ConnectionViewModel> logger, TimeSpan retryDelay)
	{
		_locator = locator;
		_factory = factory;
		_ui = ui;
		_logger = logger;
		_retryDelay = retryDelay;
	}

	/// <summary>Raised on the UI thread once the client completed its first handshake.</summary>
	public event Action<ITessynClient>? ClientReady;

	public bool IsBannerVisible => State != ConnectionBannerState.Connected;

	public string BannerText => State switch
	{
		ConnectionBannerState.DaemonNotFound => "Daemon not found. Start TessynDesktop to connect.",
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
					Set(ConnectionBannerState.DaemonNotFound, found.Status.ToString());
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
				_ui.Post(() =>
				{
					Apply(ConnectionBannerState.Connected, null);
					ClientReady?.Invoke(client);
				});
				return;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (TessynAuthException ex)
			{
				_logger.LogWarning(ex, "Daemon rejected the token");
				Set(ConnectionBannerState.AuthFailed, ex.Message);
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
				Set(ConnectionBannerState.DaemonNotFound, ex.Message);
				try
				{
					await Task.Delay(_retryDelay, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}
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
