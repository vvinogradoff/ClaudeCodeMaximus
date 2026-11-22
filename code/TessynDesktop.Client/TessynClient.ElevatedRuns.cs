using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<ElevatedListResult> ElevatedListAsync(string? nodeId = null, bool all = false, CancellationToken cancellationToken = default) =>
		CallAsync<ElevatedListResult>(
			RemoteProtocol.ElevatedRuns.ListMethod,
			Params(("nodeId", Node(nodeId)), ("status", Node(all ? RemoteProtocol.ElevatedRuns.ListAll : RemoteProtocol.ElevatedRuns.ListActive))),
			cancellationToken);

	/// <inheritdoc />
	public Task<ElevatedGetResult> ElevatedGetAsync(string requestId, int tailLines = 0, CancellationToken cancellationToken = default) =>
		CallAsync<ElevatedGetResult>(
			RemoteProtocol.ElevatedRuns.GetMethod, Params(("requestId", Node(requestId)), ("tailLines", Node(tailLines))), cancellationToken);

	/// <inheritdoc />
	public Task<ElevatedApproveResult> ElevatedApproveAsync(string requestId, CancellationToken cancellationToken = default) =>
		CallAsync<ElevatedApproveResult>(RemoteProtocol.ElevatedRuns.ApproveMethod, Params(("requestId", Node(requestId))), cancellationToken);

	/// <inheritdoc />
	public Task ElevatedLaunchedAsync(string requestId, CancellationToken cancellationToken = default) =>
		CallAsync<JsonObject>(RemoteProtocol.ElevatedRuns.LaunchedMethod, Params(("requestId", Node(requestId))), cancellationToken);

	/// <inheritdoc />
	public Task ElevatedLaunchFailedAsync(string requestId, bool declined, string? reason = null, CancellationToken cancellationToken = default) =>
		CallAsync<JsonObject>(
			RemoteProtocol.ElevatedRuns.LaunchFailedMethod,
			Params(("requestId", Node(requestId)), ("declined", Node(declined)), ("reason", Node(reason))),
			cancellationToken);

	/// <inheritdoc />
	public Task ElevatedDeclineAsync(string requestId, string? note = null, CancellationToken cancellationToken = default) =>
		CallAsync<JsonObject>(RemoteProtocol.ElevatedRuns.DeclineMethod, Params(("requestId", Node(requestId)), ("note", Node(note))), cancellationToken);

	/// <inheritdoc />
	public Task ElevatedStopAsync(string requestId, CancellationToken cancellationToken = default) =>
		CallAsync<JsonObject>(RemoteProtocol.ElevatedRuns.StopMethod, Params(("requestId", Node(requestId))), cancellationToken);
}
