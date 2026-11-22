namespace TessynDesktop.Models;

/// <summary>
/// Status of an elevated run request (FR.15.18). Wire names are the camelCase member names (<c>declinedAtPrompt</c>, <c>failedToStart</c>, <c>timedOut</c>).
/// <see cref="Pending"/>, <see cref="Launching"/> and <see cref="Running"/> are open; every other status is final and never changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public enum ElevatedRunStatus
{
	Pending,
	Launching,
	Running,
	Completed,
	Declined,
	DeclinedAtPrompt,
	FailedToStart,
	TimedOut,
	Cancelled,
	Expired,
}
