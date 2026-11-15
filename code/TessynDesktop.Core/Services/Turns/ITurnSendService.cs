using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Starts an engine turn for a client of the wire and returns at once: the turn runs in the background through <see cref="ITurnEngine"/>,
/// its failures are logged and, as for every turn, reported with <c>turn.completed</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITurnSendService
{
	/// <summary>
	/// Accepts the turn. <see cref="TurnTicketModel.Queued"/> is true when the session is running a turn (or another one is waiting for its
	/// start), so this one waits for it. <paramref name="newBranch"/> and <paramref name="autoCompact"/> are the one-shot toggles of this send
	/// (FR.11.4, FR.11.6), carried with the queued send so that they apply to it and to no other; with an empty <paramref name="text"/> and
	/// <paramref name="autoCompact"/> the session is only compacted.
	/// </summary>
	TurnTicketModel Send(SessionNodeModel node, string text, TurnSettings settings, string? clientRequestId, bool newBranch = false, bool autoCompact = false);
}
