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
	/// start), so this one waits for it.
	/// </summary>
	TurnTicketModel Send(SessionNodeModel node, string text, TurnSettings settings, string? clientRequestId);
}
