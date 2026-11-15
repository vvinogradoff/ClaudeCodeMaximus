using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>A real <see cref="RelayConnectorService"/> over the harness dispatcher and bus, dialling the given server.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayLink : IDisposable
{
	/// <summary>Reconnect fast so link-drop tests do not wait for the production 1 s / 2 s / 5 s ladder.</summary>
	public static readonly RelayConnectorTimings FastTimings = new(
		TimeSpan.FromSeconds(45),
		TimeSpan.FromMilliseconds(250),
		TimeSpan.FromSeconds(60),
		[TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500)]);

	private readonly InMemoryRelayConfigStore _store;

	public RelayLink(TdcpDispatcher dispatcher, IRemoteEventBus bus, string url, string token, Func<HttpClient> clientFactory, RelayBlobGateway? blobGateway = null)
	{
		_store = new InMemoryRelayConfigStore(new RelayConfigModel { Enabled = true, Url = url, Token = token });
		Connector = new RelayConnectorService(_store, dispatcher, bus, new DelegateHttpClientFactory(clientFactory), FastTimings, blobGateway: blobGateway);
	}

	public RelayConnectorService Connector { get; }

	public void Start() => Connector.Start();

	public void Stop() => Connector.Stop();

	public async Task WaitForStateAsync(RelayState state, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
		while (Connector.Status.State != state)
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException($"Connector stayed {Connector.Status.State} (error: {Connector.Status.LastError}), wanted {state}");

			await Task.Delay(25);
		}
	}

	public void Dispose() => Connector.Stop();
}
