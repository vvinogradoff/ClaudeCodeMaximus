using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<TurnSendResult> TurnSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, string? clientRequestId = null, CancellationToken cancellationToken = default) =>
		CallAsync<TurnSendResult>(
			RemoteProtocol.TurnStream.SendMethod,
			Params(("nodeId", Node(nodeId)), ("text", Node(text)), ("modelId", Node(modelId)), ("effort", Node(effort)), ("profileId", Node(profileId)), ("clientRequestId", Node(clientRequestId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SessionStopResult> TurnStopAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<SessionStopResult>(RemoteProtocol.TurnStream.StopMethod, Params(("nodeId", Node(nodeId))), cancellationToken);
}
