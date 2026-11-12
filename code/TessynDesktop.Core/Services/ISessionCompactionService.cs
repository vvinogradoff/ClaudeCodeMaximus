using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Compacts a session (FR.11.6): sends the transcript to Claude statelessly, rewrites the session file with the answer
/// and detaches the live Claude session. The file is replaced only after a non-empty, error-free answer; every other
/// path leaves it untouched. Shared by the desktop (TurnController) and the remote <c>session.compact</c> method.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionCompactionService
{
	/// <summary>
	/// Compacts <paramref name="node"/> under the per-node turn lock (waits for a running turn) and with the session's
	/// running flag set. Do not call while holding the turn lock of the same node — use
	/// <see cref="CompactWhileHoldingTurnLockAsync"/> then.
	/// </summary>
	Task<SessionCompactionResultModel> CompactAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default);

	/// <summary>
	/// Same as <see cref="CompactAsync"/> for a caller that already holds the node's turn lock (the desktop's after-turn
	/// auto-compact runs inside the send flow): the lock is neither taken nor released, so it cannot deadlock.
	/// </summary>
	Task<SessionCompactionResultModel> CompactWhileHoldingTurnLockAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default);
}
