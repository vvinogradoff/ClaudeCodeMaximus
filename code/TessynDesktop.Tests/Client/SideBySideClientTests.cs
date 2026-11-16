using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// The WPF client beside another client of the same daemon (the Avalonia app hosts it; here a second TDCP client stands for what else is connected), and across the
/// takeover (a daemon that ends and a new one on the same port with the same token, as when Avalonia replaces a background host). Real client, real server, no UI.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SideBySideClientTests
{
	/// <summary>A presence registry for one daemon run (a new run starts with an empty one) that records every registration.</summary>
	private sealed class PerRunRegistry : IClientPresenceRegistry
	{
		private ClientPresenceRegistry _current = new();

		public List<(string ClientId, string[] Presents)> Registrations { get; } = [];

		public event Action? Changed;

		/// <summary>The daemon ended: everything registered with it is gone.</summary>
		public void NewRun() => _current = new ClientPresenceRegistry();

		public void Register(string clientId, IReadOnlyCollection<string> presents)
		{
			lock (Registrations)
				Registrations.Add((clientId, [.. presents]));
			_current.Register(clientId, presents);
			Changed?.Invoke();
		}

		public IDisposable OpenStream(string? clientId) => _current.OpenStream(clientId);

		public bool IsPresented(string channel) => _current.IsPresented(channel);

		public int RegistrationsOf(string clientId)
		{
			lock (Registrations)
				return Registrations.Count(r => r.ClientId == clientId);
		}
	}

	private static TessynClient NewClient(ClientTestHost host, string? clientId = null, params string[] presents) =>
		new(host.Endpoint, ClientTestHost.Token, new TessynClientOptions { ClientId = clientId, Presents = presents, InitialBackoff = TimeSpan.FromMilliseconds(50), MaxBackoff = TimeSpan.FromMilliseconds(200) });

	private static ClientTestHost NewHost(PerRunRegistry presence) =>
		new((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log, presence: presence)]), presence: presence, keepAlive: TimeSpan.FromMilliseconds(100));

	private static async Task<bool> BecomesAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return true;
			await Task.Delay(25);
		}

		return condition();
	}

	[Fact]
	public async Task TwoClientsOfOneDaemon_BothSeeEveryEvent_AndOnlyTheRegisteredOneSilencesTheHostSound()
	{
		var presence = new PerRunRegistry();
		using var host = NewHost(presence);
		await using var wpf = NewClient(host, "wpf-test", RemoteProtocol.Notifications.SoundChannel);
		await using var other = NewClient(host, clientId: null);
		var wpfProbe = new ClientProbe(wpf);
		var otherProbe = new ClientProbe(other);
		await wpf.ConnectAsync();
		await other.ConnectAsync();

		host.Publish(3); // what the daemon publishes for a turn started from either side

		for (var i = 0; i < 3; i++)
		{
			Assert.Equal(i, (await wpfProbe.NextNotificationAsync()).Seq - 1);
			Assert.Equal(i, (await otherProbe.NextNotificationAsync()).Seq - 1);
		}

		// J2: the sound is the WPF client's (it registered it), the toast is not (the host shows it itself while WPF does not register toasts).
		Assert.True(await BecomesAsync(() => presence.IsPresented(RemoteProtocol.Notifications.SoundChannel)));
		Assert.False(presence.IsPresented(RemoteProtocol.Notifications.ToastChannel));
		Assert.Equal(1, presence.RegistrationsOf("wpf-test"));
	}

	[Fact]
	public async Task WhenTheWpfClientCloses_TheHostPlaysTheSoundAgain_WhileTheOtherClientStaysConnected()
	{
		var presence = new PerRunRegistry();
		using var host = NewHost(presence);
		var wpf = NewClient(host, "wpf-test", RemoteProtocol.Notifications.SoundChannel);
		await using var other = NewClient(host, clientId: null);
		await wpf.ConnectAsync();
		await other.ConnectAsync();
		Assert.True(await BecomesAsync(() => presence.IsPresented(RemoteProtocol.Notifications.SoundChannel)));

		await wpf.DisposeAsync();

		Assert.True(await BecomesAsync(() => !presence.IsPresented(RemoteProtocol.Notifications.SoundChannel)));
		Assert.Equal(TessynClientState.Connected, other.State);
	}

	[Fact]
	public async Task WhenTheDaemonIsReplacedOnTheSamePort_BothClientsFollow_AndWpfRegistersItsSoundAgain()
	{
		var presence = new PerRunRegistry();
		using var host = NewHost(presence);
		await using var wpf = NewClient(host, "wpf-test", RemoteProtocol.Notifications.SoundChannel);
		await using var other = NewClient(host, clientId: null);
		var wpfProbe = new ClientProbe(wpf);
		var otherProbe = new ClientProbe(other);
		await wpf.ConnectAsync();
		await other.ConnectAsync();
		var firstRun = wpf.ServerInfo!.RunId;
		Assert.True(await BecomesAsync(() => presence.IsPresented(RemoteProtocol.Notifications.SoundChannel)));

		// The takeover: the background host exits (its listener goes), Avalonia starts a new daemon run on the same port with the same token.
		host.StopServer();
		await wpfProbe.WaitForStateAsync(TessynClientState.Reconnecting);
		presence.NewRun();
		host.RestartAsNewRun();

		var resync = await wpfProbe.NextResyncAsync();
		Assert.Equal(ResyncedEventArgs.RunChangedReason, resync.Reason);
		await otherProbe.NextResyncAsync();
		await wpfProbe.WaitForStateAsync(TessynClientState.Connected);
		Assert.NotEqual(firstRun, wpf.ServerInfo!.RunId);
		Assert.Equal(host.RunInfo.RunId, wpf.ServerInfo.RunId);

		// The new run knows nothing of the old registration: the client must have registered again by itself, or the host would play every sound twice.
		Assert.True(await BecomesAsync(() => presence.IsPresented(RemoteProtocol.Notifications.SoundChannel)), "WPF did not register its sound with the new daemon run");
		Assert.True(presence.RegistrationsOf("wpf-test") >= 2);

		host.Publish(1);
		Assert.Equal(1, (await wpfProbe.NextNotificationAsync()).Seq);
		Assert.Equal(1, (await otherProbe.NextNotificationAsync()).Seq);
	}
}
