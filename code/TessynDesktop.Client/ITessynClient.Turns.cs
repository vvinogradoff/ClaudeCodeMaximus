using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>The interactive turn methods (P1.I). Loopback only; the daemon announces them with the <c>turn.stream</c> capability.</summary>
public partial interface ITessynClient
{
	/// <summary>
	/// <c>turn.send</c>: starts an interactive turn and returns its id at once. The progress arrives as <c>turn.*</c> notifications
	/// (<see cref="TurnNotificationExtensions"/> reads them); <c>turn.completed</c> ends it. <paramref name="clientRequestId"/> is echoed by <c>turn.started</c>.
	/// <paramref name="newBranch"/> and <paramref name="autoCompact"/> are the one-shot toggles of this send (capability <c>turn.options</c>): New Branch adds its line to
	/// the instruction block, Auto-Compact compacts the session after the answer, under the turn lock. They are per run: nothing is remembered by the daemon, and a queued
	/// send keeps the ones it was sent with. An empty <paramref name="text"/> with <paramref name="autoCompact"/> sends no turn and only compacts.
	/// </summary>
	Task<TurnSendResult> TurnSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, string? clientRequestId = null, bool newBranch = false, bool autoCompact = false, CancellationToken cancellationToken = default);

	/// <summary><c>turn.stop</c>: stops the session's running turn (and a live streaming process).</summary>
	Task<SessionStopResult> TurnStopAsync(string nodeId, CancellationToken cancellationToken = default);
}
