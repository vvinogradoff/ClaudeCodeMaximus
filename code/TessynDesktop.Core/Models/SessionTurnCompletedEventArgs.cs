using System;

namespace TessynDesktop.Models;

/// <summary>
/// Raised by <see cref="Services.ISessionFileService.TurnCompleted"/> (and relayed by
/// <see cref="Services.IDaemonClient.TurnCompleted"/>) once per turn, after the turn's last entry
/// has been appended to its session file (FR.21.22). Both turn paths report it — the headless
/// <c>SessionTurnService.RunTurnAsync</c> and the interactive <c>TurnController.SendAsyncCore</c> —
/// so a subscriber such as the Telegram bridge's answers-only mirror knows which appended entry was
/// a turn's final one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnCompletedEventArgs : EventArgs
{
	public required string FileName { get; init; }

	/// <summary>True when the turn was stopped (desktop Stop, <c>/stop</c>, <c>stop_session</c>, the scheduled-turn timeout).</summary>
	public bool Cancelled { get; init; }

	/// <summary>
	/// The text of the turn's error <c>result</c> event (e.g. a usage-limit message), or null when the
	/// turn did not end in an error. Errors a turn path retries automatically ("No conversation found",
	/// the ISSUE.010 OAuth refresh race) are never reported here.
	/// </summary>
	public string? ErrorText { get; init; }

	/// <summary>The id of the turn that ended (P1.I), when the turn path that reported it has one; null for a path that does not.</summary>
	public string? TurnId { get; init; }
}
