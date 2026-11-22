using System;
using System.Text.Json.Serialization;

namespace TessynDesktop.Models;

/// <summary>
/// An elevated run request (FR.15.18): the content of <c>request.json</c>. It is the only source of what runs: the command, the working directory and the time
/// limit are stored as given (after the checks) and never read from the agent again.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunModel
{
	public string Id { get; set; } = string.Empty;
	public string RequesterNodeId { get; set; } = string.Empty;
	public string RequesterName { get; set; } = string.Empty;

	/// <summary>The top-level supervisor of a worker requester (null for a top-level requester or when the supervisor left the tree).</summary>
	public string? TopLevelNodeId { get; set; }
	public string? TopLevelName { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string WorkingDir { get; set; } = string.Empty;
	public string Command { get; set; } = string.Empty;
	public int TimeoutMinutes { get; set; }
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset ExpiresUtc { get; set; }
	public ElevatedRunStatus Status { get; set; } = ElevatedRunStatus.Pending;

	/// <summary>When the status last changed.</summary>
	public DateTimeOffset StatusUtc { get; set; }

	/// <summary>When the user approved: starts the 5-minute wait for the client's report.</summary>
	public DateTimeOffset? ApprovedUtc { get; set; }

	/// <summary>When the client reported the launch: starts the 60-second wait for <c>started.json</c>.</summary>
	public DateTimeOffset? LaunchedUtc { get; set; }

	public DateTimeOffset? StartedUtc { get; set; }
	public DateTimeOffset? EndedUtc { get; set; }

	/// <summary>When <c>elevated.stop</c> made the <c>stop</c> file: starts the 30-second wait for <c>result.json</c>.</summary>
	public DateTimeOffset? StopRequestedUtc { get; set; }

	public int? ExitCode { get; set; }
	public ElevatedRunCancelledBy? CancelledBy { get; set; }

	/// <summary>The user's decline note (at most 500 characters).</summary>
	public string? Note { get; set; }

	/// <summary>The failure text (at most 500 characters).</summary>
	public string? Failure { get; set; }

	/// <summary>The PID of the wrapper, from <c>started.json</c>: what the daemon looks at to know the run is still alive.</summary>
	public int? WrapperPid { get; set; }

	/// <summary>True once the one outcome delivery (FR.15.19) was handed over, so a restart does not deliver it again.</summary>
	public bool OutcomeDelivered { get; set; }

	/// <summary>True for the final statuses (FR.15.18): they never change.</summary>
	[JsonIgnore]
	public bool IsFinal => Status >= ElevatedRunStatus.Completed;

	/// <summary>True while the request waits for the user or runs (<c>pending</c>, <c>launching</c>, <c>running</c>).</summary>
	[JsonIgnore]
	public bool IsOpen => !IsFinal;

	/// <summary>A copy to hand out, so a caller never sees the record change under it.</summary>
	public ElevatedRunModel Copy() => (ElevatedRunModel)MemberwiseClone();
}
