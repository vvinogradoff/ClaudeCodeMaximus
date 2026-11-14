using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Owns an interactive turn end to end (P1.I): writes the user entry, builds the instruction block, launches claude (one-shot or
/// through the session's streaming process), parses its stream into transcript entries, keeps the node's running flag, retries
/// the two transient failures, handles cancellation and reports <c>TurnCompleted</c>. Extracted unchanged from the Avalonia
/// <c>TurnController</c>; the headless <see cref="ISessionTurnService"/> path coexists and shares the per-node turn lock.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITurnEngine
{
	/// <summary>
	/// Runs one turn. The user entry is appended before the first await, so a caller on a UI thread sees it synchronously. Sends
	/// for one node are serialized by the node's turn lock: a second send is accepted at once and its process waits behind the
	/// first. Returns once the turn has been reported complete; a cancelled turn returns normally with
	/// <see cref="TurnOutcome.Cancelled"/>, any other failure is thrown after the completion was reported.
	/// </summary>
	Task<TurnOutcome> RunAsync(TurnRequest request, ITurnObserver? observer = null, CancellationToken cancellationToken = default);

	/// <summary>Cancels every engine turn of the node, running or queued. Returns true when there was one.</summary>
	bool CancelRuns(string nodeId);

	/// <summary>
	/// Fires a mid-run correction prompt (a toggle changed while Claude is working, FR.11.12) as a one-shot <c>--resume</c> process
	/// against the session's current Claude session id, even in Streaming Session Mode (FR.19.11). Only the session id of its
	/// result is kept; nothing is written to the transcript.
	/// </summary>
	void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt);
}
