using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The daemon watches the run folder, not the client (FR.15.19): the start at daemon start (reconciliation, FR.18), the poll every 2 s (<c>started.json</c>, <c>result.json</c>,
/// the 60 s, 5 min and 30 s timeouts, the wrapper's process) and the expiry sweep (FR.15.18).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ElevatedRunService
{
	/// <summary>
	/// Loads every run folder, then looks at the open requests (FR.15.18): a <c>pending</c> one stays pending with its expiry; for a <c>launching</c> or <c>running</c>
	/// one a <c>result.json</c> gives the outcome, a <c>started.json</c> whose wrapper still exists resumes the watching, anything else is <c>failedToStart</c>.
	/// A final request whose outcome was never handed over (the daemon died between the two) is delivered now.
	/// </summary>
	public void Reconcile()
	{
		LoadRecords();

		foreach (var run in List(null, all: true, int.MaxValue))
		{
			try
			{
				if (run.IsFinal)
				{
					if (!run.OutcomeDelivered)
						DeliverOutcome(run);
				}
				else if (run.Status is ElevatedRunStatus.Launching or ElevatedRunStatus.Running)
					ReconcileLaunched(run);
			}
			catch (Exception ex)
			{
				_log.Error(ex, "ElevatedRun {Id:l}: reconciliation failed", run.Id);
			}
		}

		Poll();
	}

	private void LoadRecords()
	{
		if (!Directory.Exists(_root))
			return;

		lock (_lock)
		{
			foreach (var dir in Directory.EnumerateDirectories(_root))
			{
				var run = _file.Read<ElevatedRunModel>(Path.Combine(dir, Constants.ElevatedRuns.RequestFileName));
				if (run != null && !string.IsNullOrEmpty(run.Id))
					_runs[run.Id] = run;
			}
		}
	}

	private void ReconcileLaunched(ElevatedRunModel run)
	{
		var result = ReadResult(run.Id);
		var started = ReadStarted(run.Id);
		if (result != null || (started != null && _isProcessAlive(started.Pid)))
		{
			// The normal poll applies started.json and result.json from here on.
			return;
		}

		Transition(run.Id, [ElevatedRunStatus.Launching, ElevatedRunStatus.Running], ElevatedRunStatus.FailedToStart,
			r => r.Failure = Constants.ElevatedRuns.FailureDaemonRestartedText);
	}

	private void PollSafe()
	{
		if (Interlocked.Exchange(ref _polling, 1) == 1)
			return;

		try
		{
			Poll();
		}
		catch (Exception ex)
		{
			_log.Error(ex, "ElevatedRun: poll failed; will retry on the next tick");
		}
		finally
		{
			Volatile.Write(ref _polling, 0);
		}
	}

	/// <summary>One look at every open request: the expiry sweep, the start timeouts, <c>started.json</c>, <c>result.json</c> and the wrapper's process.</summary>
	public void Poll()
	{
		foreach (var run in List(null, all: false, int.MaxValue))
		{
			try
			{
				PollOne(run);
			}
			catch (Exception ex)
			{
				_log.Error(ex, "ElevatedRun {Id:l}: poll failed", run.Id);
			}
		}
	}

	private void PollOne(ElevatedRunModel run)
	{
		var now = _clock.GetUtcNow();
		switch (run.Status)
		{
			case ElevatedRunStatus.Pending:
				if (now >= run.ExpiresUtc)
					Transition(run.Id, ElevatedRunStatus.Pending, ElevatedRunStatus.Expired);
				return;

			case ElevatedRunStatus.Launching:
				if (ReadStarted(run.Id) is { } started)
				{
					if (Transition(run.Id, ElevatedRunStatus.Launching, ElevatedRunStatus.Running, r => ApplyStarted(r, started)) is { } running)
						PollRunning(running, now);
				}
				else
					CheckStartTimeouts(run, now);
				return;

			case ElevatedRunStatus.Running:
				PollRunning(run, now);
				return;
		}
	}

	private void CheckStartTimeouts(ElevatedRunModel run, DateTimeOffset now)
	{
		if (run.LaunchedUtc is { } launched)
		{
			if (now - launched >= Constants.ElevatedRuns.StartedFileTimeout)
				Transition(run.Id, ElevatedRunStatus.Launching, ElevatedRunStatus.FailedToStart, r => r.Failure = Constants.ElevatedRuns.FailureNotStartedText);
			return;
		}

		// An open Windows prompt blocks the client's launch call for as long as the user takes: only the silence of the client is limited.
		if (run.ApprovedUtc is { } approved && now - approved >= Constants.ElevatedRuns.ClientReportTimeout)
			Transition(run.Id, ElevatedRunStatus.Launching, ElevatedRunStatus.FailedToStart, r => r.Failure = Constants.ElevatedRuns.FailureClientSilentText);
	}

	private void PollRunning(ElevatedRunModel run, DateTimeOffset now)
	{
		if (ReadResult(run.Id) is { } result)
		{
			ApplyResult(run, result);
			return;
		}

		if (run.StopRequestedUtc is { } stopped && now - stopped >= Constants.ElevatedRuns.StopGracePeriod)
		{
			// The wrapper did not confirm: the card must not stay open forever.
			if (Transition(run.Id, ElevatedRunStatus.Running, ElevatedRunStatus.Cancelled, r =>
			    {
				    r.CancelledBy = ElevatedRunCancelledBy.User;
				    r.Failure     = Constants.ElevatedRuns.StopNotConfirmedText;
			    }) != null)
				_log.Warning("ElevatedRun {Id:l}: {Text:l}", run.Id, Constants.ElevatedRuns.StopNotConfirmedText);
			return;
		}

		if (run.WrapperPid is not { } pid || _isProcessAlive(pid))
			return;

		// The wrapper writes result.json last and then exits: look once more before calling the run lost.
		if (ReadResult(run.Id) is { } late)
			ApplyResult(run, late);
		else
			Transition(run.Id, ElevatedRunStatus.Running, ElevatedRunStatus.FailedToStart, r => r.Failure = Constants.ElevatedRuns.FailureNoResultText);
	}

	private void ApplyResult(ElevatedRunModel run, ElevatedRunResultFileModel result)
	{
		var to = result.Ended switch
		{
			Constants.ElevatedRuns.EndedTimedOut => ElevatedRunStatus.TimedOut,
			Constants.ElevatedRuns.EndedStopped  => ElevatedRunStatus.Cancelled,
			_                                    => ElevatedRunStatus.Completed,
		};

		Transition(run.Id, ElevatedRunStatus.Running, to, r =>
		{
			r.ExitCode = result.ExitCode;
			r.EndedUtc = result.EndedUtc;
			if (to == ElevatedRunStatus.Cancelled)
				r.CancelledBy = ElevatedRunCancelledBy.User;
		});
	}

	private static void ApplyStarted(ElevatedRunModel run, ElevatedRunStartedFileModel started)
	{
		run.StartedUtc = started.StartedUtc;
		run.WrapperPid = started.Pid;
	}

	private ElevatedRunStartedFileModel? ReadStarted(string id) => ReadWrapperFile<ElevatedRunStartedFileModel>(id, Constants.ElevatedRuns.StartedFileName);

	private ElevatedRunResultFileModel? ReadResult(string id) => ReadWrapperFile<ElevatedRunResultFileModel>(id, Constants.ElevatedRuns.ResultFileName);

	/// <summary>A file the wrapper wrote (camelCase JSON). Written atomically, so a half-written file does not occur; an unreadable one counts as absent.</summary>
	private T? ReadWrapperFile<T>(string id, string name) where T : class
	{
		var path = FilePath(id, name);
		if (!File.Exists(path))
			return null;

		try
		{
			return JsonSerializer.Deserialize<T>(File.ReadAllText(path).TrimStart('﻿'), TdcpJsonOptions.Default);
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			_log.Debug(ex, "ElevatedRun {Id:l}: {File:l} is not readable yet", id, name);
			return null;
		}
	}
}
