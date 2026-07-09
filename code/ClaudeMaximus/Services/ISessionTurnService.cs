using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Runs headless claude turns against a session node from outside the UI
/// (scheduler, orchestration). Also owns the per-node turn lock used by
/// <c>SessionViewModel.SendAsync</c> to prevent concurrent --resume conflicts.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionTurnService
{
	/// <summary>
	/// Acquires (or creates) the exclusive per-node turn lock.
	/// Both <c>SessionViewModel.SendAsync</c> and <c>RunTurnAsync</c> acquire this
	/// lock to ensure at most one turn runs concurrently per NodeId.
	/// </summary>
	SemaphoreSlim GetTurnLock(string nodeId);

	/// <summary>
	/// Runs a single turn against <paramref name="node"/>: writes the prompt to the session
	/// file, spawns claude, streams events back to the file, and captures the new ClaudeSessionId.
	/// Acquires the per-node turn lock before spawning.
	/// </summary>
	Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Cancels any running turn for the given node by cancelling its CancellationTokenSource.
	/// Returns true if a running turn was found and cancelled.
	/// </summary>
	bool CancelTurn(string nodeId);
}
