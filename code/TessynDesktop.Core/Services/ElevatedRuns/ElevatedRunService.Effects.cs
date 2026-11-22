using System;
using System.IO;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>What follows a change of an elevated run, always after the lock (FR.15.18, FR.15.19, FR.23.31): the log line, the event, the Focus items, the outcome turn.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ElevatedRunService
{
	private void AfterChange(ElevatedRunModel run)
	{
		LogChange(run);
		SyncItems(run);
		if (run.IsFinal)
			DeliverOutcome(run);

		RaiseChanged(run);
	}

	/// <summary>FR.15.18: one Information line per request and per status change. The command is logged verbatim up to the cut (a run is an audit event).</summary>
	private void LogChange(ElevatedRunModel run) =>
		_log.Information(
			"ElevatedRun {Id:l} {Status:l} session={NodeId:l} \"{Name:l}\" dir={Dir:l} exit={Exit:l} command={Command:l}",
			run.Id, WireName(run.Status), run.RequesterNodeId, run.RequesterName, run.WorkingDir,
			run.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
			Cut(run.Command, Constants.ElevatedRuns.LogCommandLength));

	private void RaiseChanged(ElevatedRunModel run)
	{
		try
		{
			Changed?.Invoke(this, new ElevatedRunChangedEventArgs(run));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "ElevatedRun {Id:l}: a Changed subscriber failed", run.Id);
		}
	}

	// ── Focus items (FR.23.31) ────────────────────────────────────────────────

	/// <summary>Pins the card in the requester's ledger and, for a worker, in the ledger of its top-level supervisor (the mirror makes no sound and no second notification).</summary>
	private void PinItems(ElevatedRunModel run, SessionNodeModel requester, SessionNodeModel? topLevel)
	{
		try
		{
			_attention.Post(requester, [_items.Build(run, ShortIdOf(run.RequesterNodeId), mirrored: false)]);
			if (topLevel != null)
				_attention.Post(topLevel, [_items.Build(run, ShortIdOf(run.RequesterNodeId), mirrored: true)]);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "ElevatedRun {Id:l}: could not pin the Focus item", run.Id);
		}
	}

	/// <summary>A final status closes every item of the run as Resolved with the outcome as its note; any other change only tells the clients (the item list follows through attention.changed).</summary>
	private void SyncItems(ElevatedRunModel run)
	{
		foreach (var nodeId in ItemNodeIds(run))
		{
			try
			{
				if (run.IsFinal)
					_attention.CloseElevatedRunItems(nodeId, _registry.FindModelByNodeId(nodeId), run.Id, _outcome.ResolutionNote(run));
				else
					_attention.NotifyChanged(nodeId);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "ElevatedRun {Id:l}: could not update the Focus items of {NodeId}", run.Id, nodeId);
			}
		}
	}

	private static string[] ItemNodeIds(ElevatedRunModel run) =>
		run.TopLevelNodeId == null ? [run.RequesterNodeId] : [run.RequesterNodeId, run.TopLevelNodeId];

	// ── Notification (FR.23.31) ───────────────────────────────────────────────

	/// <summary>Text only, once per request, also for a worker: the supervisor's mirrored item does not notify again. Routed to the top-level session, the one the user watches.</summary>
	private async System.Threading.Tasks.Task NotifyRequestedAsync(ElevatedRunModel run, SessionNodeModel requester, SessionNodeModel? topLevel)
	{
		try
		{
			await _channels.NotifyAsync(topLevel ?? requester, BuildNotification(run));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "ElevatedRun {Id:l}: the notification failed", run.Id);
		}
	}

	private string BuildNotification(ElevatedRunModel run)
	{
		var cut     = run.Command.Length > Constants.ElevatedRuns.NotificationCommandLength;
		var command = cut ? run.Command[..Constants.ElevatedRuns.NotificationCommandLength] + Constants.ElevatedRuns.NotificationCutNote : run.Command;
		var folder  = Path.GetFileName(run.WorkingDir);

		return string.Join('\n',
			string.Format(Constants.ElevatedRuns.NotificationFormat, run.RequesterName, ShortIdOf(run.RequesterNodeId), run.Reason),
			string.Empty,
			string.Format(Constants.ElevatedRuns.NotificationCommandFormat, command),
			string.Format(Constants.ElevatedRuns.NotificationDirFormat, string.IsNullOrEmpty(folder) ? run.WorkingDir : folder),
			Constants.ElevatedRuns.NotificationApproveLine);
	}

	/// <summary>
	/// The Short Node Id of a session exactly as <c>list_sessions</c> shows it (the shortest unique prefix, at least <see cref="Constants.Agent.ShortNodeIdMinLength"/> characters;
	/// <see cref="SessionListIndex.GetShortId"/>), so the User and the agent name the same session the same way. A session that has left the tree gets the minimum-length prefix.
	/// </summary>
	public string ShortIdOf(string nodeId)
	{
		var index = new SessionListIndex(_registry.SnapshotSessionsForListing());
		return index.Find(nodeId) != null
			? index.GetShortId(nodeId)
			: nodeId.Length > Constants.Agent.ShortNodeIdMinLength ? nodeId[..Constants.Agent.ShortNodeIdMinLength] : nodeId;
	}

	// ── Outcome turn (FR.15.19) ───────────────────────────────────────────────

	/// <summary>
	/// One delivery to the requesting session, once: the claim is the persisted <see cref="ElevatedRunModel.OutcomeDelivered"/> flag. A withdrawal by the agent
	/// delivers nothing, and neither does a request whose session left the tree (logged).
	/// </summary>
	private void DeliverOutcome(ElevatedRunModel run)
	{
		lock (_lock)
		{
			if (!_runs.TryGetValue(run.Id, out var live) || live.OutcomeDelivered)
				return;

			live.OutcomeDelivered = true;
			try { SaveLocked(live); }
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error(ex, "ElevatedRun {Id:l}: could not write request.json", run.Id); }
		}

		if (run is { Status: ElevatedRunStatus.Cancelled, CancelledBy: ElevatedRunCancelledBy.Agent })
			return;

		var session = _registry.FindModelByNodeId(run.RequesterNodeId);
		if (session == null)
		{
			_log.Information("ElevatedRun {Id:l} outcome not delivered: session {NodeId:l} is no longer in the tree", run.Id, run.RequesterNodeId);
			return;
		}

		try
		{
			_scheduler.Value.AddOrMergeElevatedRunDelivery(session.NodeId, run.Id, _outcome.BuildPrompt(run, GetLogPath(run.Id)));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "ElevatedRun {Id:l}: could not hand the outcome to the scheduler", run.Id);
		}
	}
}
