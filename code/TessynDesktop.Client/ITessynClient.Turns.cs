using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>The interactive turn methods (P1.I). Loopback only; the daemon announces them with the <c>turn.stream</c> capability.</summary>
public partial interface ITessynClient
{
	/// <summary>
	/// <c>turn.send</c>: starts an interactive turn and returns its id at once. The progress arrives as <c>turn.*</c> notifications
	/// (<see cref="TurnNotificationExtensions"/> reads them); <c>turn.completed</c> ends it. <paramref name="clientRequestId"/> is echoed by <c>turn.started</c>.
	/// </summary>
	Task<TurnSendResult> TurnSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, string? clientRequestId = null, CancellationToken cancellationToken = default);

	/// <summary><c>turn.stop</c>: stops the session's running turn (and a live streaming process).</summary>
	Task<SessionStopResult> TurnStopAsync(string nodeId, CancellationToken cancellationToken = default);
}
