using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	private IReadOnlyList<string>? _registeredPresents;

	/// <summary>The channels to register with a hello: the last <see cref="ClientRegisterAsync"/>, else the options.</summary>
	private IReadOnlyList<string> HelloPresents()
	{
		lock (_gate)
			return string.IsNullOrEmpty(_options.ClientId) ? [] : _registeredPresents ?? _options.Presents;
	}

	/// <inheritdoc />
	public Task<NotificationAckResult> NotificationAckAsync(string notificationId, CancellationToken cancellationToken = default) =>
		CallAsync<NotificationAckResult>(RemoteProtocol.Notifications.AckMethod, Params(("notificationId", Node(notificationId))), cancellationToken);

	/// <inheritdoc />
	public Task<NotificationAckResult> NotificationSoundAckAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<NotificationAckResult>(RemoteProtocol.Notifications.AckMethod, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public async Task<ClientRegisterResult> ClientRegisterAsync(IReadOnlyList<string> presents, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_options.ClientId))
			throw new InvalidOperationException("ClientRegisterAsync needs TessynClientOptions.ClientId.");

		var result = await CallAsync<ClientRegisterResult>(
			RemoteProtocol.Notifications.RegisterMethod,
			Params(
				("clientId", Node(_options.ClientId)),
				("presents", new JsonArray([.. presents.Select(p => (JsonNode?)JsonValue.Create(p))]))),
			cancellationToken).ConfigureAwait(false);

		lock (_gate)
			_registeredPresents = [.. presents];
		return result;
	}
}
