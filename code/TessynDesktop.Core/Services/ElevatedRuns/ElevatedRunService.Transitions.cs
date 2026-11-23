using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The status transitions of <see cref="ElevatedRunService"/> (FR.15.18): every one is a compare-and-set under the one lock, and a final status never changes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ElevatedRunService
{
	/// <summary>The allowed edges (FR.15.18). A final status has none.</summary>
	private static readonly IReadOnlyDictionary<ElevatedRunStatus, ElevatedRunStatus[]> Edges = new Dictionary<ElevatedRunStatus, ElevatedRunStatus[]>
	{
		[ElevatedRunStatus.Pending]   = [ElevatedRunStatus.Launching, ElevatedRunStatus.Declined, ElevatedRunStatus.Cancelled, ElevatedRunStatus.Expired],
		[ElevatedRunStatus.Launching] = [ElevatedRunStatus.Running, ElevatedRunStatus.DeclinedAtPrompt, ElevatedRunStatus.FailedToStart],
		[ElevatedRunStatus.Running]   = [ElevatedRunStatus.Completed, ElevatedRunStatus.TimedOut, ElevatedRunStatus.Cancelled, ElevatedRunStatus.FailedToStart],
	};

	public ElevatedRunLaunchModel Approve(string requestId)
	{
		ElevatedRunModel snapshot;
		var expired = false;
		lock (_lock)
		{
			var run = RequireLocked(requestId);
			if (run.Status != ElevatedRunStatus.Pending)
				throw NoLongerPending(run.Status);

			var now = _clock.GetUtcNow();
			expired = now >= run.ExpiresUtc;
			snapshot = expired
				? ApplyLocked(run, ElevatedRunStatus.Expired)
				: ApplyLocked(run, ElevatedRunStatus.Launching, r => r.ApprovedUtc = now);
		}

		AfterChange(snapshot);
		if (expired)
			throw new ElevatedRunException(RemoteProtocol.ElevatedRuns.ExpiredText);

		// Built from the record that was approved, never from request.json or from anything the agent says later (FR.15.19).
		return _launch.Build(snapshot, RunDir(snapshot.Id));
	}

	public void ReportLaunched(string requestId)
	{
		ElevatedRunModel run;
		lock (_lock)
		{
			run = RequireLocked(requestId);
			if (run.Status != ElevatedRunStatus.Launching)
				throw new ElevatedRunException(string.Format(Constants.ElevatedRuns.NotLaunchingFormat, WireName(run.Status)));

			run.LaunchedUtc = _clock.GetUtcNow();
			SaveLocked(run);
		}
	}

	public void ReportLaunchFailed(string requestId, bool declined, string? reason)
	{
		var failure = Cut(string.IsNullOrWhiteSpace(reason) ? Constants.ElevatedRuns.FailureNotStartedText : reason.Trim(), RemoteProtocol.ElevatedRuns.MaxTextLength);
		var target  = declined ? ElevatedRunStatus.DeclinedAtPrompt : ElevatedRunStatus.FailedToStart;
		if (Transition(requestId, ElevatedRunStatus.Launching, target, r => r.Failure = declined ? null : failure) != null)
			return;

		throw new ElevatedRunException(string.Format(Constants.ElevatedRuns.NotLaunchingFormat, WireName(RequireStatus(requestId))));
	}

	public void Decline(string requestId, string? note)
	{
		var text = string.IsNullOrWhiteSpace(note) ? null : Cut(note.Trim(), RemoteProtocol.ElevatedRuns.MaxTextLength);
		if (Transition(requestId, ElevatedRunStatus.Pending, ElevatedRunStatus.Declined, r => r.Note = text) != null)
			return;

		throw NoLongerPending(RequireStatus(requestId));
	}

	public void Stop(string requestId)
	{
		ElevatedRunModel run;
		lock (_lock)
		{
			run = RequireLocked(requestId);
			if (run.Status != ElevatedRunStatus.Running)
				throw new ElevatedRunException(string.Format(Constants.ElevatedRuns.NotRunningFormat, WireName(run.Status)));

			File.WriteAllText(FilePath(run.Id, Constants.ElevatedRuns.StopFileName), string.Empty);
			run.StopRequestedUtc ??= _clock.GetUtcNow();
			SaveLocked(run);
		}

		_log.Information("ElevatedRun {Id:l} stop requested", requestId);
	}

	/// <summary>Moves the run from <paramref name="from"/> to <paramref name="to"/> when it is in that status and the edge is allowed; null (nothing changed) otherwise. The effects run after the lock.</summary>
	private ElevatedRunModel? Transition(string id, ElevatedRunStatus from, ElevatedRunStatus to, Action<ElevatedRunModel>? mutate = null) =>
		Transition(id, [from], to, mutate);

	private ElevatedRunModel? Transition(string id, IReadOnlyCollection<ElevatedRunStatus> from, ElevatedRunStatus to, Action<ElevatedRunModel>? mutate = null)
	{
		ElevatedRunModel snapshot;
		lock (_lock)
		{
			if (!_runs.TryGetValue(id, out var run) || !from.Contains(run.Status) || !IsEdge(run.Status, to))
				return null;

			snapshot = ApplyLocked(run, to, mutate);
		}

		AfterChange(snapshot);
		return snapshot;
	}

	private static bool IsEdge(ElevatedRunStatus from, ElevatedRunStatus to) => Edges.TryGetValue(from, out var targets) && targets.Contains(to);

	/// <summary>Sets the new status and its time, lets <paramref name="mutate"/> fill the other fields, saves the record and returns a copy.</summary>
	private ElevatedRunModel ApplyLocked(ElevatedRunModel run, ElevatedRunStatus to, Action<ElevatedRunModel>? mutate = null)
	{
		var now = _clock.GetUtcNow();
		run.Status    = to;
		run.StatusUtc = now;
		mutate?.Invoke(run);
		if (run.IsFinal)
			run.EndedUtc ??= now;

		try
		{
			SaveLocked(run);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_log.Error(ex, "ElevatedRun {Id:l}: could not write request.json", run.Id);
		}

		return run.Copy();
	}

	private ElevatedRunModel RequireLocked(string id) =>
		_runs.TryGetValue(id, out var run) ? run : throw new ElevatedRunException(Constants.ElevatedRuns.UnknownRequestText, isUnknown: true);

	private ElevatedRunStatus RequireStatus(string id)
	{
		lock (_lock)
			return RequireLocked(id).Status;
	}

	private static ElevatedRunException NoLongerPending(ElevatedRunStatus status) =>
		new(string.Format(RemoteProtocol.ElevatedRuns.NoLongerPendingFormat, WireName(status)));

	/// <summary>The status as the wire writes it (<c>declinedAtPrompt</c>).</summary>
	private static string WireName(ElevatedRunStatus status) => JsonNamingPolicy.CamelCase.ConvertName(status.ToString());
}
