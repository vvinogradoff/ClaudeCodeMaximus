using TessynDesktop.Client;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Builds the real <see cref="TessynClient"/> from the discovery result. The client names itself with a stable per-install id and presents the
/// completion sound (W1b), so the desktop host does not play it while this client is connected. Toasts are added by the notification package.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynClientFactory : ITessynClientFactory
{
	private const string ClientIdPrefix = "wpf-";

	private readonly IClientStateStore _state;

	public TessynClientFactory(IClientStateStore state)
	{
		_state = state;
	}

	public ITessynClient Create(DaemonEndpoint endpoint) =>
		new TessynClient(endpoint, new TessynClientOptions
		{
			ClientName = Constants.Identity.ClientName,
			ClientVersion = Constants.Identity.ClientVersion,
			ClientId = ClientId(),
			Presents = [Constants.SoundRules.Channel],
		});

	private string ClientId()
	{
		var existing = _state.Read(s => s.ClientId);
		if (!string.IsNullOrWhiteSpace(existing))
			return existing;

		var created = ClientIdPrefix + Guid.NewGuid().ToString("N");
		_state.Update(s => s.ClientId ??= created);
		return _state.Read(s => s.ClientId) ?? created;
	}
}
