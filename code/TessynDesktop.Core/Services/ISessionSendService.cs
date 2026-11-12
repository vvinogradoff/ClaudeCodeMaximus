using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Starts a headless turn for a remote client and returns at once: the turn runs in the background through
/// <see cref="ISessionTurnService"/> (source <see cref="TurnSource.Web"/>), its failures are logged and, as for every
/// headless turn, written to the transcript and reported with turn.completed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionSendService
{
	/// <summary>
	/// Starts the turn. Returns true when it has to wait because the session is running a turn (or another web
	/// turn is already waiting for its start), false when it starts right away.
	/// </summary>
	bool Send(SessionNodeModel node, string text, TurnOptionsModel? options);
}
