using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The connection banner states, driven by discovery results and client state changes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ConnectionViewModelTests
{
	private sealed class ScriptedLocator : IDaemonLocator
	{
		public Queue<DaemonDiscoveryResult> Results { get; } = new();

		public int Calls { get; private set; }

		public Task<DaemonDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
		{
			Calls++;
			var next = Results.Count > 1 ? Results.Dequeue() : Results.Peek();
			return Task.FromResult(next);
		}
	}

	private sealed class FixedFactory : ITessynClientFactory
	{
		public FixedFactory(FakeTessynClient client) => Client = client;

		public FakeTessynClient Client { get; }

		public ITessynClient Create(DaemonEndpoint endpoint) => Client;
	}

	private static readonly DaemonDiscoveryResult Found = new(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 1));

	private static ConnectionViewModel Create(ScriptedLocator locator, FakeTessynClient client) =>
		new(locator, new FixedFactory(client), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));

	[Fact]
	public async Task NoDaemonFile_ShowsDaemonNotFoundAndKeepsRetrying()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		locator.Results.Enqueue(Found);
		var client = new FakeTessynClient();
		var connection = Create(locator, client);
		var states = new List<ConnectionBannerState>();
		connection.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConnectionViewModel.State)) states.Add(connection.State); };

		await connection.RunAsync(CancellationToken.None);

		Assert.Contains(ConnectionBannerState.DaemonNotFound, states);
		Assert.Equal(ConnectionBannerState.Connected, connection.State);
		Assert.Equal(2, locator.Calls);
	}

	[Fact]
	public async Task DaemonNotFound_BannerTellsTheUserToStartTessynDesktop()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.StaleFile, null));
		var connection = Create(locator, new FakeTessynClient());
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

		await connection.RunAsync(cts.Token);

		Assert.Equal(ConnectionBannerState.DaemonNotFound, connection.State);
		Assert.True(connection.IsBannerVisible);
		Assert.Contains("Start TessynDesktop to connect", connection.BannerText);
	}

	[Fact]
	public async Task Connected_HidesTheBannerAndRaisesClientReady()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var client = new FakeTessynClient();
		var connection = Create(locator, client);
		ITessynClient? ready = null;
		connection.ClientReady += c => ready = c;

		await connection.RunAsync(CancellationToken.None);

		Assert.Same(client, ready);
		Assert.False(connection.IsBannerVisible);
	}

	[Theory]
	[InlineData(TessynClientState.Connecting, ConnectionBannerState.Connecting)]
	[InlineData(TessynClientState.Reconnecting, ConnectionBannerState.Reconnecting)]
	[InlineData(TessynClientState.Incompatible, ConnectionBannerState.Incompatible)]
	[InlineData(TessynClientState.AuthFailed, ConnectionBannerState.AuthFailed)]
	[InlineData(TessynClientState.Connected, ConnectionBannerState.Connected)]
	[InlineData(TessynClientState.Disconnected, ConnectionBannerState.DaemonNotFound)]
	public async Task ClientStateChanges_MapToBannerStates(TessynClientState clientState, ConnectionBannerState expected)
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var client = new FakeTessynClient();
		var connection = Create(locator, client);
		await connection.RunAsync(CancellationToken.None);

		client.SetState(clientState);

		Assert.Equal(expected, connection.State);
		Assert.Equal(expected != ConnectionBannerState.Connected, connection.IsBannerVisible);
		Assert.False(string.IsNullOrEmpty(connection.BannerText));
	}

	[Fact]
	public async Task AuthFailure_IsTerminalAndShownInTheBanner()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var client = new FakeTessynClient { ConnectException = new TessynAuthException(401, "bad token") };
		var connection = Create(locator, client);

		await connection.RunAsync(CancellationToken.None);

		Assert.Equal(ConnectionBannerState.AuthFailed, connection.State);
		Assert.Equal(1, locator.Calls);
	}

	[Fact]
	public async Task IncompatibleDaemon_IsTerminalAndShownInTheBanner()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var hello = new HelloResult(9, "9.0", "m", "run", DateTimeOffset.UtcNow, [], 9, 0);
		var client = new FakeTessynClient { ConnectException = new TessynIncompatibleException(hello) };
		var connection = Create(locator, client);

		await connection.RunAsync(CancellationToken.None);

		Assert.Equal(ConnectionBannerState.Incompatible, connection.State);
	}

	[Fact]
	public async Task ConnectFailure_FallsBackToDaemonNotFoundAndRetries()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(Found);
		var client = new FakeTessynClient { ConnectException = new HttpRequestException("refused") };
		var connection = Create(locator, client);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		await connection.RunAsync(cts.Token);

		Assert.Equal(ConnectionBannerState.DaemonNotFound, connection.State);
		Assert.True(locator.Calls > 1);
	}
}
