using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// The <c>elevated.*</c> methods of elevated runs requested by an agent (tdcp-spec 10.A13, FR.15.16 to FR.15.19, FR.32.16). Loopback only; announced by the
/// <c>elevatedRuns</c> capability. The daemon watches the run: a client only approves or declines, starts what <see cref="ElevatedApproveAsync"/> returned,
/// and reports how that start went.
/// </summary>
public partial interface ITessynClient
{
	/// <summary><c>elevated.list</c>: runs newest first (at most 100); <paramref name="all"/> includes the finished ones, <paramref name="nodeId"/> limits to one session's requests.</summary>
	Task<ElevatedListResult> ElevatedListAsync(string? nodeId = null, bool all = false, CancellationToken cancellationToken = default);

	/// <summary><c>elevated.get</c>: the run and, with <paramref name="tailLines"/> above 0 (at most 200), the last lines of its <c>output.log</c>.</summary>
	Task<ElevatedGetResult> ElevatedGetAsync(string requestId, int tailLines = 0, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>elevated.approve</c>: <c>pending</c> to <c>launching</c>, atomically; only the first call wins. The answer is the launch description the daemon built: start
	/// exactly that (ShellExecute with the verb, hidden). A refused call (no longer pending, expired) is a <see cref="TessynRpcException"/> with the user-facing text.
	/// </summary>
	Task<ElevatedApproveResult> ElevatedApproveAsync(string requestId, CancellationToken cancellationToken = default);

	/// <summary><c>elevated.launched</c>: the launch call returned (the Windows prompt was accepted and the process created); starts the daemon's 60 s wait for <c>started.json</c>.</summary>
	Task ElevatedLaunchedAsync(string requestId, CancellationToken cancellationToken = default);

	/// <summary><c>elevated.launchFailed</c>: <paramref name="declined"/> is true when the user cancelled the Windows prompt (Win32 error 1223); otherwise <paramref name="reason"/> is the system's message.</summary>
	Task ElevatedLaunchFailedAsync(string requestId, bool declined, string? reason = null, CancellationToken cancellationToken = default);

	/// <summary><c>elevated.decline</c>: <c>pending</c> to <c>declined</c>; <paramref name="note"/> (at most 500 characters) goes into the outcome the agent receives.</summary>
	Task ElevatedDeclineAsync(string requestId, string? note = null, CancellationToken cancellationToken = default);

	/// <summary><c>elevated.stop</c>: makes the daemon create the <c>stop</c> file of a running request; the wrapper kills the process tree within a second.</summary>
	Task ElevatedStopAsync(string requestId, CancellationToken cancellationToken = default);
}
