using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// The elevated-run part of <see cref="FakeTessynClient"/> (tdcp-spec 10.A13): an in-memory set of runs that follows the daemon's rules for the status changes the
/// card causes (approve: pending to launching, only once; decline; launched; launchFailed; stop). Records every call like the rest.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	/// <summary>The runs of the fake daemon, by request id.</summary>
	public Dictionary<string, ElevatedRunDto> ElevatedRuns { get; } = [];

	/// <summary>What <see cref="ElevatedGetAsync"/> answers as the output tail.</summary>
	public string? ElevatedOutputTail { get; set; }

	/// <summary>When set, <see cref="ElevatedApproveAsync"/> throws it before it changes anything (a refused approve).</summary>
	public Exception? ElevatedApproveException { get; set; }

	/// <summary>The launch description <see cref="ElevatedApproveAsync"/> answers; the request id is filled in.</summary>
	public ElevatedLaunchDto? ElevatedLaunch { get; set; }

	/// <summary>The request ids approved, launched, failed, declined and stopped, in order.</summary>
	public List<string> ElevatedApproved { get; } = [];

	public List<(string RequestId, bool Declined, string? Reason)> ElevatedLaunchFailures { get; } = [];

	public List<(string RequestId, string? Note)> ElevatedDeclines { get; } = [];

	public List<string> ElevatedLaunched { get; } = [];

	public List<string> ElevatedStopped { get; } = [];

	/// <summary>The item as the daemon lists it: an elevated-run item carries the run as it is now, and is resolved once the run is final (FR.23.31).</summary>
	private AttentionItemDto WithCurrentElevatedRun(AttentionItemDto item)
	{
		if (item.Action?.Run is not { } known || !ElevatedRuns.TryGetValue(known.Id, out var current))
			return item;

		var final = current.Status > ElevatedRunStatus.Running;
		return item with
		{
			Action = item.Action with { Run = current },
			Status = final && item.Status == AttentionStatus.Open ? AttentionStatus.Resolved : item.Status,
			ClosedUtc = final && item.Status == AttentionStatus.Open ? current.EndedUtc ?? current.CreatedUtc : item.ClosedUtc,
			ResolutionNote = final && item.Status == AttentionStatus.Open ? $"Elevated run {current.Id}: {current.Status}" : item.ResolutionNote,
		};
	}

	/// <summary>Adds or replaces a run.</summary>
	public ElevatedRunDto PutElevatedRun(ElevatedRunDto run) => ElevatedRuns[run.Id] = run;

	/// <summary>Sets a run's status as the daemon would and raises <c>elevated.changed</c>.</summary>
	public void SetElevatedStatus(string requestId, ElevatedRunStatus status, Func<ElevatedRunDto, ElevatedRunDto>? change = null)
	{
		var run = ElevatedRuns[requestId] with { Status = status };
		ElevatedRuns[requestId] = change == null ? run : change(run);
		RaiseNotification(RemoteProtocol.ElevatedRuns.ChangedEvent, new ElevatedChangedPayload(requestId, run.Requester.NodeId, status));
	}

	public Task<ElevatedListResult> ElevatedListAsync(string? nodeId = null, bool all = false, CancellationToken cancellationToken = default)
	{
		Record($"elevated.list:{nodeId ?? "-"}:{(all ? "all" : "active")}");
		var runs = ElevatedRuns.Values
			.Where(r => (nodeId == null || r.Requester.NodeId == nodeId) && (all || r.Status <= ElevatedRunStatus.Running))
			.OrderByDescending(r => r.CreatedUtc)
			.ToList();
		return Task.FromResult(new ElevatedListResult(runs));
	}

	public Task<ElevatedGetResult> ElevatedGetAsync(string requestId, int tailLines = 0, CancellationToken cancellationToken = default)
	{
		Record($"elevated.get:{requestId}:{tailLines}");
		if (!ElevatedRuns.TryGetValue(requestId, out var run))
			throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "Unknown elevated run request.");

		return Task.FromResult(new ElevatedGetResult(run, tailLines > 0 ? ElevatedOutputTail : null));
	}

	public Task<ElevatedApproveResult> ElevatedApproveAsync(string requestId, CancellationToken cancellationToken = default)
	{
		Record($"elevated.approve:{requestId}");
		if (ElevatedApproveException != null)
			throw ElevatedApproveException;

		var run = ElevatedRuns[requestId];
		if (run.Status != ElevatedRunStatus.Pending)
			throw new TessynRpcException(RemoteProtocol.ErrorCodes.DaemonError, string.Format(RemoteProtocol.ElevatedRuns.NoLongerPendingFormat, run.Status.ToString().ToLowerInvariant()));

		ElevatedApproved.Add(requestId);
		SetElevatedStatus(requestId, ElevatedRunStatus.Launching);
		var launch = ElevatedLaunch ?? new ElevatedLaunchDto(requestId, "powershell.exe", "-NoProfile", run.WorkingDir ?? string.Empty, "run-dir", RemoteProtocol.ElevatedRuns.RunAsVerb, RemoteProtocol.ElevatedRuns.StartTimeoutSeconds);
		return Task.FromResult(new ElevatedApproveResult(launch with { RequestId = requestId }));
	}

	/// <summary>When set, <see cref="ElevatedLaunchedAsync"/> throws it (the daemon's refusal of a report that came after the run moved on).</summary>
	public Exception? ElevatedLaunchedException { get; set; }

	public Task ElevatedLaunchedAsync(string requestId, CancellationToken cancellationToken = default)
	{
		Record($"elevated.launched:{requestId}");
		if (ElevatedLaunchedException != null)
			throw ElevatedLaunchedException;
		ElevatedLaunched.Add(requestId);
		return Task.CompletedTask;
	}

	public Task ElevatedLaunchFailedAsync(string requestId, bool declined, string? reason = null, CancellationToken cancellationToken = default)
	{
		Record($"elevated.launchFailed:{requestId}:{declined}");
		ElevatedLaunchFailures.Add((requestId, declined, reason));
		SetElevatedStatus(requestId, declined ? ElevatedRunStatus.DeclinedAtPrompt : ElevatedRunStatus.FailedToStart, r => r with { Failure = declined ? null : reason });
		return Task.CompletedTask;
	}

	public Task ElevatedDeclineAsync(string requestId, string? note = null, CancellationToken cancellationToken = default)
	{
		Record($"elevated.decline:{requestId}");
		ElevatedDeclines.Add((requestId, note));
		SetElevatedStatus(requestId, ElevatedRunStatus.Declined, r => r with { Note = note });
		return Task.CompletedTask;
	}

	public Task ElevatedStopAsync(string requestId, CancellationToken cancellationToken = default)
	{
		Record($"elevated.stop:{requestId}");
		ElevatedStopped.Add(requestId);
		return Task.CompletedTask;
	}
}
